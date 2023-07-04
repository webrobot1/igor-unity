using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

using WebGLSupport;

namespace MyFantasy
{
	/// <summary>
	/// Класс для обработки запросов, конект
	/// </summary>
	abstract public class UpdateController : MapController
	{
		protected override void Handle(string json)
		{		
			HandleData(JsonConvert.DeserializeObject<Recive<ObjectRecive, ObjectRecive, ObjectRecive>>(json));				
		}

		/// <summary>
		/// Обработка пришедших от сервера значений
		/// </summary>
		protected virtual void HandleData<P,E,O>(Recive<P, E, O> recive) where P : ObjectRecive where E : ObjectRecive where O : ObjectRecive
		{
			if (recive.action != null)
			{
				switch (recive.action)
				{
					case ACTION_LOAD:
						Debug.LogWarning("полная перезагрузка мира");

						// удаляет не сразу а на следующем кадре все карты
						// главное не через for  от количества детей делать DestroyImmediate - тк количество детей пропорционально будет уменьшаться
						foreach (var side in base.worldObject.transform.Cast<Transform>().ToList())
						{ 
							foreach (var child in side.transform.Cast<Transform>().ToList())
							{
								if (player == null || child.gameObject.name != player.gameObject.name)
								{
									DestroyImmediate(child.gameObject);
								}
								else
									Debug.Log("Не очищаем игрока при перезагрузке");
							}
						}	
					break;
				}
			}

			if (recive.sides != null)
			{
				UpdateSides(recive.sides);
			}

			if (recive.world != null)
			{
				Debug.Log("Обрабатываем мир");
				foreach (var map in recive.world)
				{
					// найдем карту на сцене для которых пришло обнолление. если пусто - создадим ее
					Transform map_zone = base.worldObject.transform.Find(this.sides[map.Key]);
					if (map_zone == null)
					{
						map_zone = new GameObject(this.sides[map.Key]).transform;
						map_zone.SetParent(base.worldObject.transform, false);
						Debug.LogWarning("Создаем область для объектов " + map.Key+ "("+ this.sides[map.Key] + ")");
					}

					// если пришел пустой обхект (массив)  то надо все удалить с зоны карты все электменты 
					if (map.Value.players == null && map.Value.enemys == null && map.Value.objects == null)
					{
						Debug.LogWarning("локация " + map.Key + " отправила пустое содержимое - удалим ее объекты с карты");

						// если саму зону оставить надо
						/*foreach (var child in map_zone.Cast<Transform>().ToList())
						{
							DestroyImmediate(child.gameObject);
						}*/

						DestroyImmediate(map_zone.gameObject);
					}
					else
					{
						if (map.Value.players != null)
						{
							//Debug.Log("Обновляем игроков");
							foreach (var player in map.Value.players)
							{
								UpdateObject(map.Key, player.Key, player.Value, "Players");
							}
						}

						// если есть враги
						if (map.Value.enemys != null)
						{
							//Debug.Log("Обновляем enemy");
							foreach (var enemy in map.Value.enemys)
							{
								UpdateObject(map.Key, enemy.Key, enemy.Value, "Enemys");
							}
						}

						// если есть объекты
						if (map.Value.objects != null)
						{
							//Debug.Log("Обновляем объекты");
							foreach (var obj in map.Value.objects)
							{
								UpdateObject(map.Key, obj.Key, obj.Value, "Objects");
							}
						}
					}
				}
			}
		}


		/// <summary>
		/// Обработка пакета - с какой стороны какая ID карты на сцене
		/// </summary>
		private void UpdateSides(Dictionary<int, string> sides)
		{
			Debug.Log("Обрабатываем стороны карт");

			if (!sides.ContainsValue("center")) Error("Запись о центральной карте не пришла");

			mapObject.SetActive(false);
			worldObject.SetActive(false);

			// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
			if (this.maps.Count > 0)
			{
				foreach (Transform grid in mapObject.transform)
				{
					int old_side = this.sides.FirstOrDefault(x => x.Value == grid.name).Key;

                    if (sides.ContainsKey(old_side))
                    {
						Debug.Log("подмена карты с " + this.sides[old_side] + " на " + sides[old_side]);
						grid.name = sides[old_side];
                    }
					else
					{
						Debug.Log("уничтожаем неиспользуемую карту " + old_side);
						DestroyImmediate(mapObject.transform.Find(this.sides[old_side]).gameObject);
						maps.Remove(old_side);
					}				
				}
			}

			this.sides = sides;
			SortMap();
			
			mapObject.SetActive(true);
			worldObject.SetActive(true);


			// загрузим отвутвующую графику центральной и смежных карт 
			// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
			foreach (KeyValuePair<int, string> side in sides)
			{
				if (!maps.ContainsKey(side.Key)) StartCoroutine(GetMap(side.Key));
			}
		}

		/// <summary>
		/// обработка кокнретной сущности (создание и обновлелние)
		/// </summary>
		protected virtual GameObject UpdateObject(int map_id, string key, ObjectRecive recive, string type)
		{
			Debug.Log("Обрабатываем "+type+" "+key+" на карте "+ map_id);

			GameObject prefab = GameObject.Find(key);
			ObjectModel model;

			// если игрока нет на сцене
			if (prefab == null)
			{
				Debug.Log("Создаем " + recive.prefab + " " + key);

				UnityEngine.Object ob = Resources.Load("Prefabs/" + type + "/" + recive.prefab, typeof(GameObject));

				if (ob == null)
					ob = Resources.Load("Prefabs/" + type + "/Empty", typeof(GameObject));

				if (ob == null) Error("Отсутвует префаб Empty (по умолчанию) для объекта типа " + type);

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;

				model = prefab.GetComponent<ObjectModel>();
				if (model == null) Error("Отсутвует скрипт модели на объекте " + key);

				model.type = type.ToLower();

				if (key == player_key)
				{
					player = model;
				}

				// мы сортировку устанавливаем в двух местах - здесь и при загрузке карты. тк объекты могут быть загружены раньше карты и наоборот
				if (maps.ContainsKey(map_id))
				{
					if (prefab.GetComponent<SpriteRenderer>())
						prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[map_id].spawn_sort + model.sort;
					if (prefab.GetComponentInChildren<Canvas>())
						prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[map_id].spawn_sort + 1 + model.sort;
				}
			}
            else 
			{
				model = prefab.GetComponent<ObjectModel>();
			}

			prefab.transform.SetParent(worldObject.transform.Find(this.sides[map_id]).transform, false);

			try
			{
				model.SetData(recive);
			}
			catch (Exception ex)
			{
				Error("Не удалось загрузить " + key, ex);
			}

			return prefab;
		}	
	}
}