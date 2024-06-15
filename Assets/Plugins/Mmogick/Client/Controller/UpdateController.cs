using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Класс для обновления данных существ с сервера
	/// </summary>
	abstract public class UpdateController : MapController
	{
		protected override void Handle(string json)
		{		
			HandleData(JsonConvert.DeserializeObject<Recive<EntityRecive, EntityRecive, EntityRecive>>(json));				
		}

		/// <summary>
		/// Обработка пришедших от сервера значений
		/// </summary>
		protected override void HandleData<P,E,O>(Recive<P, E, O> recive)
		{
			base.HandleData(recive);

			if (recive.action != null)
			{
				switch (recive.action)
				{
					case ACTION_LOAD:
						Debug.LogWarning("WebSocket: полная перезагрузка мира");

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
									player.Log("Не очищаем игрока при перезагрузке");
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

						Debug.LogWarning("WebSocket: Создаем область для объектов " + map.Key);
					}

					// если пришел пустой обхект (массив)  то надо все удалить с зоны карты все электменты 
					if (map.Value.players == null && map.Value.enemys == null && map.Value.objects == null)
					{
						Debug.LogWarning("WebSocket: локация " + map.Key + " отправила пустое содержимое - удалим ее объекты с карты");

						// если саму зону оставить надо
						foreach (Transform child in map_zone.transform)
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
		}

		/// <summary>
		/// обработка кокнретной сущности (создание и обновлелние)
		/// </summary>
		protected virtual GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
		{
			GameObject prefab = GameObject.Find(key);
			EntityModel model;

			// если игрока нет на сцене
			if (prefab == null)
			{
				// те что удалились как только мы пришли тех не создаем тк нет пакета с чего их создавать 
				if (recive.action == "remove") 
					return null;

				UnityEngine.Object ob = null;

				if (recive.prefab != null)
					ob = Resources.Load("Prefabs/" + type + "/" + recive.prefab, typeof(GameObject));

				if (recive.prefab == null || ob == null)
                {
					Debug.LogError("Отсутвует и префаб Prefabs / " + type + " / " + recive.prefab+" у существа "+key);
					ob = Resources.Load("Prefabs/" + type + "/Unknow", typeof(GameObject));
				}
					
				if (ob == null)
				{
					Error("WebSocket: Отсутвует и префаб Prefabs/" + type + "/" + recive.prefab+ " и Prefabs/" + type + "/Unknow для объекта " + key);
					return null;
				}

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;

				model = prefab.GetComponent<EntityModel>();
				if (model == null)
				{
					Error("WebSocket: Отсутвует скрипт модели на объекте " + key);
					return null;
				}
				
				model.key = key;
				model.Log("создан с префабом " + recive.prefab);

				model.type = type.ToLower();

				if (key == player_key)
				{
					player = model;
					if (!getSides().ContainsKey(map_id)) 
						Error("Запись о карте "+ map_id + " игрока не пришла вместе с доступными сторонами");

					#if UNITY_WEBGL && !UNITY_EDITOR
						WebGLSupport.WebGLDebug.DebugCheck(map_id);
					#endif
				}

				// мы сортировку устанавливаем в двух местах - здесь и при загрузке карты. тк объекты могут быть загружены раньше карты и наоборот
				if (getMaps().ContainsKey(map_id))
				{
					if (prefab.GetComponent<SpriteRenderer>())
						prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)getMaps()[map_id].spawn_sort + model.sort;
					if (prefab.GetComponentInChildren<Canvas>())
						prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)getMaps()[map_id].spawn_sort + 1 + model.sort;
				}
			}
            else 
			{
				model = prefab.GetComponent<EntityModel>();
			}

			model.Log("Обрабатываем на карте " + map_id + " пакетом " + JsonConvert.SerializeObject(recive, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
			prefab.transform.SetParent(worldObject.transform.Find(map_id.ToString()).transform, false);

			try
			{
				if (recive.prefab!=null && model.prefab != recive.prefab)
                {
					StartCoroutine(Patcher.GetAnimation(SERVER, GAME_ID, player_token, recive.prefab, (Patcher patcher) =>
					{
						if (patcher.error != null)
							Error("Анимации: ошибка " + patcher.error);
						if (patcher.result == null || patcher.result.Length == 0)
							Error("Анимации: пришел пустой ответ от патчера");
						else
						{
							Debug.Log("Анимации: декодируем пакет анимации для " + key);
							try
							{
								SpriterPacket packet;
								using (MemoryStream source = new MemoryStream(System.Convert.FromBase64String(patcher.result)))
								{
									using (MemoryStream target = new MemoryStream())
									{
										using (var decompressStream = new GZipStream(source, CompressionMode.Decompress))
										{
											decompressStream.CopyTo(target);
										}

										Debug.Log("Карты: Парсим карту");
										packet = JsonConvert.DeserializeObject<SpriterPacket>(Encoding.UTF8.GetString(target.ToArray()));
									}
								}

								Debug.Log("Анимации: обновляем анмиацию " + packet.xml);
								NewSpriterRuntimeImporter.CreateSpriter(packet, key);
							}
							catch (Exception ex)
							{
								Error("Анимации: ошибка " + ex);
							}
						}
					}));
				}
				model.SetData(recive);
			}
			catch (Exception ex)
			{
				Error("WebSocket: Не удалось загрузить " + key, ex);
				return null;
			}

			return prefab;
		}	
	}
}