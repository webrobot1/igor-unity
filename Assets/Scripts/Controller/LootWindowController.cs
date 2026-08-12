using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	// Окно добычи контейнера — трупа существа либо объекта-сундука: сетка слотов по паттерну инвентаря.
	// Контракт сервера:
	//   - состав добычи ПРИВАТЕН: приходит только тому, кто стоит на клетке контейнера, — по команде
	//     открытия (ui/loot/open) и дальше при каждом изменении состава. Данные оседают на самой
	//     сущности (CorpseLootMarker), поэтому окно открывается по ПРИХОДУ состава, а не решением клиента;
	//   - клик по контейнеру издалека: команду шлём сразу, сервер сам ведёт игрока к его клетке и
	//     повторяет открытие до прибытия — своего подхода клиент не изобретает;
	//   - пустой состав тоже приходит и показывается пустым окном («обыскал — пусто»); окно держится,
	//     пока игрок стоит на клетке, и закрывается сходом с неё либо исчезновением самой сущности;
	//   - право на добычу (данные команды status/lootfree) сервер проверяет на попытке взять: пока
	//     владелец назначен, берёт только он, отказ остальным тихий; по истечении срока эксклюзива
	//     сервер команду не перевешивает — право гаснет вместе с ней. Клиент это ЗЕРКАЛИТ неактивными
	//     кнопками — UX-фильтр, а не замена серверной проверки;
	//   - каждая операция (take/put) отвечает свежим составом — окно перерисовывается по нему, локально
	//     ничего не двигаем (сервер — source of truth).
	// Взятие предмета контейнера в курсор делает базовый SlotScript-клик; принадлежность слота
	// контейнеру Item.Use определяет меткой LootSlotMarker (на слоте-цели и на родителе Item).
	abstract public class LootWindowController : ActionBarsController
	{
		[Header("Для работы с окном добычи (труп)")]

		// панель окна добычи (включается при открытии). Слоты создаются в lootSlotArea из тех же
		// slotPrefab/itemPrefab, что инвентарь (биндинги ниже).
		[SerializeField]
		private CanvasGroup lootGroup;

		[SerializeField]
		private Transform lootSlotArea;

		[SerializeField]
		private GameObject lootSlotPrefab;

		[SerializeField]
		private Item lootItemPrefab;

		// «Забрать всё» — одна команда take со списком ВСЕХ занятых позиций: инвентарные команды
		// игрока сервер исполняет по одной за тик, пачка отдельных take затёрла бы сама себя.
		[SerializeField]
		private Button lootTakeAllButton;

		// key открытого трупа; null — окно закрыто. static — Item.Use шлёт команды take/put без поиска
		// инстанса контроллера (паттерн InventoryController._slots).
		private static string _containerKey;

		// key трупа, к которому игрок идёт по клику: окно откроется по прибытии на его клетку.
		private static string _pendingKey;

		private static SlotScript[] _lootSlots;
		private static CanvasGroup _lootGroup;
		private static Button _takeAllButton;

		// Маркер трупа, с которым работает окно (открытого либо того, к которому идём). Кеш нужен
		// потому, что состояние окна пересчитывается КАЖДЫЙ кадр: истечение чужого эксклюзива — не
		// событие, пакета в этот момент нет (сервер шлёт только изменения), а кнопки обязаны
		// разблокироваться сами. GameObject.Find на каждом кадре для этого слишком дорог.
		private static CorpseLootMarker _marker;
		private static string _markerKey;

		// Версия добычи, по которой отрисованы слоты сейчас (-1 — окно закрыто/не отрисовано).
		private static int _renderedVersion = -1;

		// Инстанс контроллера для статических точек входа (Item.Use, кнопка, RefreshWindow): единственный
		// на сцене, заполняется в Awake — Find не нужен.
		private static LootWindowController _instance;

		protected override void Awake()
		{
			base.Awake();

			// статики чистить вручную: Enter Play Mode без Domain Reload их не сбрасывает
			_containerKey = null;
			_pendingKey = null;
			_lootSlots = null;
			_renderedVersion = -1;
			_marker = null;
			_markerKey = null;
			_lootGroup = lootGroup;
			_takeAllButton = lootTakeAllButton;
			_instance = this;

			if (lootGroup == null)
			{
				Error("не указана CanvasGroup окна добычи");
				return;
			}

			if (lootSlotArea == null)
			{
				Error("не указан Transform контейнер слотов окна добычи");
				return;
			}

			if (lootSlotPrefab == null || lootItemPrefab == null)
			{
				Error("не указаны префабы слота/предмета окна добычи");
				return;
			}

			if (lootTakeAllButton == null)
			{
				Error("не назначена кнопка «Забрать всё» окна добычи");
				return;
			}

			lootTakeAllButton.onClick.RemoveListener(SendTakeAll);
			lootTakeAllButton.onClick.AddListener(SendTakeAll);

			Hide();
		}

		// Состояние окна пересчитывается каждый кадр, а не только на пакете: тело уходит со сцены не в
		// кадре своего пакета — уничтожение ждёт окончания анимации ухода, и до него окно висело бы над
		// пустым местом. Отсчёт на трупе тикает тем же способом (CorpseLootMarker.LateUpdate).
		protected override void Update()
		{
			base.Update();

			if (_containerKey != null || _pendingKey != null)
				RefreshWindow();
		}

		// Добыча приходит компонентом самой сущности — складываем её на сущность (CorpseLootMarker),
		// оттуда её читают и окно, и отображение трупа на карте. Свой игрок не наш случай: у игроков добычи нет,
		// его inventory наполняет InventoryController.
		protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
		{
			EnemyComponentsRecive components = null;

			if (key != player_key)
			{
				// components полиморфны (shadowed new): у player-группы Newtonsoft заполняет
				// PlayerRecive.components, у entity-группы — EnemyRecive.components; loot лежит
				// в общем базовом EnemyComponentsRecive, поэтому читаем через базовый тип.
				components = recive is PlayerRecive playerRecive
					? playerRecive.components
					: ((EnemyRecive)recive).components;
			}

			GameObject prefab = base.UpdateObject(map_id, key, recive);

			// Право на добычу приходит данными своей команды, а не компонентом: оно публично и нужно
			// издалека — по нему решают, идти к телу или оно чужое. Потому маркер заводится и на одной
			// команде, без приватного состава добычи.
			// Гейт — именно ДАННЫЕ команды, а не её наличие: снятие команды тоже приходит узлом этой
			// группы (пустое действие, данных нет), и по одному наличию маркер заводился бы на теле,
			// добыча которого уже свободна.
			EventRecive lootfree;
			bool right = recive.events != null
				&& recive.events.TryGetValue(CorpseLootMarker.GROUP_LOOTFREE, out lootfree)
				&& lootfree.data != null;

			if (prefab != null && (right || (components != null && components.loot != null)))
				CorpseLootMarker.Apply(prefab, components);

			// Немедленный пересчёт на пришедшей дельте: игрок сдвинулся (сошёл с клетки / дошёл до цели)
			// либо изменилась сама добыча — окно догоняет в том же кадре, не ожидая ближайшего Update.
			// После base серверная позиция игрока из этого пакета уже применена.
			if (key == player_key || key == _containerKey || key == _pendingKey)
				RefreshWindow();

			return prefab;
		}

		// Имена серверных компонентов добычи: у объекта-сундука состав задан самим префабом (loot),
		// у существа он разыгрывается при смерти по таблице дропа (loot_table) — до смерти компонента
		// добычи на нём нет вовсе. Оба приходят в составе префаба (манифест /prefabs).
		private const string COMPONENT_LOOT = "loot";
		private const string COMPONENT_LOOT_TABLE = "loot_table";

		/// <summary>
		/// Открываемый ли это контейнер — труп существа с дропом либо объект-сундук. UX-фильтр перед
		/// отправкой открытия: зеркалит серверные гейты ui/loot/open (живую цель и сущность без добычи
		/// сервер отбивает молча) — без него клик по любому объекту без запаса здоровья (портал, алтарь)
		/// слал бы заведомо отбиваемую команду. Тем же признаком решается и подсветка кликабельного:
		/// кольцо обещает открытие ровно там, где оно состоится.
		/// </summary>
		public static bool IsContainer(EntityModel entity)
		{
			if (entity == null || string.IsNullOrEmpty(entity.prefab)) return false;

			// живая цель добычи не отдаёт (труп существа лутается только мёртвым); у объекта-сундука
			// запаса здоровья нет вовсе — hp остаётся null и гейт его не трогает
			if (entity is EnemyModel enemy && enemy.hp != null && enemy.hp > 0) return false;

			List<string> components = AnimationCacheService.GetPrefabComponents(entity.prefab);
			return components.Contains(COMPONENT_LOOT) || components.Contains(COMPONENT_LOOT_TABLE);
		}

		/// <summary>
		/// Клик по контейнеру: запросить его добычу. Команда уходит серверу всегда — состав приватен, и
		/// без запроса его не будет даже на клетке. Возвращает true, если игрок уже на клетке контейнера
		/// (вызывающему не нужно вести персонажа): подход при клике издалека ведёт сам сервер.
		/// </summary>
		public static bool RequestOpen(EntityModel container)
		{
			if (container == null) return false;

			_pendingKey = container.key;

			LootOpenResponse response = new LootOpenResponse();
			response.key = container.key;
			response.Send();

			RefreshWindow();

			PlayerModel me = PlayerController.Player;
			return me != null && EntityModel.SameTile(me.position, container.position);
		}

		/// <summary>Игрок выбрал другую цель/пошёл в другое место — отменить отложенное открытие.</summary>
		public static void CancelPending()
		{
			_pendingKey = null;
		}

		/// <summary>
		/// Показать/перерисовать/закрыть окно по ТЕКУЩЕМУ состоянию мира. Единственная точка решения
		/// «показывать ли»: и открытие по прибытии, и закрытие (сход с клетки, воскрешение цели,
		/// исчезновение сущности, опустевшая добыча) — один и тот же набор условий.
		/// </summary>
		private static void RefreshWindow()
		{
			string key = _containerKey ?? _pendingKey;
			if (key == null) return;

			PlayerModel me = PlayerController.Player;
			CorpseLootMarker marker = FindMarker(key);

			// Сам контейнер ищем в мире, а маркер добычи — отдельно: у объекта-сундука публичного
			// признака добычи нет вовсе, и до прихода приватного состава маркера на нём ЕЩЁ НЕТ.
			// Считать «нет маркера» за «контейнер исчез» нельзя — отложенное открытие гасло бы на
			// первом же кадре ожидания, и пришедший следом состав окно уже не открывал.
			// Поиск по сцене — только на этой ветке: у открытого окна маркер есть и берётся из кеша.
			EntityModel container = marker != null ? marker.GetComponent<EntityModel>() : FindEntity(key);

			// Сущность исчезла (тело распалось) — отложенное открытие больше не состоится.
			if (container == null)
			{
				_pendingKey = null;
				Hide();
				return;
			}

			// Состав ни разу не приходил (запрос ещё в пути либо сервер его отбил), игрок сошёл с клетки
			// либо сам мёртв (мёртвый не лутает — тот же гейт держит сервер, а умереть на клетке трупа
			// обычное дело) — окна нет. Пустой состав окно НЕ закрывает: он и есть ответ «обыскал — пусто».
			// SameTile — тот же порог, что гейтит серверную проверку клетки на попытке взять.
			if (me == null || (me.hp != null && me.hp <= 0) || marker == null || marker.Loot == null
				|| !EntityModel.SameTile(me.position, container.position))
			{
				Hide();
				return;
			}

			ShowLoot(key, marker);
		}

		/// <summary>Сущность-контейнер по её ключу — есть ли она ещё в мире (маркера добычи может не быть).</summary>
		private static EntityModel FindEntity(string key)
		{
			GameObject go = !string.IsNullOrEmpty(key) ? GameObject.Find(key) : null;
			return go != null ? go.GetComponent<EntityModel>() : null;
		}

		/// <summary>
		/// Маркер трупа key через кеш. Промах (сущности ещё/уже нет на сцене) не кешируется — иначе
		/// отложенное открытие не дождалось бы появления тела.
		/// </summary>
		private static CorpseLootMarker FindMarker(string key)
		{
			if (_markerKey == key && _marker != null) return _marker;

			_marker = CorpseLootMarker.Find(key);
			_markerKey = _marker != null ? key : null;
			return _marker;
		}

		/// <summary>Открыть/перерисовать окно по содержимому добычи трупа key.</summary>
		private static void ShowLoot(string key, CorpseLootMarker marker)
		{
			LootWindowController instance = _instance;
			if (instance == null) return;

			// Слоты пересоздаём ТОЛЬКО на смене трупа либо пришедшей дельте добычи: RefreshWindow
			// зовётся каждый кадр, а пересборка Item'ов рвала бы перетаскивание.
			bool opening = _containerKey != key;
			bool rebuild = opening || _renderedVersion != marker.Version;

			_containerKey = key;
			_pendingKey = null;

			if (_lootSlots == null)
			{
				instance.InitializeSlots();
				if (_lootSlots == null) return;
				rebuild = true;
			}

			if (rebuild)
			{
				_renderedVersion = marker.Version;

				Dictionary<int, ItemSlotRecive> loot = marker.Loot;

				for (int i = 0; i < _lootSlots.Length; i++)
				{
					SlotScript slotUI = _lootSlots[i];
					slotUI.Clear();

					if (loot != null && loot.TryGetValue(i + 1, out ItemSlotRecive data) && data != null && !string.IsNullOrEmpty(data.prefab))
					{
						// SlotNum=0: предмет НЕ в инвентаре игрока — инвентарные ветки Item.Use
						// (LocalSwap/equip по SlotNum) не должны срабатывать; позицию добычи
						// несёт LootSlotMarker слота-родителя.
						instance.RenderSlotItem(slotUI, instance.lootItemPrefab, data, 0);
					}
				}
			}

			// Чужая добыча до истечения срока — окно видно (что лежит и сколько ждать), но не активно:
			// зеркалим серверную проверку права, чтобы не слать заведомо отбиваемую команду.
			bool canTake = marker.CanTake(PlayerController.Player != null ? PlayerController.Player.key : null);

			if (_lootGroup != null)
			{
				_lootGroup.alpha = 1;
				_lootGroup.blocksRaycasts = true;
				_lootGroup.interactable = canTake;
			}

			// Пустой контейнер («обыскал — пусто») кнопку не показывает вовсе: забирать нечего, а сама
			// кнопка лежит поверх сетки и мешала бы класть в него своё.
			if (_takeAllButton != null)
			{
				_takeAllButton.gameObject.SetActive(marker.HasLoot);
				_takeAllButton.interactable = canTake;
			}

			// Перетаскивание труп↔инвентарь требует оба окна: инвентарь открываем вместе с добычей —
			// но только В МОМЕНТ открытия, иначе покадровый пересчёт не давал бы игроку закрыть
			// инвентарь клавишей, пока он стоит на трупе.
			if (opening)
			{
				instance.inventoryGroup.alpha = 1;
				instance.inventoryGroup.blocksRaycasts = true;
			}
		}

		// Сетка слотов добычи — размер как у инвентаря игрока (ёмкость добычи на сервере равна числу
		// слотов инвентаря).
		private void InitializeSlots()
		{
			// инвентарь игрока приходит при входе — к моменту первого пакета с добычей размер известен
			int count = InventoryController.SlotCount;
			if (count == 0)
			{
				Error("окно добычи открыто до получения инвентаря игрока");
				return;
			}

			FitWindowToSlots(count);

			_lootSlots = SlotScript.BuildGrid(lootSlotPrefab, lootSlotArea, count, "LootSlot", tooltip, (slot, i) =>
			{
				slot.SlotNum = 0;   // не инвентарный номер — против ложных инвентарных веток
				slot.gameObject.AddComponent<LootSlotMarker>().Num = i + 1;
			});
		}

		/// <summary>
		/// Подогнать окно под фактическое число слотов: ёмкость добычи задаёт сервер (равна инвентарю),
		/// поэтому строк сетки заранее не известно — фон, растянутый под фиксированный размер, оставлял бы
		/// слоты за своими краями поверх кнопки. Считаем размер сетки по её раскладке, а окно — по нему;
		/// отступы под заголовок и кнопку берём из самой области слотов (её sizeDelta — эти же поля со
		/// знаком минус, растяжение по обеим осям), чтобы правка отступов в сцене не требовала правки кода.
		/// </summary>
		private void FitWindowToSlots(int count)
		{
			GridLayoutGroup grid = lootSlotArea.GetComponent<GridLayoutGroup>();
			RectTransform area = lootSlotArea as RectTransform;
			RectTransform window = lootGroup.transform as RectTransform;
			if (grid == null || area == null || window == null)
			{
				Error("окно добычи: у области слотов нет раскладки-сетки либо окно не UI-объект");
				return;
			}

			int columns = Mathf.Max(1, grid.constraintCount);
			int rows = Mathf.CeilToInt((float)count / columns);

			float gridWidth = grid.padding.left + grid.padding.right
				+ columns * grid.cellSize.x + (columns - 1) * grid.spacing.x;
			float gridHeight = grid.padding.top + grid.padding.bottom
				+ rows * grid.cellSize.y + (rows - 1) * grid.spacing.y;

			window.sizeDelta = new Vector2(gridWidth - area.sizeDelta.x, gridHeight - area.sizeDelta.y);
		}

		private static void Hide()
		{
			_containerKey = null;
			_renderedVersion = -1;
			if (_lootGroup != null)
			{
				_lootGroup.alpha = 0;
				_lootGroup.blocksRaycasts = false;
				_lootGroup.interactable = true;
			}
		}

		/// <summary>Разрешает ли право на добычу забирать из ОТКРЫТОГО трупа (нет открытого — нет и забора).</summary>
		public static bool CanTakeFromOpen()
		{
			if (_containerKey == null) return false;

			CorpseLootMarker marker = FindMarker(_containerKey);
			return marker != null && marker.CanTake(PlayerController.Player != null ? PlayerController.Player.key : null);
		}

		/// <summary>
		/// Забрать позиции добычи в свой инвентарь. to (желаемый свой слот) сервер учитывает, только
		/// когда позиция одна. Перенос исполняет владелец-источник, ответ приезжает дельтой компонента.
		/// </summary>
		public static void SendTake(List<int> idx, int? to = null)
		{
			if (_containerKey == null || idx == null || idx.Count == 0) return;
			if (!CanTakeFromOpen()) return;

			LootTakeResponse response = new LootTakeResponse();
			response.key = _containerKey;
			response.idx = idx;
			response.to = to;
			response.Send();
		}

		/// <summary>Забрать всё: одна команда со списком всех занятых позиций.</summary>
		public static void SendTakeAll()
		{
			if (_containerKey == null) return;

			CorpseLootMarker marker = FindMarker(_containerKey);
			if (marker == null) return;

			SendTake(marker.OccupiedSlots());
		}

		// Положить свой предмет (позиция idx своего инвентаря) в добычу трупа (to — позиция добычи).
		public static void SendPut(int idx, int? to = null)
		{
			if (_containerKey == null) return;

			LootPutResponse response = new LootPutResponse();
			response.key = _containerKey;
			response.idx = idx;
			response.to = to;
			response.Send();
		}
	}
}
