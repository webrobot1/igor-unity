using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Класс для обработки ответов от сервера - карт
	/// </summary>
	public abstract class MapController : ConnectController 
	{
		[Header("Укажите родительский GameObject карт и существ")]

		/// <summary>
		/// объект в котором будут дочерние объекты карт
		/// </summary>
		[SerializeField]
		protected GameObject mapObject;

		/// <summary>
		/// родителький объект всех обектов
		/// </summary>
		[SerializeField]
		protected GameObject worldObject;

		/// <summary>
		/// массив с перечнем с какой стороны какая смежная карта
		/// </summary>
		private static Dictionary<int, MapSide> _sides = new Dictionary<int, MapSide>();

		/// <summary>
		/// массив декодированных с сервера карт
		/// </summary>
		private static Dictionary<int, MapDecode> _maps = new Dictionary<int, MapDecode>();

		/// <summary>
		/// Сдвиг ТАЙЛОВ (grid) относительно логических клеток и сущностей. Тайлы кладутся спрайтом с pivot(0,0)
		/// при tileAnchor(0.5,0.5) — визуальный центр тайла уходит от логической клетки; этот сдвиг возвращает
		/// совмещение. X=-1: центр спрайта тайла совпадает с центром сущности. Y=-0.5: сущность привязана
		/// НОГАМИ (feet), а не центром, поэтому по вертикали пол-клетки, а не целая.
		/// Применяется ТОЛЬКО к grid (тайлам), НЕ к zone (сущностям) — иначе сдвинулись бы вместе и совмещение
		/// не изменилось бы. Раньше держался вручную позицией mapObject в сцене (хрупко) — перенесён в код.
		/// </summary>
		private static readonly Vector2 TILE_OFFSET = new Vector2(-1f, -0.5f);

		/// <summary>
		/// Где лежит НАРИСОВАННОЕ полотно тайлов относительно ЛОГИЧЕСКИХ границ карты, которыми оперируют
		/// данные сервера и позиции сущностей: логическая карта занимает [0 .. width] по X и [1-height .. 1]
		/// по Y, а полотно на экране — [-0.5 .. width-0.5] по X и те же [1-height .. 1] по Y. Разница — это
		/// значение.
		/// По вертикали её нет: клетка занимает [y .. y+1], и сущность привязана ногами к её НИЖНЕМУ краю
		/// (см. <see cref="TILE_OFFSET"/>) — низ нижней клетки и верх верхней и есть логические границы.
		/// По горизонтали сущность стоит в СЕРЕДИНЕ своей клетки, а полотно нарезано по краям клеток —
		/// оттого полклетки.
		/// Нужна тому, кто считает границы по данным карты, а отвечает за увиденное игроком: границами
		/// обзора камеры карта упирается в край НАРИСОВАННОГО и центрируется по его середине.
		/// </summary>
		public static readonly Vector2 GRID_DRAW_OFFSET = new Vector2(-0.5f, 0f);

		/// <summary>
		/// Точка СЦЕНЫ (координаты сущностей внутри карты) в клетках НАРИСОВАННОЙ картинки карты — той, что
		/// показывают радар и обзорная карта (<see cref="WorldMapRenderer"/>). Ось X вправо, ось Y ВНИЗ,
		/// начало — левый верхний угол картинки.
		///
		/// Картинка нарезана по границам клеток: её левый край — левый край крайней клетки, верхний — верхний
		/// край верхнего ряда. Сцена же считает иначе: по горизонтали позиция сущности лежит в СЕРЕДИНЕ её
		/// клетки, по вертикали — у НИЖНЕГО края (сущность привязана ногами, см. <see cref="TILE_OFFSET"/>).
		/// Оттого перевод и есть пол-клетки по обеим осям: метка встаёт в середину той клетки, в которой
		/// сущность стоит, а не в её угол. Без него метка уезжает от картинки влево и вверх.
		///
		/// Тем же счётом карты разложены в открытом мире (клетки, ось Y вниз), потому к результату прямо
		/// прибавляется место карты в раскладке.
		/// </summary>
		public static Vector2 MapImageCell(Vector2 scenePoint)
		{
			return new Vector2(scenePoint.x + 0.5f, 0.5f - scenePoint.y);
		}

		protected override void Awake()
		{
			base.Awake();

			if (mapObject == null)
				Error("Карты: не присвоен GameObject для карт");

			if (worldObject == null)
				Error("Карты: не присвоен GameObject для игровых обектов");


			// на случай если мы как разработчик какие то тестовые данные оставили
			foreach (Transform transform in mapObject.transform)
			{
				DestroyImmediate(transform.gameObject);
			}

			foreach (Transform transform in worldObject.transform)
			{
				DestroyImmediate(transform.gameObject);
			}

			// Окно прозрачности в перекрывающих слоях карты: живёт на контейнере карт, центры окон берёт
			// по сущностям контейнера World. Ставится кодом — отдельного объекта в сцене не требует.
			TilemapXray.Attach(mapObject, worldObject);

			// определяем здесь что бы сбросить статичные свойства если мы перезаходили в игру
			// сбрасываем тк при разработке некие опции у нас стоят что не очищают при отладке эти данные https://youtu.be/sRx14YMbLuw
			_sides.Clear();
			_maps.Clear();
			_gates = null;
		}

		/// <summary>
		/// СНЯТИЕ панели загрузки: показывать уже есть что — мир пришёл, карта под игроком выложена (её
		/// графика приходит отдельным запросом и строится позже мира), тело персонажа показано.
		///
		/// Только снятие: поднимают панель точки начала ДОЛГОЙ паузы — нажатие кнопки входа и снятие игрока
		/// с названной новой картой. Переход между соседними картами открытого мира идёт иначе: сервер сам
		/// даёт адрес новой карты, переподключение занимает доли секунды, и панель на нём только мешала бы —
		/// игроку там достаточно короткой заминки.
		/// </summary>
		protected override void Update()
		{
			base.Update();

			// Ступени сообщаем и панель снимаем только у поднятой панели: этот же код крутится при переходе
			// между соседними картами открытого мира, а там панели быть не должно — ступень её подняла бы.
			if (!LoadingScreen.IsShown)
				return;

			// Игрока сервер снял с карты — начался переход, и до входа заново он остаётся на СТАРОЙ карте:
			// та цела, персонаж на ней виден, и опрос отрапортовал бы последними ступенями, а следом вход
			// заново отбросил бы полосу к первой. Ждём молча — ступени пойдут с авторизации.
			if (player != null && player.action == ACTION_REMOVE)
				return;

			if (IsWaitingWorld || player == null || !_maps.ContainsKey(player.map))
				LoadingScreen.SetStage(LoadingScreen.Stage.World);
			else if (!IsVisible(player))
				LoadingScreen.SetStage(LoadingScreen.Stage.Map);
			else
				LoadingScreen.Hide();
		}

		/// <summary>
		/// Показано ли тело сущности. Оно собирается асинхронно (скачивание анимации, подгонка размеров), и
		/// до готовности рендереры выключены: корневой гасит UpdateController.ApplyVisualPrefab, тело Spriter'а
		/// создаётся выключенным и включается концом подгонки. Пока не показано, карта стоит без персонажа —
		/// для игрока это та же пустая картинка, ради которой панель и заведена.
		///
		/// Спрашиваем рендерер ЛЮБОГО вида: тело рисуют разные — картинка и дерево Spriter'а спрайтами,
		/// скелет Spine мешем. Полоска здоровья и имя живут на холсте интерфейса и рендерером сцены не
		/// являются — за тело их тут не примешь.
		/// </summary>
		private static bool IsVisible(EntityModel entity)
		{
			foreach (var renderer in entity.GetComponentsInChildren<Renderer>())
				if (renderer.enabled)
					return true;

			return false;
		}

		/// <summary>
		/// Обработка пакета - с какой стороны какая ID карты на сцене
		/// </summary>
		protected virtual void HandleData<P, E>(Recive<P, E> recive) where P : EntityRecive where E : EntityRecive
		{
			if (recive.sides != null)
			{
				Debug.Log("Карты: Обрабатываем стороны карт");

				// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
				if (_maps.Count > 0)
				{
					foreach (Transform grid in mapObject.transform)
					{
						int map_id = Int32.Parse(grid.name);
						if (!recive.sides.ContainsKey(map_id))
						{
							Debug.Log("Карты: уничтожаем неиспользуемую карту " + map_id);
							DestroyImmediate(mapObject.transform.Find(map_id.ToString()).gameObject);
							DestroyImmediate(worldObject.transform.Find(map_id.ToString()).gameObject);

							_maps.Remove(map_id);
						}
					}
				}

				MapController._sides = recive.sides;
				// Доступность соседей пришла новая — проходы в недоступные карты считать заново (getGates).
				_gates = null;
				SortMap();

				// загрузим отвутвующую графику центральной и смежных карт 
				// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
				foreach (KeyValuePair<int, MapSide> side in recive.sides)
				{
					if (!_maps.ContainsKey(side.Key))
					{
						StartCoroutine(MapPatcher.Get(SERVER, GAME_ID, player_token, side.Key, (MapPatcher patcher) =>
						{
							if (patcher.error != null)
								Error("Карты: ошибка " + patcher.error);
							if (patcher.result == null || patcher.result.Length == 0)
								Error("Карты: пришел пустой ответ от патчера");
							else
							{
								Debug.Log("Карты: Обновляем " + side.Key);

								// приведем координаты в сответсвие с сеткой Unity
								try
								{
									if (!_sides.ContainsKey(side.Key))
										Debug.LogError("Карты: " + side.Key + " загружена в то время когда уже ее нет в массиве сторон (возможно игрок уже ушел с карты где она была нужна)");
									else if (mapObject.transform.Find(side.Key.ToString()) != null)
										Error("Карты: " + side.Key + " уже выгружена в игровое пространство");
									else if (_maps.ContainsKey(side.Key))
										Error("Карты: попытка загрузки " + side.Key + " повторно");
									else
									{
										Transform grid = new GameObject(side.Key.ToString()).transform;
										grid.gameObject.AddComponent<Grid>();
										grid.SetParent(mapObject.transform, false);

										_maps.Add(side.Key, MapDecodeModel.generate(patcher.result, grid, GAME_ID));

										// Пришла разметка ещё одной карты — с ней меняется и то, что клиент
										// знает о преградах на границах (getGates).
										_gates = null;

										// Слои, способные перекрыть сущность (выше слоя-земли), переводим на
										// xray-материал — окно прозрачности вокруг сущностей под кронами и крышами.
										TilemapXray.RegisterMap(grid, _maps[side.Key].spawn_sort);

										SortMap();
									}
								}
								catch (Exception ex)
								{
									TileCacheService.ResetCache(GAME_ID);
									Error("Карты: Ошибка разбора карты", ex);
								}
							}
						}));
					}
				}
			}
		}

		/// <summary>
		/// Известна ли клиенту преграда в клетке cell на карте mapId. Коллайдеры берутся ПЕР-КАРТА
		/// (getMaps()[mapId].colliders) — единый источник клиентских проверок проходимости: отсев заведомо
		/// холостых команд движения (CursorController) и экстраполяция сущностей (ObjectModel). Карта не
		/// загружена/выгружена (нет в _maps) → false: клетку не знаем, сервер отобьёт сам. Ответ false значит
		/// «преграды не знаю», а не «проходимо» — непроходимой сервер считает и клетку без тайла в слоях, и
		/// клетку недоступной соседней карты, а их клиент отсюда не различает.
		/// НЕ общий статик — он в открытом мире хранил бы коллайдеры случайного соседнего сегмента.
		/// </summary>
		public static bool IsColliderCell(int mapId, Vector2Int cell)
		{
			return _maps.TryGetValue(mapId, out MapDecode m) && m.colliders != null && m.colliders.Contains(cell);
		}

		/// <summary>
		/// Известно ли клиенту, что в клетке пройти НЕЛЬЗЯ. Ответ true значит «преграду знаю»: клетку накрывает
		/// преграда, в ней нет тайла (чернота за краем рисунка карты), она лежит на НЕДОСТУПНОЙ соседней карте
		/// (<see cref="MapSide.ready"/>) либо не попадает ни в одну известную карту вовсе — все четыре случая
		/// сервер держит непроходимыми. Разметки карты у клиента ещё нет → false: клетку не знаем, сервер
		/// отобьёт сам — то же правило, что у <see cref="IsColliderCell"/>, ошибаться допустимо лишь в сторону
		/// лишней отправки.
		///
		/// Клетка приходит в координатах СЦЕНЫ (там же стоят сущности), а преграды и тайлы карта держит в своих:
		/// перевод — вычитание смещения её стороны, как в <see cref="AddGates"/>.
		/// </summary>
		public static bool IsKnownImpassableCell(Vector2Int cell)
		{
			foreach (KeyValuePair<int, MapSide> side in _sides)
			{
				int sx = Mathf.RoundToInt(side.Value.x), sy = Mathf.RoundToInt(side.Value.y);
				int w  = Mathf.RoundToInt(side.Value.width), h = Mathf.RoundToInt(side.Value.height);

				// Ось Y раскладки смотрит ВВЕРХ, а ряды карты идут от её верхнего края вниз: карта с верхом sy
				// занимает ряды sy .. sy-h+1 (см. AddGates).
				if (cell.x < sx || cell.x >= sx + w || cell.y > sy || cell.y <= sy - h)
					continue;

				// Недоступную карту сервер не запускает, а её клетки считает непроходимыми целиком — ровно там
				// игрок и упирается в невидимую стену (см. getGates).
				if (!side.Value.ready)
					return true;

				if (!_maps.TryGetValue(side.Key, out MapDecode decoded))
					return false;

				Vector2Int local = new Vector2Int(cell.x - sx, cell.y - sy);

				if (decoded.colliders != null && decoded.colliders.Contains(local))
					return true;

				return decoded.tiles != null && !decoded.tiles.Contains(local);
			}

			// Клетка не попала ни в одну известную карту: за пределами своей карты и её соседей мира нет.
			return true;
		}

		public static Dictionary<int, MapDecode> getMaps()
        {
			return _maps;
		}		
		
		public static Dictionary<int, MapSide> getSides()
        {
			return _sides;
		}

		/// <summary>
		/// Проход в НЕДОСТУПНУЮ соседнюю карту: сплошной участок общей границы, не занятый преградой ни с
		/// одной стороны. from/to — середины КРАЙНИХ клеток участка, в координатах сцены (та же система, что
		/// у сущностей — клетка есть целая точка); участок шириной в клетку даёт from == to.
		///
		/// map — карта ДОСТУПНОЙ стороны, которой участок границы принадлежит: ею метка на земле находит своё
		/// место в иерархии сцены и порядок отрисовки — слой-земля у каждой карты свой.
		/// </summary>
		public struct Gate
		{
			public int map;
			public Vector2 from;
			public Vector2 to;

			/// <summary>Середина прохода — место ОДНОЙ метки там, где участок целиком не показать (карты клиента).</summary>
			public Vector2 center => (from + to) * 0.5f;
		}

		/// <summary>
		/// Проходы в НЕДОСТУПНЫЕ соседние карты — участками общей границы (<see cref="Gate"/>).
		///
		/// Ровно в этих местах игрок упирается в невидимую стену: недоступную карту сервер не запускает, а её
		/// клетки считает непроходимыми — переход не состоится, хотя ни стены, ни иного признака на земле нет
		/// (<see cref="MapSide.ready"/>). Оттого их и помечают карты клиента. Тем же они говорят, ГДЕ вообще
		/// проходы к соседу: занятые преградой участки границы никуда не ведут и в любом случае непроходимы.
		///
		/// Считается по ВСЕМ парам сторон, а не только по карте игрока: недоступный сосед граничит и с другой
		/// выложенной картой, и там та же стена. Разметка соседа неизвестна (карта не загружена) — считаем по
		/// своей стороне: <see cref="IsColliderCell"/> о незагруженной карте отвечает «преграды не знаю».
		/// </summary>
		public static List<Gate> getGates()
		{
			// Пересчитываем не каждый кадр, а по смене входов: стороны приходят пакетом сервера, разметка —
			// с загрузкой карты, обе точки сбрасывают кеш. Тем же самым сменившийся ЭКЗЕМПЛЯР списка говорит
			// показам, что выложенные метки устарели, — сравнивать их состав не требуется.
			return _gates ??= BuildGates();
		}

		/// <summary>Готовые проходы (см. <see cref="getGates"/>); null — входы сменились, считать заново.</summary>
		private static List<Gate> _gates;

		private static List<Gate> BuildGates()
		{
			List<Gate> gates = new List<Gate>();

			foreach (KeyValuePair<int, MapSide> closed in _sides)
			{
				if (closed.Value.ready)
					continue;

				foreach (KeyValuePair<int, MapSide> open in _sides)
					if (open.Key != closed.Key)
						AddGates(gates, open.Key, open.Value, closed.Key, closed.Value);
			}

			return gates;
		}

		/// <summary>
		/// Проходы на общей границе карты open и недоступной карты closed. Карты не примыкают — границы нет,
		/// добавлять нечего.
		///
		/// Стороны примыкают тайл в тайл, потому граница задаётся одной линией и парами соседних клеток на
		/// ней. Линия лежит МЕЖДУ рядами клеток, то есть на полуцелой координате: клетка есть целая точка
		/// (см. <see cref="IsColliderCell"/>), и метка встаёт ровно на стык, а не в клетку одной из карт.
		/// </summary>
		private static void AddGates(List<Gate> gates, int openId, MapSide open, int closedId, MapSide closed)
		{
			int ox = Mathf.RoundToInt(open.x),   oy = Mathf.RoundToInt(open.y);
			int ow = Mathf.RoundToInt(open.width), oh = Mathf.RoundToInt(open.height);
			int cx = Mathf.RoundToInt(closed.x),   cy = Mathf.RoundToInt(closed.y);
			int cw = Mathf.RoundToInt(closed.width), ch = Mathf.RoundToInt(closed.height);

			// Ось Y раскладки смотрит ВВЕРХ (см. MapSide), а ряды карты идут от её верхнего края вниз: карта
			// с верхом oy занимает ряды oy .. oy-oh+1.
			bool horizontal;   // граница горизонтальная — сосед снизу либо сверху
			float line;        // координата линии границы поперёк неё
			int openEdge;      // ряд (столбец) своей карты у границы, в её локальных координатах
			int closedEdge;    // то же у недоступной карты

			if (cy == oy - oh)          // сосед снизу
			{
				horizontal = true;  line = oy - oh + 0.5f;  openEdge = -(oh - 1);  closedEdge = 0;
			}
			else if (oy == cy - ch)     // сосед сверху
			{
				horizontal = true;  line = oy + 0.5f;       openEdge = 0;          closedEdge = -(ch - 1);
			}
			else if (cx == ox + ow)     // сосед справа
			{
				horizontal = false; line = ox + ow - 0.5f;  openEdge = ow - 1;     closedEdge = 0;
			}
			else if (ox == cx + cw)     // сосед слева
			{
				horizontal = false; line = ox - 0.5f;       openEdge = 0;          closedEdge = cw - 1;
			}
			else
				return;

			// Общая часть границы: у соседа своя длина и своё смещение, границей делится только пересечение.
			// Вдоль горизонтальной границы идём вправо, вдоль вертикальной — вниз (ряды убывают).
			int step = horizontal ? 1 : -1;
			int from = horizontal ? Mathf.Max(ox, cx) : Mathf.Min(oy, cy);
			int to   = horizontal
				? Mathf.Min(ox + ow, cx + cw) - 1
				: Mathf.Max(oy - oh, cy - ch) + 1;

			// Карты сошлись по одной оси, но по другой не перекрылись — касание углом: общей границы нет,
			// перейти там негде.
			int length = (horizontal ? to - from : from - to) + 1;
			if (length <= 0)
				return;

			// Проход — сплошной участок свободных клеток. Отдаём его целиком, от края до края: игрок упирается
			// в стену в ЛЮБОМ его месте, и метка на земле стоит по всей ширине; показу, которому места хватает
			// лишь на одну точку, участок отдаёт свою середину сам (Gate.center).
			int start = 0;
			bool inside = false;

			for (int i = 0; i < length; i++)
			{
				int at = from + i * step;

				Vector2Int openCell   = horizontal ? new Vector2Int(at - ox, openEdge)   : new Vector2Int(openEdge, at - oy);
				Vector2Int closedCell = horizontal ? new Vector2Int(at - cx, closedEdge) : new Vector2Int(closedEdge, at - cy);

				bool free = !IsColliderCell(openId, openCell) && !IsColliderCell(closedId, closedCell);

				if (free && !inside)
				{
					start = at;
					inside = true;
				}
				else if (!free && inside)
				{
					gates.Add(GateAt(openId, horizontal, line, start, at - step));
					inside = false;
				}
			}

			if (inside)
				gates.Add(GateAt(openId, horizontal, line, start, to));
		}

		/// <summary>Проход от клетки from до клетки to (включительно) на линии границы line.</summary>
		private static Gate GateAt(int openId, bool horizontal, float line, int from, int to)
		{
			return new Gate
			{
				map  = openId,
				from = horizontal ? new Vector2(from, line) : new Vector2(line, from),
				to   = horizontal ? new Vector2(to, line)   : new Vector2(line, to)
			};
		}
		
		private void SortMap()
		{
			foreach (Transform grid in mapObject.transform)
			{
				int map_id = Int32.Parse(grid.name);
				if (_sides.ContainsKey(map_id))
				{
					// mapPos — чистая позиция карты в открытом мире (для zone сущностей). grid (тайлы) дополнительно
					// сдвигается на TILE_OFFSET — совмещение спрайтов тайлов с логическими клетками (см. TILE_OFFSET).
					Vector2 mapPos = new Vector2(_sides[map_id].x, _sides[map_id].y);
					grid.localPosition = mapPos + TILE_OFFSET;

					// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
					if (worldObject.transform.Find(grid.gameObject.name) != null)
					{
						worldObject.transform.Find(grid.gameObject.name).localPosition = mapPos;
						foreach (Transform child in worldObject.transform.Find(grid.gameObject.name))
						{
							var model = child.GetComponent<EntityModel>();
							if (model != null)
							{
								int order = _maps[map_id].spawn_sort + model.sort;

								// SortingGroup гарантирован на корне каждой сущности (см. UpdateController.UpdateObject).
								var group = child.gameObject.GetComponent<UnityEngine.Rendering.SortingGroup>();
								if (group != null)
									group.sortingOrder = order;

								if (child.gameObject.GetComponentInChildren<Canvas>())
									// +100 (а не +1) чтобы Canvas LifeBar лежал над всеми детскими SpriteRenderer'ами анимации
									// (Spriter создаёт N child-sprite'ов с собственным sortingOrder 0..N-1 из UnityAnimator).
									child.gameObject.GetComponentInChildren<Canvas>().sortingOrder = _maps[map_id].spawn_sort + 100 + model.sort;
							}
						}
					}
				}
				else
					Error("Карты: На сцене присутвует карта "+ map_id + " которая не является текущей или смежной");
			}
		}
	}
}