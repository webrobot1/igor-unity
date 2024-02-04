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
						foreach (var side in worldObject.transform.Cast<Transform>().ToList())
						{ 
							foreach (var child in side.transform.Cast<Transform>().ToList())
							{
								if (player == null || child.gameObject.name != player.gameObject.name)
								{
									DestroyImmediate(child.gameObject);
								}
                                else
                                {
									Debug.Log("Не очищаем игрока при перезагрузке");
                                }	
							}
						}	
					break;
				}
			}


			if (recive.world != null)
			{
				Debug.Log("Обрабатываем мир");
				foreach (var map in recive.world)
				{
					// найдем карту на сцене для которых пришло обнолление. если пусто - создадим ее
					Transform map_zone = worldObject.transform.Find(map.Key.ToString());
					if (map_zone == null)
					{
						map_zone = new GameObject(map.Key.ToString()).transform;
						map_zone.SetParent(worldObject.transform, false);

						if (mapObject.transform.Find(map.Key.ToString()) !=null)
							map_zone.localPosition = mapObject.transform.Find(map.Key.ToString()).localPosition;

						Debug.LogWarning("Создаем область для объектов " + map.Key);
					}

					// если пришел пустой обхект (массив)  то надо все удалить с зоны карты все электменты 
					if (map.Value.players == null && map.Value.enemys == null && map.Value.objects == null)
					{
						Debug.LogWarning("локация " + map.Key + " отправила пустое содержимое - удалим ее объекты с карты");

						// если саму зону оставить надо
						foreach (var child in map_zone.Cast<Transform>().ToList())
						{
							DestroyImmediate(child.gameObject);
						}

						//DestroyImmediate(map_zone.gameObject);
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

			if (recive.sides != null)
			{
				UpdateSides(recive.sides);
			}
		}


		/// <summary>
		/// Обработка пакета - с какой стороны какая ID карты на сцене
		/// </summary>
		private void UpdateSides(Dictionary<int, Point> sides)
		{
			Debug.Log("Обрабатываем стороны карт");

			if (player == null) Error("Нельзя обновить карты ДО того как обновили данные игрока");
			if (!sides.ContainsKey(player.map_id)) Error("Запись о карте игрока не пришла");
			

			// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
			if (this.maps.Count > 0)
			{
				foreach (Transform grid in mapObject.transform)
				{
					int map_id = Int32.Parse(grid.name);
                    if (!sides.ContainsKey(map_id))
                    {
						Debug.Log("уничтожаем неиспользуемую карту " + map_id);
						DestroyImmediate(mapObject.transform.Find(map_id.ToString()).gameObject);
						DestroyImmediate(worldObject.transform.Find(map_id.ToString()).gameObject);

						maps.Remove(map_id);
					}				
				}
			}

			this.sides = sides;
			SortMap();


			// загрузим отвутвующую графику центральной и смежных карт 
			// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
			foreach (KeyValuePair<int, Point> side in sides)
			{
				if (!maps.ContainsKey(side.Key)) StartCoroutine(DownloadMap(side.Key));
			}
		}

		/// <summary>
		/// обработка кокнретной сущности (создание и обновлелние)
		/// </summary>
		protected virtual GameObject UpdateObject(int map_id, string key, ObjectRecive recive, string type)
		{
			GameObject prefab = GameObject.Find(key);
			ObjectModel model;

			// если игрока нет на сцене
			if (prefab == null)
			{
				// те что удалились как только мы пришли тех не создаем тк нет пакета с чего их создавать 
				if (recive.action == "remove") 
					return null;

				UnityEngine.Object ob = Resources.Load("Prefabs/" + type + "/" + recive.prefab, typeof(GameObject));

				if (ob == null)
					ob = Resources.Load("Prefabs/" + type + "/Empty", typeof(GameObject));

				if (ob == null) Error("Отсутвует префаб Empty (по умолчанию) для объекта "+ key + " типа " + type);

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;

				model = prefab.GetComponent<ObjectModel>();
				if (model == null) Error("Отсутвует скрипт модели на объекте " + key);
				
				model.key = key;
				model.Log("создан с префабом " + recive.prefab);

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

			model.Log("Обрабатываем на карте " + map_id + " пакетом " + JsonConvert.SerializeObject(recive, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

			prefab.transform.SetParent(worldObject.transform.Find(map_id.ToString()).transform, false);

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