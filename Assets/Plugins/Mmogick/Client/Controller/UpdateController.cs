using Newtonsoft.Json;
using SpriterDotNetUnity;
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

						// .Cast<Transform>().ToList() — снапшот детей: DestroyImmediate меняет коллекцию, прямой
						// foreach по transform пропустил бы половину
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

					// сервер прислал пустую локацию — запрашиваем отложенное удаление по каждой сущности.
					// тем у кого action уже remove не трогаем — у них своя корутина уже работает,
					// остальным даём 5 сек шанс отмены при появлении на смежной карте
					if (map.Value.player == null && map.Value.entity == null)
					{
						Debug.LogWarning("WebSocket: локация " + map.Key + " отправила пустое содержимое - удалим ее объекты с карты");

						foreach (Transform child in map_zone.transform.Cast<Transform>().ToList())
						{
							var model = child.GetComponent<EntityModel>();
							if (model.action != ACTION_REMOVE)
							{
								model.action = ACTION_REMOVE;
								model.StartCoroutine(model.Remove(true));
							}
						}
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

				// все новые  kind = prefab.object
				if (ob == null)
				{
					ob = Resources.Load("Prefabs/object", typeof(GameObject));
					//Error("WebSocket: Отсутствует префаб Prefabs/" + type + " для объекта " + key);
					//return null;
				}

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;

				// Non-uniform scale root-префаба в связке с rotated children даёт skew (Unity doc для Transform:
				// «child rotated relative to a non-uniformly scaled parent might appear skewed»). Spriter создаёт
				// ротированные body-parts, у них визуал поплывёт на поворотах. Мы принудительно выставляем uniform
				// scale (|x|=y), сохраняя знак X (mirror-flip). Если разработчик умышленно задал non-uniform —
				// пишем warning, чтобы это было видно и исправлено в префабе, а не маскировалось визуалом.
				Vector3 initScale = prefab.transform.localScale;
				if (Mathf.Abs(Mathf.Abs(initScale.x) - initScale.y) > 0.0001f)
				{
					float signX = initScale.x < 0 ? -1f : 1f;
					prefab.transform.localScale = new Vector3(signX * initScale.y, initScale.y, initScale.z);
					Debug.LogWarning("UpdateController: префаб '" + type + "' имеет non-uniform scale (" + initScale.x + ", " + initScale.y + ") — сброшен до uniform (" + (signX * initScale.y) + ", " + initScale.y + "). Задавайте uniform scale в префабе, иначе Spriter-дети поворотами дают skew (Transform doc).");
				}

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

				if (key == player_key)
				{
					player = model;
					if (!getSides().ContainsKey(map_id))
						Error("Запись о карте "+ map_id + " игрока не пришла вместе с доступными сторонами");

					#if UNITY_WEBGL && !UNITY_EDITOR
						WebGLSupport.WebGLDebug.DebugCheck(map_id);
					#endif
				}
			}
            else
			{
				model = prefab.GetComponent<EntityModel>();
			}

			// Визуал из серверной library: единая точка для первого спавна и для смены prefab на лету.
			// SetData ниже перезапишет model.prefab из recive — поэтому сверяем и применяем именно ДО SetData.
			// При первом спавне model.prefab пуст (default) → отличается от recive.prefab → ApplyVisualPrefab сработает.
			// При update без prefab в пакете или с тем же prefab — no-op (визуал не пересоздаём).
			if (!string.IsNullOrEmpty(recive.prefab) && recive.prefab != model.prefab)
			{
				if (!string.IsNullOrEmpty(model.prefab))
					model.Log("смена визуала с '" + model.prefab + "' на '" + recive.prefab + "'");
				ApplyVisualPrefab(prefab, model, recive.prefab, key);
			}
			else if (string.IsNullOrEmpty(recive.prefab) && string.IsNullOrEmpty(model.prefab))
				model.LogWarning("не указан префаб");

			model.Log("Обрабатываем на карте " + map_id + " пакетом " + JsonConvert.SerializeObject(recive, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
			// worldPositionStays=true: при смене map_zone (переход через границу карты) мировая позиция
			// сохраняется автоматически — localPosition пересчитывается под новый map_zone. Иначе мировая
			// прыгает (localPosition сохраняется, родитель сменился, мировая = новый_родитель + старый_local).
			prefab.transform.SetParent(worldObject.transform.Find(map_id.ToString()).transform, true);

			try
			{
				model.SetData(recive);
			}
			catch (Exception ex)
			{
				Error("WebSocket: Не удалось загрузить " + key, ex);
				return null;
			}

			// Сортировку выставляем ПОСЛЕ SetData: иначе model.sort ещё равен 0 (default),
			// и SortingGroup получает spawn_sort вместо spawn_sort + sort — у объектов с ненулевым
			// серверным sort это визуально выглядело как «на чужом слое».
			// Второе место — MapController.SortMap (при загрузке карты), на случай когда сущность
			// пришла раньше карты.
			if (getMaps().ContainsKey(map_id))
			{
				int order = getMaps()[map_id].spawn_sort + model.sort;

				// SortingGroup гарантированно добавлен выше — все child-SpriteRenderer'ы (fallback или Spriter)
				// сортируются как единое целое относительно других сущностей.
				prefab.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = order;

				if (prefab.GetComponentInChildren<Canvas>())
					// +100 (а не +1) чтобы Canvas LifeBar лежал над всеми детскими SpriteRenderer'ами анимации
					// (Spriter создаёт N child-sprite'ов с собственным sortingOrder 0..N-1 из UnityAnimator).
					prefab.GetComponentInChildren<Canvas>().sortingOrder = getMaps()[map_id].spawn_sort + 100 + model.sort;
			}

			return prefab;
		}

		/// <summary>
		/// Применяет к существующему GameObject визуал из серверной library:
		///   - image-prefab → ставит sprite в корневой SpriteRenderer + нормализует scale по серверному size;
		///   - animation-prefab → асинхронно (AnimationPatcher) собирает Spriter-сущность через
		///     NewSpriterRuntimeImporter.CreateSpriter (он сам чистит предыдущий Spriter).
		/// Для image-варианта явно сносит fallback-Animator (легаси из Unity-префаба) и Spriter-компоненты
		/// (на случай перехода animation→image), включает корневой SR и компенсирует мировой размер
		/// LifeBar/CapsuleCollider2D под применённый scale.
		/// </summary>
		private void ApplyVisualPrefab(GameObject go, EntityModel model, string newPrefab, string key)
		{
			string imageFile = AnimationCacheService.GetPrefabImage(newPrefab);
			if (imageFile != null)
			{
				// переход animation→image: сносим Spriter-компоненты (включая child Sprites/Metadata).
				// Для первого спавна работает как no-op (компонентов ещё нет).
				ClearSpriterVisualComponents(go);

				// Image-prefab статичен (только один спрайт через TryGetSprite ниже), но мы оставляем
				// Universal Animator для эффекта remove (Puff при попадании firebolt'а или выбрасывании
				// item'а). Параметр startDisabled=true критически важен: без него Animator перехватывает
				// SR.sprite и item рендерится пустым (apple, firebolt без иконки). PlayAction включит
				// Animator в момент проигрывания эффекта.
				var entityModel = go.GetComponent<Mmogick.EntityModel>();
				if (entityModel != null) entityModel.EnsureUniversalAnimator(startDisabled: true);

				var sr = go.GetComponent<SpriteRenderer>();
				if (sr == null) sr = go.AddComponent<SpriteRenderer>();
				// после перехода animation→image корневой SR был enabled=false — включаем обратно.
				sr.enabled = true;
				// TryGetSprite инвалидирует битый кеш и бросает exception — ловим, выходим
				// (визуал отменяется, на следующем sync файл перекачается).
				try { sr.sprite = AnimationCacheService.TryGetSprite(GAME_ID, imageFile); }
				catch (Exception ex) { Error(ex.Message); return; }

				float? size = AnimationCacheService.GetPrefabSize(newPrefab);
				if (size.HasValue && size.Value > 0.0001f)
				{
					// scale.y * factor = 1/size → итоговая мировая высота image-sprite = 1/size клеток.
					// Формула идемпотентна: повторное применение на уже отнормализованном scale даёт тот же 1/size.
					float factor = 1f / (size.Value * go.transform.localScale.y);
					Vector3 s = go.transform.localScale;
					go.transform.localScale = new Vector3(s.x * factor, s.y * factor, s.z);

					// П2: компенсируем LifeBar/CapsuleCollider2D, чтобы их МИРОВОЙ размер не плыл при scale корня.
					// Аналогично fallback-нормализации выше (LifeBar.localScale *= 1/factor + позиция, capsule.size/offset).
					float inv = 1f / factor;
					var lifeBar = go.transform.Find("LifeBar");
					if (lifeBar != null)
					{
						lifeBar.localScale = new Vector3(lifeBar.localScale.x * inv, lifeBar.localScale.y * inv, lifeBar.localScale.z);
						var p = lifeBar.localPosition;
						lifeBar.localPosition = new Vector3(p.x * inv, p.y * inv, p.z);
					}
					var capsule = go.GetComponent<CapsuleCollider2D>();
					if (capsule != null)
					{
						capsule.size *= inv;
						capsule.offset *= inv;
					}
				}
				model.Log("image-sprite " + newPrefab + " применён");
			}
			else if (AnimationCacheService.HasPrefab(newPrefab))
			{
				StartCoroutine(AnimationPatcher.Get(SERVER, GAME_ID, player_token, newPrefab, (AnimationPatcher patcher) =>
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
						Debug.Log("Анимации: создаём " + newPrefab + " для " + key);
						NewSpriterRuntimeImporter.CreateSpriter(patcher.spriterPacket, key, GAME_ID, newPrefab, AnimationCacheService.GetPrefabSize(newPrefab));
					}
					catch (Exception ex)
					{
						Error("Анимации: ошибка " + ex);
					}
				}));
			}
			else
				model.LogError("префаб '" + newPrefab + "' не определён в library (нет ни image-привязки, ни animation-привязки на сервере)");
		}

		/// <summary>
		/// Сносит все компоненты, которые ставит NewSpriterRuntimeImporter.CreateSpriter:
		/// SpriterDotNetBehaviour, SpriterPostImportAdjuster и child-объекты "Sprites"/"Metadata".
		/// Для первого спавна и для перехода image→image — no-op.
		/// </summary>
		private static void ClearSpriterVisualComponents(GameObject go)
		{
			var sb = go.GetComponent<SpriterDotNetBehaviour>();
			if (sb != null) GameObject.DestroyImmediate(sb);
			var adj = go.GetComponent<SpriterPostImportAdjuster>();
			if (adj != null) GameObject.DestroyImmediate(adj);
			var sprites = go.transform.Find("Sprites");
			if (sprites != null) GameObject.DestroyImmediate(sprites.gameObject);
			var metadata = go.transform.Find("Metadata");
			if (metadata != null) GameObject.DestroyImmediate(metadata.gameObject);
		}
	}
}