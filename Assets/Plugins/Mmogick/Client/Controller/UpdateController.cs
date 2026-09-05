using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
		/// <summary>
		/// true, пока обрабатывается пакет полной перезагрузки мира (верхний action == ACTION_LOAD):
		/// сущности из него — первичная отгрузка мира (вход/переход), НЕ живой спавн, эффект появления
		/// им не положен. Живые спавны приходят обычными дельтами (World::add песочницы) — worldLoading=false.
		/// </summary>
		private bool worldLoading;

		/// <summary>
		/// Кандидаты на удаление при полной перезагрузке мира (ACTION_LOAD). Сущности НЕ сносятся сразу:
		/// та, что есть и в новом мире (тот же key), переиспользуется UpdateObject (снимается из списка) —
		/// её визуал живёт бесшовно, без «исчезло-пересобралось» при каждом переходе между картами.
		/// Остаток (кого в новом мире нет) уничтожается после обработки пакета. Симметрично реюзу
		/// карт-тайлов в MapController.HandleData (там неактуальные зоны сносятся по sides).
		/// </summary>
		private List<GameObject> loadSweep;

		/// <summary>
		/// Целевой размер визуала-ЗАГЛУШКИ (спрайт "unknow" на корневом SR Resources-префаба) по длинной стороне,
		/// в клетках карты — отдельно для существа и для предмета на земле (у самой заглушки длинная сторона —
		/// высота). Заглушка — единственное, что видно у prefab'а без
		/// картинки и без скелета, поэтому её размер обязан отвечать РОДУ сущности: существо занимает клетку
		/// (та же цель, что у нормализации настоящих тел — VisualBuilder), предмет — заметно меньше,
		/// иначе он выходит крупнее любого предмета с картинкой и спорит с фигурой персонажа.
		/// Серверной доли клетки у такого предмета нет вовсе — ни своей, ни умолчания: и то и другое приходит
		/// от НОСИТЕЛЯ графики (набор картинок либо вариант скелета), которого у него нет, и брать её отсюда
		/// нечем. Число ниже — типовая доля клетки у предмета без графики.
		/// Род сущности решает серверная library (AnimationCacheService.IsGroundItem).
		/// </summary>
		private const float PlaceholderHeightCreature = 1f;
		private const float PlaceholderHeightItem = 0.3f;

		/// <summary>
		/// Сущности на сцене по их ключу. Поиск объекта по имени средствами движка обходит всю сцену, а он нужен
		/// на КАЖДУЮ сущность КАЖДОГО пакета — при сотне сущностей и шестидесяти пакетах в секунду это тысячи
		/// обходов сцены в секунду. Записи об уничтоженных объектах убираются лениво: уничтоженный объект движка
		/// сравнивается с null, и запись снимается при первом же обращении.
		/// </summary>
		private static readonly Dictionary<string, GameObject> entityObjects = new Dictionary<string, GameObject>();

		/// <summary>
		/// Объект сущности по ключу; null — её на сцене нет (либо она уже уничтожена).
		/// </summary>
		protected static GameObject FindEntity(string key)
		{
			if (entityObjects.TryGetValue(key, out GameObject known))
			{
				if (known != null)
					return known;

				entityObjects.Remove(key);
			}

			return null;
		}

		protected override void Handle(string json)
		{
			HandleData(JsonConvert.DeserializeObject<Recive<EntityRecive, EntityRecive>>(json));
		}

		/// <summary>
		/// Обработка пришедших от сервера значений
		/// </summary>
		protected override void HandleData<P,E>(Recive<P, E> recive)
		{
			worldLoading = recive.action == ACTION_LOAD;

			base.HandleData(recive);

			if (recive.action != null)
			{
				switch (recive.action)
				{
					case ACTION_LOAD:
						Debug.LogWarning("WebSocket: полная перезагрузка мира");

						// Не сносим сразу — собираем кандидатов: тех, кто есть и в новом мире, UpdateObject
						// переиспользует (см. loadSweep), остаток уничтожается после обработки пакета.
						loadSweep = new List<GameObject>();
						foreach (var side in worldObject.transform.Cast<Transform>().ToList())
						{
							foreach (var child in side.transform.Cast<Transform>().ToList())
							{
								if (player == null || child.gameObject.name != player.gameObject.name)
								{
									loadSweep.Add(child.gameObject);
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

			// Сервер снял НАШЕГО игрока со сцены, назвав новую карту: он переезжает туда, где мы ещё не
			// авторизованы. Переподключение произойдёт позже, по закрытию соединения — команду на вход заново
			// даёт оно, и ждёт записи новой карты в базу, откуда её читает авторизация. А экран закрываем уже
			// здесь: пауза до первой отрисовки новой карты — самая длинная в игре, и это её начало.
			if (player != null && recive.world != null
				&& recive.world.ContainsKey(player.map) && recive.world[player.map].player != null
				&& recive.world[player.map].player.ContainsKey(player_key)
				&& recive.world[player.map].player[player_key].action == ACTION_REMOVE
				&& recive.world[player.map].player[player_key].map != null)
				LoadingScreen.Show();

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

						// zone (сущности) = чистая позиция карты (sides), БЕЗ TILE_OFFSET: сдвиг несут только тайлы (grid).
						if (getSides().ContainsKey(map.Key))
							map_zone.localPosition = new Vector2(getSides()[map.Key].x, getSides()[map.Key].y);

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
						// kind игрока резолвится из recive.prefab так же, как у entity (манифест /prefabs
						// содержит player → kind 'player' → Resources/Prefabs/player).
						if (map.Value.player != null)
						{
							foreach (var player in map.Value.player)
							{
								UpdateObject(map.Key, player.Key, player.Value);
							}
						}

						// Единая группа entity: вид (kind) в пакете не едет. Резолв kind из prefab'а
						// (через library /prefabs) нужен ТОЛЬКО на спавне (Resources.Load в UpdateObject), а recive.prefab
						// присутствует лишь в полном пакете спавна — на дельтах сущность уже на сцене и prefab==null
						// (в спавн-ветку UpdateObject там не заходим, GameObject.Find нашёл бы сущность).
						if (map.Value.entity != null)
						{
							foreach (var ent in map.Value.entity)
							{
								UpdateObject(map.Key, ent.Key, ent.Value);
							}
						}
					}
				}
			}

			// Остаток кандидатов перезагрузки мира — сущности, которых в новом мире нет: сносим.
			// Немедленно (DestroyImmediate, как прежний снос в ACTION_LOAD-ветке), а не Remove-корутиной:
			// на новых картах их не существует, «шанс отмены» им не положен.
			if (loadSweep != null)
			{
				foreach (var stale in loadSweep)
					if (stale != null)
					{
						Debug.LogWarning("WebSocket: " + stale.name + " отсутствует в новом мире - удаляем");
						DestroyImmediate(stale);
					}
				loadSweep = null;
			}
		}

		/// <summary>
		/// Имя Resources-префаба для сущности, у чьего вида своего файла нет (Prefabs/{kind} отсутствует).
		/// Умолчание — модель объекта: без полоски здоровья и без выбора целью. Игра переопределяет по
		/// ДАННЫМ пакета спавна: вид в пакете не едет, состав компонентов едет.
		/// </summary>
		protected virtual string FallbackKind(EntityRecive recive)
		{
			return "object";
		}

		/// <summary>
		/// обработка кокнретной сущности (создание и обновлелние)
		/// </summary>
		protected virtual GameObject UpdateObject(int map_id, string key, EntityRecive recive)
		{
			GameObject prefab = FindEntity(key);
			EntityModel model;

			// Сущность из нового мира жива на сцене — переиспользуем, из кандидатов на снос убираем.
			if (prefab != null)
				loadSweep?.Remove(prefab);

			// если игрока нет на сцене
			if (prefab == null)
			{
				// те что удалились как только мы пришли тех не создаем тк нет пакета с чего их создавать
				if (recive.action == "remove")
					return null;

				// Имя Resources-префаба = kind. Резолвим строго из recive.prefab — он в полном пакете
				// спавна гарантированно присутствует и у игрока, и у entity (на дельтах эта ветка не выполняется —
				// GameObject.Find выше нашёл бы). Манифест /prefabs содержит ВСЕ prefab'ы, включая player.
				// GetPrefabKind строгий: бросит, если prefab'а нет в library — это нарушение целостности данных.
				string kind = AnimationCacheService.GetPrefabKind(recive.prefab);

				// Единый префаб на kind: Assets/Resources/Prefabs/{kind}.prefab; у вида без своего файла
				// заготовку называет FallbackKind.
				// Визуал (скелет Spine) подтягивается с сервера по recive.prefab, если анимация есть в кеше.
				// Если нет — остаётся корневой fallback-SpriteRenderer с "unknow" спрайтом.
				UnityEngine.Object ob = Resources.Load("Prefabs/" + kind, typeof(GameObject));
				if (ob == null)
					ob = Resources.Load("Prefabs/" + FallbackKind(recive), typeof(GameObject));

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;
				entityObjects[key] = prefab;

				// Non-uniform scale root-префаба в связке с rotated children даёт skew (Unity doc для Transform:
				// «child rotated relative to a non-uniformly scaled parent might appear skewed»). Скелет живёт
				// ротированным дочерним объектом, и визуал поплывёт на поворотах. Мы принудительно выставляем uniform
				// scale (|x|=y), сохраняя знак X (mirror-flip). Если разработчик умышленно задал non-uniform —
				// пишем warning, чтобы это было видно и исправлено в префабе, а не маскировалось визуалом.
				Vector3 initScale = prefab.transform.localScale;
				if (Mathf.Abs(Mathf.Abs(initScale.x) - initScale.y) > 0.0001f)
				{
					float signX = initScale.x < 0 ? -1f : 1f;
					prefab.transform.localScale = new Vector3(signX * initScale.y, initScale.y, initScale.z);
					Debug.LogWarning("UpdateController: префаб '" + kind + "' имеет non-uniform scale (" + initScale.x + ", " + initScale.y + ") — сброшен до uniform (" + (signX * initScale.y) + ", " + initScale.y + "). Задавайте uniform scale в префабе, иначе дочерний скелет поворотами даёт skew (Transform doc).");
				}

				// SortingGroup на корне сразу: сортируем всё нарисованное на сущности как единое целое относительно
				// других сущностей (иначе Custom Axis Z-sort перемешивает её части с частями другой).
				if (prefab.GetComponent<UnityEngine.Rendering.SortingGroup>() == null)
					prefab.AddComponent<UnityEngine.Rendering.SortingGroup>();

				// Нормализация fallback-визуала (заглушки) до целевой высоты своего рода — см. doc
				// PlaceholderHeightCreature/PlaceholderHeightItem.
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
					// Нормализуем по max(width, height) — иначе вытянутые горизонтально спрайты
					// (молния 3:1) после нормализации по Y становятся 3 клетки в ширину.
					// Симметрично нормализации настоящего тела (VisualBuilder.Fit — по большей стороне).
					float native = AnimationCacheService.TryGetTightRect(fallbackSr.sprite, out Rect tight)
						? Mathf.Max(tight.width, tight.height)
						: Mathf.Max(fallbackSr.sprite.bounds.size.x, fallbackSr.sprite.bounds.size.y);
					// Критерий «это предмет» — тот же, каким подбираемым предметам вешается маркер-подсветка
					// (MainController.UpdateObject): род сущности задаётся её prefab'ом в серверной library,
					// второго признака под размер заводить незачем. recive.prefab здесь непуст —
					// GetPrefabKind выше уже отрезолвил по нему Resources-префаб.
					float target = AnimationCacheService.IsGroundItem(recive.prefab)
						? PlaceholderHeightItem
						: PlaceholderHeightCreature;
					Vector3 oldScale = prefab.transform.localScale;
					if (native > 0.0001f && oldScale.y > 0.0001f)
					{
						// После root.scale *= factor мировой max(W,H) спрайта = oldScale.y * factor * native = target.
						float factor = target / (native * oldScale.y);
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
				model.type = kind.ToLower();

				// Живой спавн существа (появилось, пока игрок в игре) — пометить для эффекта появления.
				// Сыграет EntityModel.OnVisualReady, когда визуал готов и показан. НЕ живой спавн:
				//  - worldLoading: сущность из пакета полной отгрузки мира (вход/переход);
				//  - key == player_key: свой игрок при входе добавляется в мир песочницей ПОСЛЕ отдачи
				//    мира и приходит дельтой (WebSocket.sendWorlds → Channel::player_add) — это вход, не спавн;
				//  - object: предметы/декор появляются без эффекта (выброс item'а, снаряды).
				if (!worldLoading && key != player_key && model.type != "object")
					model.pendingAppearFlash = true;

				model.Log("создан с префабом " + recive.prefab);

				if (key == player_key)
				{
					player = model;
					if (!getSides().ContainsKey(map_id))
						Error("Запись о карте "+ map_id + " игрока не пришла вместе с доступными сторонами");

					#if UNITY_WEBGL && !UNITY_EDITOR
						WebGLSupport.WebGLDebug.DebugCheck(map_id, Put2Send);
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

			// Пакет в текстовом виде собирается ТОЛЬКО под флагом подробного журнала: сериализация выполняется
			// на каждую сущность каждого кадра и стоит дороже всей остальной обработки пакета вместе взятой.
			if (EntityModel.verbose)
				model.Log("Обрабатываем на карте " + map_id + " пакетом " + JsonConvert.SerializeObject(recive, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

			// worldPositionStays=true: при смене map_zone (переход через границу карты) мировая позиция
			// сохраняется автоматически — localPosition пересчитывается под новый map_zone. Иначе мировая
			// прыгает (localPosition сохраняется, родитель сменился, мировая = новый_родитель + старый_local).
			// Смена родителя перестраивает иерархию сцены, потому делается только при РЕАЛЬНОЙ смене зоны:
			// у стоящей на месте сущности зона та же самая в каждом пакете.
			Transform target_zone = worldObject.transform.Find(map_id.ToString());
			if (prefab.transform.parent != target_zone)
				prefab.transform.SetParent(target_zone, true);

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
				int spawn_sort = getMaps()[map_id].spawn_sort;

				// Компоненты берутся из кеша модели: их поиск обходит объект (а поиск Canvas — ещё и всех
				// потомков), и это на каждую сущность каждого пакета. Состав компонентов сущности после
				// сборки визуала не меняется, потому ссылки достаточно найти однажды.
				// SortingGroup гарантированно добавлен выше — всё нарисованное на сущности (заглушка, картинка,
				// скелет) сортируется как единое целое относительно других сущностей.
				model.EnsureRenderRefs();

				if (model.sortingGroup != null)
					model.sortingGroup.sortingOrder = spawn_sort + model.sort;

				if (model.barCanvas != null)
					// +100 (а не +1) чтобы Canvas LifeBar лежал над всем, что рисует сама сущность (скелет и
					// надетые предметы держат свой порядок внутри группы).
					model.barCanvas.sortingOrder = spawn_sort + 100 + model.sort;
			}

			return prefab;
		}

		/// <summary>
		/// Применяет к существующему GameObject визуал из серверной library. Собирает его не этот метод, а
		/// общая точка сборки (VisualBuilder) — она же выбирает форму по источнику; здесь остаётся то, чего
		/// сборка не знает: чем является prefab в library, откуда взять данные формы (картинка лежит в
		/// локальном кеше, скелет качается асинхронно) и что делать с моделью по готовности визуала.
		///   - image-prefab → кадр из кеша графики, дальше общая точка;
		///   - animation-prefab → пакет скелета асинхронно (SpineCacheService), дальше общая точка;
		///   - kind-only prefab → своего визуала нет вовсе, остаётся заглушка Resources-префаба.
		/// </summary>
		private void ApplyVisualPrefab(GameObject go, EntityModel model, string newPrefab, string key)
		{
			string imageFile = AnimationCacheService.GetPrefabImage(newPrefab);
			if (imageFile != null)
			{
				// Image-prefab статичен (один кадр), но мы оставляем
				// Universal Animator для эффекта remove (Puff при попадании firebolt'а или выбрасывании
				// item'а). Параметр startDisabled=true критически важен: без него Animator перехватывает
				// SR.sprite и item рендерится пустым (apple, firebolt без иконки). PlayAction включит
				// Animator в момент проигрывания эффекта.
				var entityModel = go.GetComponent<Mmogick.EntityModel>();
				if (entityModel != null) entityModel.EnsureUniversalAnimator(startDisabled: true);

				// TryGetSprite инвалидирует битый кеш и бросает exception — ловим, выходим
				// (визуал отменяется, на следующем sync файл перекачается).
				Sprite sprite;
				try { sprite = AnimationCacheService.TryGetSprite(GAME_ID, imageFile); }
				catch (Exception ex) { Error(ex.Message); return; }

				// Размер целиком ведёт СЕРВЕР: своё значение записи, а нет его — умолчание её рода из конверта
				// каталога; своего числа клиент не держит, и смена серверного умолчания правки его не требует.
				// Разрешение обоих звеньев — за GetPrefabSize, нормализацию по нему держит общая точка сборки.
				VisualBuilder.Create(go, VisualBuilder.Source.Image(sprite, AnimationCacheService.GetPrefabSize(newPrefab)));

				model.Log("image-sprite " + newPrefab + " применён");
				// Image-визуал применяется синхронно — точка «визуал готов» сразу здесь
				// (у animation-пути её проходит сборка скелета по концу подгонки).
				model.OnVisualReady();
			}
			else if (AnimationCacheService.HasAnimation(newPrefab))
			{
				// Пока скелет асинхронно качается и собирается, сущность не показываем вовсе: placeholder "unknow"
				// (при первом спавне) или устаревший визуал мелькали бы до готового тела. Прячем корневой SR
				// (запомнив состояние — при смене prefab он уже выключен прошлой сборкой) и LifeBar
				// первого спавна (model.prefab ещё пуст — SetData присвоит его после). Показ по готовности —
				// сама сборка скелета (тело) и EntityModel.OnVisualReady (LifeBar); при ошибке загрузки
				// возвращаем как было, иначе сущность осталась бы невидимкой.
				var hideSr = go.GetComponent<SpriteRenderer>();
				bool srWasEnabled = hideSr != null && hideSr.enabled;
				if (hideSr != null) hideSr.enabled = false;
				var hideLifeBar = string.IsNullOrEmpty(model.prefab) ? go.transform.Find("LifeBar") : null;
				if (hideLifeBar != null && hideLifeBar.gameObject.activeSelf)
				{
					hideLifeBar.gameObject.SetActive(false);
					model.lifeBarHiddenForBuild = true;
				}
				Action restoreVisual = () =>
				{
					if (srWasEnabled && hideSr != null) hideSr.enabled = true;
					if (model.lifeBarHiddenForBuild && hideLifeBar != null)
					{
						hideLifeBar.gameObject.SetActive(true);
						model.lifeBarHiddenForBuild = false;
					}
				};

				StartCoroutine(SpineCacheService.GetSkeleton(SERVER, GAME_ID, newPrefab, player_token, (asset, error) =>
				{
					// Скелет качается асинхронно, и за это время сервер мог убрать сущность со сцены (тело по
					// истечении срока лежания, снаряд после попадания). Это ОЖИДАЕМАЯ гонка, а не сбой:
					// рисовать уже нечего, и поднимать её ошибкой нельзя — Error рвёт игроку вход.
					if (go == null)
					{
						Debug.Log("Анимации: " + key + " ушёл со сцены, пока грузилась анимация — рисовать нечего");
						return;
					}
					if (error != null || asset == null)
					{
						restoreVisual();
						Error("Анимации: " + (error ?? "скелет не собрался для " + key));
						return;
					}

					var (clipName, flipX, clipAngle) = AnimationCacheService.GetClipName(
						newPrefab, model.action, model.Forward.x, model.Forward.y);
					VisualBuilder.Create(go, VisualBuilder.Source.Skeleton(
						asset, AnimationCacheService.GetPrefabSize(newPrefab), clipName));
					model.DisplayAngle = clipAngle;
					model.OnVisualReady();
				}));
			}
			else if (AnimationCacheService.HasPrefab(newPrefab))
			{
				// prefab есть в library, но без картинки и без скелета — он существует только чтобы
				// донести kind (GetPrefabKind → Resources/Prefabs/{kind}). Визуала-оверлея нет: остаёмся
				// на fallback-SpriteRenderer Resources-префаба ("unknow"). Легитимно (см. PrefabEntry).
				//
				// Universal Animator навешиваем, чтобы зашитые в клиент дефолтные эффекты dead (силуэт) и
				// remove (Puff) работали и для kind-only сущности (без них PlayAction вернул бы false и
				// сущность никогда не проигрывала бы даже дефолтную смерть). Universal dead/remove.anim бьют
				// по спрайту КОРНЕВОГО SR — поэтому placeholder-спрайт запоминаем в EntityModel ДО навешивания
				// Animator'а: PlayAction вернёт его на SR, когда сущность оживёт после dead (иначе она застряла
				// бы на dead-кадре, т.к. writeDefaults=false держит последний кадр анимации). startDisabled=true:
				// в idle Animator не должен перехватывать placeholder; PlayAction включит его в момент эффекта.
				var entityModel = go.GetComponent<Mmogick.EntityModel>();
				var sr = go.GetComponent<SpriteRenderer>();
				if (entityModel != null && sr != null && sr.sprite != null)
					entityModel.SetFallbackSprite(sr.sprite);
				if (entityModel != null) entityModel.EnsureUniversalAnimator(startDisabled: true);
				model.LogWarning("prefab '" + newPrefab + "' без image/animation — fallback-визуал kind + Universal overlay (dead/remove)");
				// kind-only визуал (placeholder) окончателен и виден сразу — точка «визуал готов» здесь.
				model.OnVisualReady();
			}
			else
				model.LogError("префаб '" + newPrefab + "' не определён в library (нет ни image-привязки, ни animation-привязки на сервере)");
		}

	}
}