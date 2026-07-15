using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	// Окно контейнера (лут трупа, позже сундук #17): сетка слотов по паттерну инвентаря.
	// Контракт сервера (открытие — ui/loot/open; перенос/перестановка — группа ui/inventory:
	// take/put/index с key, сервер сериализует операции гейтом «группа занята»):
	//   - содержимое контейнера приходит обыскивающему игроку АДРЕСНО, обычной world-дельтой
	//     самой сущности-контейнера: components.inventory чужого key (см. EnemyComponentsRecive.inventory).
	//     Данные принадлежат сущности (несут её key) → текут world-каналом, не кастомным полем игрока;
	//   - реакция — в UpdateObject (ветка key != player_key), не в отдельном пакете loot;
	//   - каждая операция (take/put/перестановка) отвечает свежей дельтой — окно перерисовывается
	//     по ней, локально ничего не двигаем (сервер — source of truth);
	//   - пустой inventory (JSON `[]` → Count==0) = контейнер пуст → окно ПОКАЗЫВАЕТСЯ пустым
	//     (сигнал «обыскал — пусто»; молчание неотличимо от несработавшего клика).
	// Взятие предмета контейнера в курсор делает базовый SlotScript-клик; принадлежность слота
	// контейнеру Item.Use определяет меткой LootSlotMarker (на слоте-цели и на родителе Item).
	abstract public class LootWindowController : ActionBarsController
	{
		[Header("Для работы с окном лута (контейнер трупа/сундука)")]

		// панель окна лута (включается при открытии). Слоты создаются в lootSlotArea из тех же
		// slotPrefab/itemPrefab, что инвентарь (биндинги ниже).
		[SerializeField]
		private CanvasGroup lootGroup;

		[SerializeField]
		private Transform lootSlotArea;

		[SerializeField]
		private GameObject lootSlotPrefab;

		[SerializeField]
		private Item lootItemPrefab;

		// key открытого контейнера; null — окно закрыто. static — Item.Use шлёт команды
		// take/put/reorder без поиска инстанса контроллера (паттерн InventoryController._slots).
		private static string _containerKey;
		private static SlotScript[] _lootSlots;
		private static CanvasGroup _lootGroup;

		public static string ContainerKey
		{
			get { return _containerKey; }
		}

		protected override void Awake()
		{
			base.Awake();

			// статики чистить вручную: Enter Play Mode без Domain Reload их не сбрасывает
			_containerKey = null;
			_lootSlots = null;
			_lootGroup = lootGroup;

			if (lootGroup == null)
			{
				Error("не указана CanvasGroup окна лута");
				return;
			}

			if (lootSlotArea == null)
			{
				Error("не указан Transform контейнер слотов окна лута");
				return;
			}

			if (lootSlotPrefab == null || lootItemPrefab == null)
			{
				Error("не указаны префабы слота/предмета окна лута");
				return;
			}

			Hide();
		}

		// Содержимое контейнера приходит АДРЕСНО world-дельтой самой сущности — реагируем в UpdateObject.
		// Свой игрок (key == player_key) не наш случай (его инвентарь наполняет InventoryController);
		// обрабатываем только чужой key, у которого в пакете есть компонент inventory.
		protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
		{
			if (key != player_key)
			{
				// components полиморфны (shadowed new): у player-группы Newtonsoft заполняет
				// PlayerRecive.components, у entity-группы — EnemyRecive.components; inventory лежит
				// в общем базовом EnemyComponentsRecive, поэтому читаем через базовый тип.
				EnemyComponentsRecive components = recive is PlayerRecive playerRecive
					? playerRecive.components
					: ((EnemyRecive)recive).components;

				if (components != null)
				{
					// Контейнер открытого лута ОЖИЛ (hp контейнера стало >0) — закрыть окно: зеркалит
					// серверный отказ open/take/put при воскрешении. hp>0 приходит world-дельтой самой
					// сущности-контейнера тем же каналом, что и inventory; проверяем ДО inventory —
					// в пакете воскрешения может прийти и то, и другое, приоритет за закрытием.
					if (key == _containerKey && components.hp != null && components.hp > 0)
						Hide();
					else if (components.inventory != null)
					{
						// пустой inventory (JSON `[]` → Count==0) тоже показываем: игрок видит «обыскал —
						// пусто» вместо молчания, неотличимого от «клик не сработал»
						ShowLoot(key, components.inventory);
					}
				}
			}

			GameObject prefab = base.UpdateObject(map_id, key, recive);

			// Авто-закрытие: контейнер доступен, ТОЛЬКО пока игрок стоит на ЕГО клетке (серверное
			// правило ui/loot — open/take/put валидируют same-tile, шаг в сторону = уход). После base
			// серверная позиция игрока из этого пакета уже применена — сверяем его клетку с клеткой
			// контейнера тем же порогом, что гейтит открытие (EntityModel.SameTile). Сошёл с клетки —
			// или контейнер исчез из мира (труп распался, GameObject.Find вернул null) — закрыть окно.
			if (key == player_key && _containerKey != null && player != null)
			{
				GameObject container = GameObject.Find(_containerKey);
				EntityModel containerModel = container != null ? container.GetComponent<EntityModel>() : null;
				if (containerModel == null || !EntityModel.SameTile(player.position, containerModel.position))
					Hide();
			}

			return prefab;
		}

		// Открыть/перерисовать окно лута по содержимому контейнера key (позиция → предмет).
		private void ShowLoot(string key, Dictionary<int, InventorySlotRecive> items)
		{
			_containerKey = key;

			if (_lootSlots == null)
				InitializeSlots();

			for (int i = 0; i < _lootSlots.Length; i++)
			{
				SlotScript slotUI = _lootSlots[i];
				slotUI.Clear();

				if (items.TryGetValue(i + 1, out InventorySlotRecive data) && data != null && !string.IsNullOrEmpty(data.prefab))
				{
					// SlotNum=0: предмет НЕ в инвентаре игрока — инвентарные ветки Item.Use
					// (LocalSwap/equip по SlotNum) не должны срабатывать; позицию контейнера
					// несёт LootSlotMarker слота-родителя.
					RenderSlotItem(slotUI, lootItemPrefab, data, 0);
				}
			}

			lootGroup.alpha = 1;
			lootGroup.blocksRaycasts = true;

			// перетаскивание контейнер↔инвентарь требует оба окна: инвентарь открываем вместе с лутом
			inventoryGroup.alpha = 1;
			inventoryGroup.blocksRaycasts = true;
		}

		// Сетка слотов контейнера — размер как у инвентаря игрока (у сервера один компонент inventory
		// на всех носителей, число позиций одинаковое).
		private void InitializeSlots()
		{
			// инвентарь игрока приходит при входе — к моменту первого loot-пакета размер известен
			int count = InventoryController.SlotCount;
			if (count == 0)
			{
				Error("окно лута открыто до получения инвентаря игрока");
				return;
			}

			_lootSlots = SlotScript.BuildGrid(lootSlotPrefab, lootSlotArea, count, "LootSlot", tooltip, (slot, i) =>
			{
				slot.SlotNum = 0;   // не инвентарный номер — против ложных инвентарных веток
				slot.gameObject.AddComponent<LootSlotMarker>().Num = i + 1;
			});
		}

		public static void Hide()
		{
			_containerKey = null;
			if (_lootGroup != null)
			{
				_lootGroup.alpha = 0;
				_lootGroup.blocksRaycasts = false;
			}
		}

		// Клик по мёртвой сущности рядом (CursorController) — запросить содержимое.
		public static void Open(string key)
		{
			LootResponse response = new LootResponse();
			response.action = "open";
			response.key = key;
			response.Send();
		}

		// Забрать позицию idx контейнера в свой инвентарь (to — конкретный слот, null — стак/первый свободный).
		// Перенос между инвентарями — операция группы ui/inventory (сервер: списание исполняет владелец-контейнер).
		public static void SendTake(int idx, int? to = null)
		{
			if (_containerKey == null) return;
			InventoryResponse response = new InventoryResponse();
			response.action = "take";
			response.key = _containerKey;
			response.idx = idx;
			response.to = to;
			response.Send();
		}

		// Положить свой предмет (позиция idx своего инвентаря) в контейнер (to — позиция контейнера).
		public static void SendPut(int idx, int? to = null)
		{
			if (_containerKey == null) return;
			InventoryResponse response = new InventoryResponse();
			response.action = "put";
			response.key = _containerKey;
			response.idx = idx;
			response.to = to;
			response.Send();
		}

		// Перестановка ВНУТРИ контейнера: полный пересейв его инвентаря с обменом from↔to
		// (ui/inventory с key — сервер валидирует «ничего не появилось» над инвентарём цели).
		public static void SendReorder(int from, int to)
		{
			if (_containerKey == null || _lootSlots == null || from == to) return;

			InventoryResponse response = new InventoryResponse();
			response.key = _containerKey;
			// содержимое читаем из UI-слотов с обменом позиций from/to
			response.inventory = SnapshotSlots(_lootSlots, pos => pos == from ? to : (pos == to ? from : pos));

			response.Send();
		}
	}
}
