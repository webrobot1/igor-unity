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
			HandleData(JsonConvert.DeserializeObject<Recive<EntityRecive, EntityRecive>>(json));
		}

		/// <summary>
		/// Обработка пришедших от сервера значений
		/// </summary>
		protected override void HandleData<P,E>(Recive<P, E> recive)
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
					if (map.Value.player == null && map.Value.entity == null)
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
							foreach (var player in map.Value.player)
							{
								UpdateObject(map.Key, player.Key, player.Value, "player");
							}
						}

						// Унифицированная группа entity — различаем по kind-полю внутри каждой сущности.
						// Папка префабов в Resources/Prefabs/ совпадает с именем kind один-в-один.
						if (map.Value.entity != null)
						{
							foreach (var ent in map.Value.entity)
							{
								UpdateObject(map.Key, ent.Key, ent.Value, ent.Value.kind);
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

				// Единый префаб на kind: Assets/Resources/Prefabs/{type}.prefab.
				// Визуал (Spriter scml) подтягивается с сервера по recive.prefab, если анимация есть в кеше.
				// Если нет — остаётся корневой fallback-SpriteRenderer с "unknow" спрайтом.
				UnityEngine.Object ob = Resources.Load("Prefabs/" + type, typeof(GameObject));

				if (ob == null)
				{
					Error("WebSocket: Отсутствует префаб Prefabs/" + type + " для объекта " + key);
					return null;
				}

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;

				// SortingGroup на корне сразу: сортируем все спрайты сущности как единое целое относительно
				// других сущностей (иначе Custom Axis Z-sort перемешивает body-parts одной сущности с частями другой).
				// Spriter при загрузке переиспользует этот же SortingGroup.
				if (prefab.GetComponent<UnityEngine.Rendering.SortingGroup>() == null)
					prefab.AddComponent<UnityEngine.Rendering.SortingGroup>();

				// Нормализация fallback-визуала до 1 клетки по высоте.
				// SR остаётся на корне (его Animator-клипы таргетят по пустому пути — если унести в child,
				// player'овские fallback-анимации перестанут работать). Поэтому скейлим корень целиком,
				// а LifeBar и CapsuleCollider2D контр-компенсируем, чтобы они остались своего префабного мира
				// (иначе HP-полоска раздувается у enemy и сжимается у player'а).
				// Высоту берём tight — т.е. по непрозрачным пикселям PNG'шки (Sprite.vertices при Tight-меше
				// в импортёре). sprite.bounds не подходит: включает прозрачные поля и нормализует таких
				// персонажей мельче остальных.
				var fallbackSr = prefab.GetComponent<SpriteRenderer>();
				if (fallbackSr != null && fallbackSr.sprite != null)
				{
					float native = AnimationCacheService.TryGetTightRect(fallbackSr.sprite, out Rect tight)
						? tight.height
						: fallbackSr.sprite.bounds.size.y;
					Vector3 oldScale = prefab.transform.localScale;
					if (native > 0.0001f && oldScale.y > 0.0001f)
					{
						// После root.scale *= factor мировая высота спрайта = oldScale.y * factor * native = 1.
						float factor = 1f / (native * oldScale.y);
						prefab.transform.localScale = new Vector3(oldScale.x * factor, oldScale.y * factor, oldScale.z);

						// Дети/компоненты, чьи размеры тюнились под oldScale, компенсируем.
						float inv = 1f / factor;
						var lifeBar = prefab.transform.Find("LifeBar");
						if (lifeBar != null)
						{
							lifeBar.localScale = new Vector3(inv, inv, 1f);
							var p = lifeBar.localPosition;
							lifeBar.localPosition = new Vector3(p.x * inv, p.y * inv, p.z);
						}
						var capsule = prefab.GetComponent<CapsuleCollider2D>();
						if (capsule != null)
						{
							capsule.size *= inv;
							capsule.offset *= inv;
						}
					}
				}

				model = prefab.GetComponent<EntityModel>();
				if (model == null)
				{
					Error("WebSocket: Отсутвует скрипт модели на объекте " + key);
					return null;
				}
				
				model.key = key;
				model.type = type.ToLower();

				model.Log("создан с префабом " + recive.prefab);


				// SCML-анимация цепляется только если такой Prefab есть в серверном списке (AnimationCacheService._library).
				// Имя Prefab глобально уникально в пределах игры — совпадает с именем Unity-prefab в Resources/Prefabs/{type}/.
				if (!string.IsNullOrEmpty(recive.prefab))
				{
					if(AnimationCacheService.HasPrefab(recive.prefab))
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
					else
						model.LogError("отсутвует анимация в кеше " + recive.prefab);
				}
				else
					model.LogWarning("не указан префаб");

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
					int order = (int)getMaps()[map_id].spawn_sort + model.sort;

					// SortingGroup гарантированно добавлен выше — все child-SpriteRenderer'ы (fallback или Spriter)
					// сортируются как единое целое относительно других сущностей.
					prefab.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = order;

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