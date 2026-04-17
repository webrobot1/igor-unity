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
				if (EntityModel.verbose) Debug.Log("Обрабатываем мир");
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
					if (map.Value.player == null && map.Value.enemy == null && map.Value.objects == null && map.Value.animal == null)
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
						if (map.Value.player != null)
						{
							//Debug.Log("Обновляем игроков");
							foreach (var player in map.Value.player)
							{
								UpdateObject(map.Key, player.Key, player.Value, "Players");
							}
						}

						// если есть враги
						if (map.Value.enemy != null)
						{
							//Debug.Log("Обновляем enemy");
							foreach (var enemy in map.Value.enemy)
							{
								UpdateObject(map.Key, enemy.Key, enemy.Value, "Enemys");
							}
						}

						if (map.Value.animal != null)
						{
							foreach (var animal in map.Value.animal)
							{
								UpdateObject(map.Key, animal.Key, animal.Value, "Animals");
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
						// +100 (а не +1) чтобы Canvas LifeBar лежал над всеми детскими SpriteRenderer'ами анимации
						// (Spriter создаёт N child-sprite'ов с собственным sortingOrder 0..N-1 из UnityAnimator).
						prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)getMaps()[map_id].spawn_sort + 100 + model.sort;
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
				// SCML-анимация цепляется только если такой Prefab есть в серверном списке (AnimationCacheService._library).
				// Имя Prefab глобально уникально в пределах игры — совпадает с именем Unity-prefab в Resources/Prefabs/{type}/.
				if (recive.prefab != null && model.prefab != recive.prefab
					&& AnimationCacheService.HasPrefab(recive.prefab))
				{
					StartCoroutine(AnimationPatcher.Get(SERVER, GAME_ID, player_token, recive.prefab, (AnimationPatcher patcher) =>
					{
						if (patcher.error != null)
						{
							Error("Анимации: ошибка " + patcher.error);
							return;
						}
						if (patcher.spriterPacket == null)
						{
							Error("Анимации: пустой ответ от патчера для " + key);
							return;
						}
						try
						{
							Debug.Log("Анимации: создаём " + recive.prefab + " для " + key);
							NewSpriterRuntimeImporter.CreateSpriter(patcher.spriterPacket, key, GAME_ID);
						}
						catch (Exception ex)
						{
							Error("Анимации: ошибка " + ex);
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