using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	// Окно добычи трупа: сетка слотов по паттерну инвентаря.
	// Контракт сервера:
	//   - содержимое добычи — ПУБЛИЧНЫЙ компонент loot самой сущности-трупа: приезжает всем видящим
	//     обычной world-дельтой, отдельной команды открытия НЕТ (группа ui/loot удалена, отправка
	//     неизвестной группы = дисконнект). Данные оседают на самом трупе (CorpseLootMarker);
	//   - когда ПОКАЗЫВАТЬ окно, решает клиент: игрок стоит на клетке трупа. Клик по трупу издалека —
	//     обычное движение к его клетке (тот же путь, что клик по земле), окно открывается по прибытии;
	//   - право на добычу (компонент loot_owner) сервер проверяет на попытке взять: до истечения срока
	//     берёт только владелец, после — любой, отказ тихий. Клиент это ЗЕРКАЛИТ неактивными кнопками —
	//     UX-фильтр, а не замена серверной проверки;
	//   - каждая операция (take/put) отвечает свежей дельтой компонента loot — окно перерисовывается
	//     по ней, локально ничего не двигаем (сервер — source of truth);
	//   - опустевшая добыча закрывает окно: труп с пустой добычей вообще не рисуется (CorpseLootMarker).
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

		// Состояние окна пересчитывается каждый кадр, а не только на пакете: окончание чужого
		// эксклюзива событием не приходит (значение loot_owner не обнуляется, «истёк ли» — сравнение
		// с текущим временем), и без покадрового пересчёта кнопки остались бы заблокированными до
		// ближайшей дельты. Отсчёт на трупе гаснет тем же способом (CorpseLootMarker.LateUpdate).
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

			if (prefab != null && components != null && (components.loot != null || components.loot_owner != null))
				CorpseLootMarker.Apply(prefab, components);

			// Немедленный пересчёт на пришедшей дельте: игрок сдвинулся (сошёл с клетки / дошёл до цели)
			// либо изменилась сама добыча — окно догоняет в том же кадре, не ожидая ближайшего Update.
			// После base серверная позиция игрока из этого пакета уже применена.
			if (key == player_key || key == _containerKey || key == _pendingKey)
				RefreshWindow();

			return prefab;
		}

		/// <summary>
		/// Клик по трупу. Игрок уже на его клетке — открываем окно локально (на сервер не уходит ничего)
		/// и возвращаем true. Иначе запоминаем цель, возвращаем false: вызывающий ведёт игрока к клетке
		/// обычным движением, окно откроется по прибытии (RefreshWindow на пакете с новой позицией).
		/// </summary>
		public static bool RequestOpen(EntityModel corpse)
		{
			if (corpse == null) return false;

			_pendingKey = corpse.key;
			RefreshWindow();
			return _containerKey == corpse.key;
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
			EntityModel corpse = marker != null ? marker.GetComponent<EntityModel>() : null;

			// Сущность исчезла (тело распалось) — отложенное открытие больше не состоится.
			if (corpse == null)
			{
				_pendingKey = null;
				Hide();
				return;
			}

			// Труп ожил (воскрешение гасит добычу), добыча пуста, игрок сошёл с клетки либо сам мёртв
			// (мёртвый не лутает — тот же гейт держит сервер, а умереть на клетке трупа обычное дело) —
			// окна нет. SameTile — тот же порог, что гейтит серверную проверку клетки на попытке взять.
			if (me == null || (me.hp != null && me.hp <= 0) || corpse.action != "dead" || !marker.HasLoot
				|| !EntityModel.SameTile(me.position, corpse.position))
			{
				Hide();
				return;
			}

			ShowLoot(key, marker);
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

			if (_takeAllButton != null)
				_takeAllButton.interactable = canTake;

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

			_lootSlots = SlotScript.BuildGrid(lootSlotPrefab, lootSlotArea, count, "LootSlot", tooltip, (slot, i) =>
			{
				slot.SlotNum = 0;   // не инвентарный номер — против ложных инвентарных веток
				slot.gameObject.AddComponent<LootSlotMarker>().Num = i + 1;
			});
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
