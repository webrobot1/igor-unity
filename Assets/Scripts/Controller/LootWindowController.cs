using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	// Окно добычи контейнера — трупа существа, объекта-сундука либо лавки торговца: сетка слотов по
	// паттерну инвентаря.
	//
	// Лавка — тот же контейнер, только платный: покупка это забор из него (take), продажа — укладывание
	// в него (put), отдельных команд сделки нет. Отличает её ЗАПОЛНЕННЫЙ ценник (компонент trade, см.
	// TradeRecive): он публичен, приходит всем видящим и несёт множители обеих сторон, а у неторгующих
	// объектов приходит пустым. Базовая цена предмета лежит у самого предмета и приезжает манифестом
	// префабов (AnimationCacheService.GetComponentValue) — множители лишь пересчитывают её на свою сторону.
	// Считает цену и решает, состоялась ли сделка, сервер — клиент её только показывает и не шлёт
	// заведомо отбиваемого (нечем платить, вещь без цены скупки, пустая касса торговца).
	// Контракт сервера:
	//   - состав добычи ПРИВАТЕН: приходит только тому, кто стоит на клетке контейнера, — по команде
	//     открытия (ui/loot/open) и дальше при каждом изменении состава. Данные оседают на самой
	//     сущности (CorpseLootMarker), поэтому окно открывается по ПРИХОДУ состава, а не решением клиента;
	//   - клик по контейнеру издалека: команду шлём сразу, сервер сам ведёт игрока к его клетке и
	//     повторяет открытие до прибытия — своего подхода клиент не изобретает;
	//   - пустой состав тоже приходит и показывается пустым окном («обыскал — пусто»); окно держится,
	//     пока игрок стоит на клетке, и закрывается сходом с неё либо исчезновением самой сущности;
	//   - право на добычу (список допущенных в данных команды status/lootfree) держит сервер: пока добыча
	//     закреплена, состав уходит только допущенным — недопущенному не приходит ни содержимое, ни само
	//     окно, а сколько ему ждать, показывает полоска над телом; допускать больше некого — сервер
	//     команду не перевешивает, и добыча свободна любому;
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

		// Заголовок окна: над ним стоит имя самого контейнера («Торговец», «Сундук») — торг и обыск
		// тела разные занятия, и одним словом их не назвать.
		[SerializeField]
		private Text lootTitle;

		// key открытого трупа; null — окно закрыто. static — Item.Use шлёт команды take/put без поиска
		// инстанса контроллера (паттерн InventoryController._slots).
		private static string _containerKey;

		// key трупа, к которому игрок идёт по клику: окно откроется по прибытии на его клетку.
		private static string _pendingKey;

		private static SlotScript[] _lootSlots;
		private static CanvasGroup _lootGroup;
		private static Button _takeAllButton;

		// Заголовок окна и подпись из сцены: она остаётся запасной на случай, когда имени у префаба
		// контейнера нет вовсе, — потому исходную запоминаем, а не переписываем в одну сторону.
		private static Text _title;
		private static string _titleDefault;

		// Маркер трупа, с которым работает окно (открытого либо того, к которому идём). Кеш нужен
		// потому, что состояние окна пересчитывается КАЖДЫЙ кадр — сход с клетки и распад тела закрывают
		// его без всякого пакета, — а GameObject.Find на каждом кадре для этого слишком дорог.
		private static CorpseLootMarker _marker;
		private static string _markerKey;

		// Версия добычи, по которой отрисованы слоты сейчас (-1 — окно закрыто/не отрисовано).
		private static int _renderedVersion = -1;

		// Нажато «забрать всё» и ответа ещё нет: опустевший контейнер закроет окна, непустой оставит
		// открытыми — часть предметов могла не влезть в инвентарь.
		private static bool _closeWhenEmpty;

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

			if (lootTitle == null)
			{
				Error("не назначен заголовок окна добычи (Text) — у лавки его менять нечем");
				return;
			}

			_title = lootTitle;
			_titleDefault = lootTitle.text;

			// Метка окна целиком: по ней Item.Use узнаёт промах мимо ячейки, но внутрь окна.
			// Ставится кодом, как и метки самих ячеек: их число и состав задаёт сервер, а не сцена.
			if (lootGroup.GetComponent<LootWindowMarker>() == null)
				lootGroup.gameObject.AddComponent<LootWindowMarker>();

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
			CreatureComponentsRecive components = null;

			if (key != player_key)
			{
				// components полиморфны (shadowed new): у player-группы Newtonsoft заполняет
				// PlayerRecive.components, у entity-группы — CreatureRecive.components; loot лежит
				// в общем базовом CreatureComponentsRecive, поэтому читаем через базовый тип.
				components = recive is PlayerRecive playerRecive
					? playerRecive.components
					: ((CreatureRecive)recive).components;
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

			// Ценник лавки — такой же публичный повод завести маркер, как право на добычу: он приходит
			// раньше состава (тот приватен и ждёт подхода игрока) и НЕ повторяется — дельта шлёт компонент
			// один раз, поэтому без маркера в этот момент ценник потерялся бы совсем. Повод даёт только
			// ЗАПОЛНЕННЫЙ ценник: пустой приходит каждому объекту, и маркер висел бы на всём подряд.
			bool trades = components != null && components.trade != null && components.trade.Trades;

			if (prefab != null && (right || trades || (components != null && components.loot != null)))
				CorpseLootMarker.Apply(prefab, components);

			// Немедленный пересчёт на пришедшей дельте: игрок сдвинулся (сошёл с клетки / дошёл до цели)
			// либо изменилась сама добыча — окно догоняет в том же кадре, не ожидая ближайшего Update.
			// После base серверная позиция игрока из этого пакета уже применена.
			if (key == player_key || key == _containerKey || key == _pendingKey)
				RefreshWindow();

			return prefab;
		}

		// Имена серверных компонентов добычи: у объекта-сундука и у лавки состав задан самим префабом (loot),
		// у существа он разыгрывается при смерти по таблице дропа (loot_table) — до смерти компонента
		// добычи на нём нет вовсе. Оба приходят в составе префаба (манифест /prefabs).
		// Ценника (trade) в этом списке НЕТ намеренно: компонент со структурным значением платформа заводит
		// всему виду object, поэтому в составе префаба он есть и у портала с алтарём — признаком лавки
		// служить не может. Лавку признаём по её товару, как любой контейнер, а торгует она или нет,
		// решают уже пришедшие множители (CorpseLootMarker.Trade).
		private const string COMPONENT_LOOT = "loot";
		private const string COMPONENT_LOOT_TABLE = "loot_table";

		/// <summary>
		/// Открываемый ли это контейнер — труп существа с дропом, объект-сундук либо лавка торговца. UX-фильтр
		/// перед отправкой открытия: зеркалит серверные гейты ui/loot/open (живую цель и сущность без добычи
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
		/// Ценник ОТКРЫТОГО контейнера; null — окно закрыто либо контейнер не торгует (пустой ценник
		/// отсеивает CorpseLootMarker.Trade). Единственная точка, по которой остальной интерфейс
		/// (подсказка предмета, подпись кнопки) узнаёт, что перед игроком лавка.
		/// </summary>
		public static TradeRecive OpenTrade
		{
			get
			{
				if (_containerKey == null) return null;

				CorpseLootMarker marker = FindMarker(_containerKey);
				return marker != null ? marker.Trade : null;
			}
		}

		/// <summary>
		/// Строка сделки для подсказки предмета, пока открыта лавка: у её товара — почём берут штуку,
		/// у своей вещи — сколько за штуку дадут. Обе стороны считаются и подаются одинаково, разнится
		/// лишь смысл: цену видно у любого предмета, на который игрок смотрит, с какой бы стороны прилавка
		/// тот ни лежал.
		/// ЗА ШТУКУ, а не за стак: стак из одной единицы уходит сразу, без вопроса о количестве, и цену
		/// игрок должен видеть ДО клика; сколько выйдет за несколько, считает окно выбора количества.
		/// Вещь вне торговли — словами, а не нулём: ноль монет читался бы ценой, хотя сделки не будет вовсе.
		/// null — лавка не открыта либо предмет в ней не участвует (монеты, предмет в руке).
		/// Знание о торговле держит окно лавки, а не предмет: предмет один и тот же и в инвентаре, и на
		/// прилавке — сторону сделки задаёт то, где он лежит.
		/// </summary>
		public static string TradeHint(Item item)
		{
			TradeRecive trade = OpenTrade;

			if (trade == null || item == null || string.IsNullOrEmpty(item.Prefab))
				return null;

			// Товар прилавка узнаём по метке слота, в котором он лежит: инвентарного номера у него нет
			// (SlotNum = 0), и от предмета в руке отличает его только место.
			if (item.GetComponentInParent<LootSlotMarker>() != null)
			{
				int? buy = trade.BuyPrice(item.Prefab, item.Components, 1);

				return buy != null ? "Купить за штуку: " + Coins(buy.Value) : "Торговец это не продаёт";
			}

			if (item.SlotNum <= 0) return null;

			// Монеты — сама плата, торговцу их не продают.
			if (item.Prefab == MONEY_PREFAB) return null;

			int? sell = trade.SellPrice(item.Prefab, item.Components, 1);

			return sell != null ? "Продать за штуку: " + Coins(sell.Value) : "Торговец это не покупает";
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
			bool gone;

			// Сперва — контейнер, к которому игрок ИДЁТ: он вытесняет открытое окно, как только готов
			// показаться сам (дошли, и состав пришёл). Пока не готов, на экране остаётся прежнее окно —
			// иначе оно гасло бы на всю дорогу до новой цели. Решает готовность НОВОГО, а не сход с клетки
			// старого: контейнеры бывают и на одной клетке (тело упало на сундук), и сходить тогда не с чего.
			if (_pendingKey != null)
			{
				if (TryShow(_pendingKey, out gone)) return;

				// Цель распалась, пока игрок шёл, — идти больше не к чему.
				if (gone) _pendingKey = null;
			}

			// Открытое окно держится, пока игрок стоит на клетке своего контейнера.
			if (_containerKey != null && !TryShow(_containerKey, out gone))
				Hide();
		}

		/// <summary>
		/// Показать окно контейнера key, если ему есть что показать прямо сейчас, и сообщить, состоялся ли
		/// показ. Показывается контейнер, который ещё в мире и чей состав добычи пришёл, — игроку живому и
		/// стоящему на его клетке (мёртвый не лутает: тот же гейт держит сервер, а умереть на клетке трупа
		/// обычное дело). SameTile — тот же порог, что гейтит серверную проверку клетки на попытке взять.
		/// Пустой состав показу НЕ мешает: он и есть ответ «обыскал — пусто».
		/// gone — самой сущности в мире больше нет (тело распалось): ждать её бессмысленно, в отличие от
		/// прочих отказов, которые снимаются приходом состава либо приходом самого игрока.
		/// </summary>
		private static bool TryShow(string key, out bool gone)
		{
			gone = false;

			CorpseLootMarker marker = FindMarker(key);

			// Сам контейнер ищем в мире, а маркер добычи — отдельно: у объекта-сундука публичного
			// признака добычи нет вовсе (у лавки он есть — заполненный ценник), и до прихода приватного
			// состава маркера на сундуке ЕЩЁ НЕТ.
			// Считать «нет маркера» за «контейнер исчез» нельзя — отложенное открытие гасло бы на
			// первом же кадре ожидания, и пришедший следом состав окно уже не открывал.
			// Поиск по сцене — только на этой ветке: у открытого окна маркер есть и берётся из кеша.
			EntityModel container = marker != null ? marker.GetComponent<EntityModel>() : FindContainer(key);

			if (container == null)
			{
				gone = true;
				return false;
			}

			PlayerModel me = PlayerController.Player;

			if (me == null || (me.hp != null && me.hp <= 0) || marker == null || marker.Loot == null
				|| !EntityModel.SameTile(me.position, container.position))
				return false;

			ShowLoot(key, marker, container);
			return true;
		}

		/// <summary>
		/// Сущность-контейнер по её ключу — есть ли она ещё в мире (маркера добычи может не быть).
		/// Спрашиваем общий реестр сущностей, а не сцену: ответ нужен КАЖДЫЙ кадр всю дорогу до цели,
		/// пока состав ещё не пришёл, а поиск по сцене обходит её целиком на каждый такой вопрос.
		/// </summary>
		private static EntityModel FindContainer(string key)
		{
			GameObject go = !string.IsNullOrEmpty(key) ? FindEntity(key) : null;
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

		/// <summary>
		/// Открыть/перерисовать окно по содержимому добычи контейнера key. container — сама сущность
		/// (её префаб даёт заголовок окна); вызывающий её уже нашёл, второй раз не ищем.
		/// </summary>
		private static void ShowLoot(string key, CorpseLootMarker marker, EntityModel container)
		{
			LootWindowController instance = _instance;
			if (instance == null) return;

			// Слоты пересоздаём ТОЛЬКО на смене трупа либо пришедшей дельте добычи: RefreshWindow
			// зовётся каждый кадр, а пересборка Item'ов рвала бы перетаскивание.
			bool opening = _containerKey != key;
			bool rebuild = opening || _renderedVersion != marker.Version;

			_containerKey = key;

			// Снимаем ТОЛЬКО своё отложенное открытие: пока показывается один контейнер, игрок мог уже
			// кликнуть по другому — его намерение живёт своей жизнью, и стирать его чужим показом нельзя,
			// иначе дойдя до новой цели игрок остался бы вовсе без окна.
			if (_pendingKey == key) _pendingKey = null;

			if (_lootSlots == null)
			{
				instance.InitializeSlots();
				if (_lootSlots == null) return;
				rebuild = true;
			}

			// Ценник контейнера (null — не лавка). Он же двигает Version, поэтому запоздавший ценник сам
			// вызывает пересборку слотов и цены появляются на уже открытом окне.
			TradeRecive trade = marker.Trade;

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
						// Цену тут не проставляем: она стоит в подсказке предмета, у обеих сторон прилавка
						// в одном виде (TradeHint), а угол ячейки занят количеством в стаке.
						instance.RenderSlotItem(slotUI, instance.lootItemPrefab, data, 0);
					}
				}
			}

			// Право на добычу окно не гасит: чужой состав сервер не присылает вовсе, и открыться ему не на
			// чем. Открытое окно запретным уже не станет — круг допущенных только ширится.
			if (_lootGroup != null)
			{
				_lootGroup.alpha = 1;
				_lootGroup.blocksRaycasts = true;
				_lootGroup.interactable = true;
			}

			// Заголовок — имя самого контейнера, как его назвали префабу в админке («Торговец», «Сундук»):
			// торг и обыск тела разные занятия, и общей подписью их не назвать. Имя необязательно —
			// не задано, остаётся подпись из сцены: slug на её месте игроку ничего не говорит.
			if (_title != null)
				_title.text = AnimationCacheService.GetPrefabName(container.prefab) ?? _titleDefault;

			// Пустой контейнер («обыскал — пусто») кнопку не показывает вовсе: забирать нечего, а сама
			// кнопка лежит поверх сетки и мешала бы класть в него своё. У лавки кнопки нет ни при каком
			// составе — скупать прилавок целиком одним нажатием игроку незачем (решение пользователя),
			// а денег на это не хватало бы почти всегда.
			if (_takeAllButton != null)
			{
				_takeAllButton.gameObject.SetActive(marker.HasLoot && trade == null);
				_takeAllButton.interactable = true;
			}

			// «Забрать всё» закрывает оба окна — но по опустевшему контейнеру, а не по самому нажатию:
			// невместившееся остаётся в трупе, и закрытие вслепую унесло бы остаток с глаз молча.
			// Инвентарь закрывает сам Hide — он открывался вместе с контейнером.
			if (_closeWhenEmpty && !marker.HasLoot)
			{
				_closeWhenEmpty = false;
				Hide();
				return;
			}

			// Перетаскивание труп↔инвентарь требует оба окна: инвентарь открываем вместе с добычей —
			// но только В МОМЕНТ открытия, иначе покадровый пересчёт не давал бы игроку закрыть
			// инвентарь клавишей, пока он стоит на трупе.
			// У лавки инвентарь держим открытым ВСЁ время торга: в нём и товар на продажу, и плашка денег —
			// сколько монет осталось, игрок обязан видеть, пока покупает.
			if (opening || trade != null)
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
			// Инвентарь открывался вместе с контейнером (см. ShowLoot) — вместе с ним и убирается:
			// отошёл от торговца, тела либо сундука — экран чист, как до подхода. Закрываем только
			// то, что БЫЛО открыто: покадровое ожидание подхода (_containerKey ещё null, идём к цели)
			// иначе держало бы инвентарь закрытым всю дорогу, не давая игроку открыть его самому.
			bool wasOpen = _containerKey != null;

			_containerKey = null;
			_renderedVersion = -1;
			_closeWhenEmpty = false;
			if (_lootGroup != null)
			{
				_lootGroup.alpha = 0;
				_lootGroup.blocksRaycasts = false;
				_lootGroup.interactable = true;
			}

			if (wasOpen && _instance != null && _instance.inventoryGroup != null)
			{
				_instance.inventoryGroup.alpha = 0;
				_instance.inventoryGroup.blocksRaycasts = false;
			}
		}

		/// <summary>
		/// Во сколько обойдётся покупка позиций idx у этого торговца; null — среди них есть то, чего он не
		/// продаёт: сервер такую сделку отбивает целиком («ноль значит сделки нет»), значит и покупки нет.
		/// </summary>
		private static int? TotalPrice(CorpseLootMarker trader, List<int> idx)
		{
			TradeRecive trade = trader != null ? trader.Trade : null;
			if (trade == null || trader.Loot == null || idx == null) return 0;

			int total = 0;

			for (int i = 0; i < idx.Count; i++)
			{
				ItemSlotRecive data;
				if (!trader.Loot.TryGetValue(idx[i], out data) || data == null || string.IsNullOrEmpty(data.prefab))
					continue;

				int? price = trade.BuyPrice(data.prefab, data.components, data.count);
				if (price == null) return null;

				total += price.Value;
			}

			return total;
		}

		/// <summary>
		/// Касса контейнера — сколько монет лежит в нём самом: отдельного счёта у торговца нет, выручку он
		/// платит из тех же позиций, что и товар (счёт игрока считается так же — InventoryController.Money).
		/// </summary>
		private static int CashOf(CorpseLootMarker trader)
		{
			int total = 0;
			if (trader == null || trader.Loot == null) return total;

			foreach (var kv in trader.Loot)
				if (kv.Value != null && kv.Value.prefab == MONEY_PREFAB)
					total += kv.Value.count;

			return total;
		}

		/// <summary>
		/// Отдаст ли открытый контейнер позицию num: право на добычу разрешает, а у лавки предмет ещё и
		/// продаётся и по карману. Спрашивается ДО взятия предмета в курсор — иначе товар прилипал бы к
		/// курсору, а команда потом молча не уходила бы (те же условия в SendTake).
		/// У лавки порог — ОДНА единица, а не весь стак: сколько штук взять, игрок называет сам
		/// (окно количества в SendTake), и запирать от него стак, который он в состоянии купить
		/// частями, незачем.
		/// </summary>
		public static bool CanTakeSlot(int num)
		{
			if (!CanTakeFromOpen()) return false;

			CorpseLootMarker trader = FindMarker(_containerKey);
			if (trader == null || trader.Trade == null) return true;

			ItemSlotRecive item = SlotOf(trader, num);
			if (item == null) return false;

			int? unit = trader.Trade.BuyPrice(item.prefab, item.components, 1);
			return unit != null && unit.Value <= Money;
		}

		/// <summary>Предмет позиции num контейнера; null — позиция пуста либо состава ещё нет.</summary>
		private static ItemSlotRecive SlotOf(CorpseLootMarker container, int num)
		{
			ItemSlotRecive item;
			if (container == null || container.Loot == null || !container.Loot.TryGetValue(num, out item))
				return null;
			return item != null && !string.IsNullOrEmpty(item.prefab) ? item : null;
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

			CorpseLootMarker trader = FindMarker(_containerKey);
			TradeRecive trade = trader != null ? trader.Trade : null;

			// Обычный контейнер: забор бесплатен и идёт позициями целиком — спрашивать не о чем,
			// количество не шлём вовсе.
			if (trade == null)
			{
				SendTakeCommand(_containerKey, idx, to, null);
				return;
			}

			// Забор из лавки — покупка: не хватает монет либо просят непродаваемое (её же касса), и сервер
			// отказывает молча и целиком (частичной сделки у него нет). Заведомо отбиваемую команду не шлём.
			// Истина всё равно за сервером: он считает по свежему кошельку, мы — по последнему пришедшему
			// состоянию инвентаря, поэтому запоздалая дельта даёт лишь на миг заблокированную покупку.
			// Пачка позиций («забрать всё») у лавки не набирается — кнопки у неё нет (см. ShowLoot),
			// поэтому количество спрашиваем у одиночной позиции, а список берётся целиком по общей цене.
			if (idx.Count > 1)
			{
				int? total = TotalPrice(trader, idx);
				if (total == null || total.Value > Money) return;

				SendTakeCommand(_containerKey, idx, to, null);
				return;
			}

			ItemSlotRecive item = SlotOf(trader, idx[0]);
			if (item == null) return;

			// Цена ЕДИНИЦЫ, а не всего стака: по ней видно, сколько штук потянет кошелёк. Нет её —
			// товар не продаётся вовсе, и спрашивать количество не о чем (то же у продажи — SendPut).
			int? unit = trade.BuyPrice(item.prefab, item.components, 1);
			if (unit == null) return;

			int affordable = Money / unit.Value;
			if (affordable <= 0) return;                       // не по карману даже одна штука

			int max = Mathf.Min(item.count, affordable);

			// Одна единица вопроса не стоит: окно спрашивало бы то, на что ответ единственный. Число
			// всё равно шлём — весь стак игроку может быть не по карману, и «целиком» тут не то же самое.
			if (max <= 1)
			{
				SendTakeCommand(_containerKey, idx, to, max);
				return;
			}

			// Пока игрок вводит число, окно лавки может закрыться (сошёл с клетки) либо смениться на
			// другой контейнер — команду шлём, только если перед нами всё тот же торговец.
			string key = _containerKey;
			string title = "Купить: " + (AnimationCacheService.GetPrefabName(item.prefab) ?? item.prefab);

			QuantityPromptController.Ask(title, max, count =>
			{
				if (_containerKey == key)
					SendTakeCommand(key, idx, to, count);
			},
			// Сколько игрок отдаст за выбранное количество — считает тот же ценник, что и на ячейке товара.
			count =>
			{
				int? price = trade.BuyPrice(item.prefab, item.components, count);
				return price != null ? "Заплатите: " + Coins(price.Value) : null;
			});
		}

		/// <summary>
		/// Сама команда забора. count — сколько единиц суммарно забираем; пусто — позиции целиком
		/// (обычный контейнер, где дробить нечего и незачем).
		/// </summary>
		private static void SendTakeCommand(string key, List<int> idx, int? to, int? count)
		{
			LootTakeResponse response = new LootTakeResponse();
			response.key = key;
			response.idx = idx;
			response.to = to;
			response.count = count;
			response.Send();
		}

		/// <summary>
		/// Забрать всё: одна команда со списком занятых позиций. Опустевший после неё контейнер закроет
		/// окна сам (см. ShowLoot) — по факту пустоты, а не по нажатию: часть предметов могла не влезть
		/// в инвентарь и остаться в трупе. Кнопки этой у лавки нет (см. ShowLoot), поэтому платный забор
		/// сюда не приходит и отбирать продаваемое из списка не от чего.
		/// </summary>
		public static void SendTakeAll()
		{
			if (_containerKey == null) return;

			CorpseLootMarker marker = FindMarker(_containerKey);
			if (marker == null) return;

			_closeWhenEmpty = true;
			SendTake(marker.OccupiedSlots());
		}

		// Положить свой предмет (позиция idx своего инвентаря) в контейнер (to — позиция в нём); у лавки
		// то же укладывание и есть продажа.
		public static void SendPut(int idx, int? to = null)
		{
			if (_containerKey == null) return;

			// Укладывание в лавку — продажа: даром вещь не уходит. Сервер молча отбивает и вещь без цены
			// скупки, и сделку, на которую у торговца не хватает кассы, — оба условия зеркалим, иначе
			// предмет уезжал бы в чужое окно без всякого ответа. Что вещь не берут, игрок видит заранее
			// по её подсказке: скупка идёт от базовой цены предмета, и вещи без цены не торгуются вовсе.
			CorpseLootMarker trader = FindMarker(_containerKey);
			TradeRecive trade = trader != null ? trader.Trade : null;

			// Обычный контейнер: перенос бесплатен и идёт позицией целиком — спрашивать не о чем.
			if (trade == null)
			{
				SendPutCommand(_containerKey, idx, to, null);
				return;
			}

			Item item = GetItemBySlot(idx);
			if (item == null) return;

			// Цена ЕДИНИЦЫ, а не всего стака: по ней видно, сколько штук потянет касса торговца. Нет
			// её — вещь не покупают вовсе, и спрашивать количество не о чем.
			int? unit = trade.SellPrice(item.Prefab, item.Components, 1);
			if (unit == null) return;

			int affordable = CashOf(trader) / unit.Value;
			if (affordable <= 0) return;                       // касса пуста — сделки нет

			int max = Mathf.Min(item.Count, affordable);

			// Одна единица вопроса не стоит: окно спрашивало бы то, на что ответ единственный.
			if (max <= 1)
			{
				SendPutCommand(_containerKey, idx, to, max);
				return;
			}

			// Пока игрок вводит число, окно лавки может закрыться (сошёл с клетки) либо смениться на
			// другой контейнер — команду шлём, только если перед нами всё тот же торговец.
			string key = _containerKey;
			string title = "Продать: " + (AnimationCacheService.GetPrefabName(item.Prefab) ?? item.Prefab);

			QuantityPromptController.Ask(title, max, count =>
			{
				if (_containerKey == key)
					SendPutCommand(key, idx, to, count);
			},
			// Сколько игрок выручит за выбранное количество: цену сделки видно там, где она и совершается,
			// а в подсказке предмета стоит его базовая цена — она от торговца не зависит.
			count =>
			{
				int? price = trade.SellPrice(item.Prefab, item.Components, count);
				return price != null ? "Получите: " + Coins(price.Value) : null;
			});
		}

		/// <summary>
		/// Сама команда укладывания. count — сколько единиц позиции отдаём; пусто — позицию целиком
		/// (обычный контейнер, где дробить нечего и незачем).
		/// </summary>
		private static void SendPutCommand(string key, int idx, int? to, int? count)
		{
			LootPutResponse response = new LootPutResponse();
			response.key = key;
			response.idx = idx;
			response.to = to;
			response.count = count;
			response.Send();
		}
	}
}
