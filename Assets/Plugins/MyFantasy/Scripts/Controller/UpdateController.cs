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
	public abstract class UpdateController : MapController
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
					case "load/index":

						// удаляет не сразу а на следующем кадре все карты
						// главное не через for  от количества детей делать DestroyImmediate - тк количество детей пропорционально будет уменьшаться
						foreach (var child in base.worldObject.transform.Cast<Transform>().ToList())
						{
							DestroyImmediate(child.gameObject);
						}
						Debug.LogWarning("полная перезагрузка мира");
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
					Transform map_zone = base.worldObject.transform.Find(map.Key);
					if (map_zone == null)
					{
						map_zone = new GameObject(map.Key).transform;
						map_zone.SetParent(base.worldObject.transform, false);
						Debug.LogWarning("Создаем область для объектов " + map.Key);
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
							Debug.Log("Обновляем игроков");
							foreach (var player in map.Value.players)
							{
								UpdateObject(map.Key, player.Key, player.Value, "Players");
							}
						}

						// если есть враги
						if (map.Value.enemys != null)
						{
							Debug.Log("Обновляем enemy");
							foreach (var enemy in map.Value.enemys)
							{
								UpdateObject(map.Key, enemy.Key, enemy.Value, "Enemys");
							}
						}

						// если есть объекты
						if (map.Value.objects != null)
						{
							Debug.Log("Обновляем объекты");
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
		/// обработка кокнретной сущности (создание и обновлелние)
		/// </summary>
		protected virtual void UpdateObject(string side, string key, ObjectRecive recive, string type)
		{
			Debug.Log("Обрабатываем "+type+" "+key+" на карте "+side);

			GameObject prefab = GameObject.Find(key);

			// если игрока нет на сцене
			if (prefab == null)
			{
				// если игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
				if (recive.prefab == null || recive.prefab.Length == 0)
				{
					return;
				}

				Debug.Log("Создаем " + recive.prefab + " " + key);

				UnityEngine.Object ob = Resources.Load("Prefabs/" + type + "/" + recive.prefab, typeof(GameObject));

				if (ob == null)
					ob = Resources.Load("Prefabs/" + type + "/Empty", typeof(GameObject));

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;
				prefab.transform.SetParent(worldObject.transform.Find(side).transform, false);

				if (key == player_key)
				{
					player = prefab.GetComponent<ObjectModel>();
				}
			}
			//else
			//	Debug.Log("Обновляем " + key);

			// мы сортировку устанавливаем в двух местах - здесь и при загрузке карты. тк объекты могут быть загружены раньше карты и наоборот
			if (maps.ContainsKey(side) && recive.sort != null)
			{
				if (prefab.GetComponent<SpriteRenderer>())
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[side].spawn_sort + (int)recive.sort;
				if (prefab.GetComponentInChildren<Canvas>())
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[side].spawn_sort + 1 + (int)recive.sort;
			}

			try
			{
				var model = prefab.GetComponent<ObjectModel>();
				if (model != null)
					model.SetData(recive);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Error("Не удалось загрузить " + key);
			}
		}

		/// <summary>
		/// Обработка пакета - с какой стороны какая ID карты на сцене
		/// </summary>
		protected virtual void UpdateSides(Dictionary<string, int> sides)
		{
			Debug.Log("Обрабатываем стороны карт");

			if (!sides.ContainsKey("center")) Error("Запись о центральной карте не пришла");
			this.sides = sides;

			// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
			if (this.maps.Count > 0)
			{
				bool find;
				Dictionary<string, MapDecode> new_maps = new Dictionary<string, MapDecode>();

				// сначала 
				foreach (KeyValuePair<string, int> side in this.sides)
				{
					find = false;
					foreach (KeyValuePair<string, MapDecode> map in maps)
					{
						if (map.Value.map_id == side.Value)
						{
							if (map.Key != side.Key)
							{
								Debug.Log("подмена карты с " + map.Key + " на " + side.Key);
								mapObject.transform.Find(map.Key).gameObject.name = side.Key;
								new_maps.Add(side.Key, map.Value);
							}
							find = true;
						}
					}

					if (!find && mapObject.transform.Find(side.Key))
						DestroyImmediate(mapObject.transform.Find(side.Key).gameObject);
				}

				if (new_maps.Count > 0)
				{
					maps = new_maps;
					SortMap();
				}
			}

			// загрузим отвутвующую графику центральной и смежных карт 
			// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
			foreach (KeyValuePair<string, int> side in this.sides)
			{
				if (!maps.ContainsKey(side.Key)) StartCoroutine(base.GetMap(side.Key));
			}
		}
	}
}