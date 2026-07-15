using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Контроллер инвентаря — управляет слотами и предметами
    /// </summary>
    abstract public class InventoryController : SpellBookController
    {
        [Header("Для работы с инвентарём")]

        [SerializeField]
        private Transform inventorySlotArea;

        [SerializeField]
        private GameObject slotPrefab;

        [SerializeField]
        private Item itemPrefab;

        private static SlotScript[] _slots;
        private static bool _dirty;
        private Dictionary<string, Item> _items;

        public Dictionary<string, Item> Items
        {
            get { return _items; }
            set { }
        }

        protected override void Awake()
        {
            base.Awake();

            _items = new Dictionary<string, Item>();
            _slots = null;
            _dirty = false;

            if (inventorySlotArea == null)
            {
                Error("не указан Transform контейнер для слотов инвентаря");
                return;
            }

            if (slotPrefab == null)
            {
                Error("не указан префаб слота инвентаря");
                return;
            }

            if (itemPrefab == null)
            {
                Error("не указан префаб предмета");
                return;
            }

            if (!inventorySlotArea.IsChildOf(inventoryGroup.transform))
            {
                Error("контейнер слотов инвентаря не является частью CanvasGroup инвентаря");
                return;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<int, InventorySlotRecive> inventory = ((PlayerRecive)recive).components.inventory;

                if (inventory != null)
                {
                    if (_slots == null)
                        InitializeSlots(inventory.Count);

                    _items = new Dictionary<string, Item>();

                    foreach (var slot in inventory)
                    {
                        if (slot.Key < 1 || slot.Key > _slots.Length)
                        {
                            Error("Пришел номер слота инвентаря " + slot.Key + " однако настроено лишь " + _slots.Length);
                            return null;
                        }

                        SlotScript slotUI = _slots[slot.Key - 1];
                        slotUI.Clear();

                        if (slot.Value != null && !string.IsNullOrEmpty(slot.Value.prefab))
                        {
                            Item item = RenderSlotItem(slotUI, itemPrefab, slot.Value, slot.Key);

                            _items[slot.Value.prefab] = item;

                            player?.Log("Инвентарь слот " + slot.Key + ": " + slot.Value.prefab + " x" + slot.Value.count);
                        }
                        else
                        {
                            slotUI.Clear();
                        }
                    }
                }
            }

            return base.UpdateObject(map_id, key, recive);
        }

        // создает пустые ячейки инвентаря в количестве count
        private void InitializeSlots(int count)
        {
            _slots = SlotScript.BuildGrid(slotPrefab, inventorySlotArea, count, "Slot", tooltip,
                (slot, i) => slot.SlotNum = i + 1);
        }

        /// <summary>
        /// Наполнить UI-слот предметом из данных слота инвентаря (общий рендер для окна инвентаря и окна
        /// контейнера): инстанс префаба, спрайт, тултип, счётчик. slotNum — номер СВОЕГО инвентаря
        /// (по нему ветвятся Item.Use/equip); 0 — предмет чужого контейнера (позицию несёт LootSlotMarker).
        /// </summary>
        protected Item RenderSlotItem(SlotScript slotUI, Item prefab, InventorySlotRecive data, int slotNum)
        {
            Item item = Instantiate(prefab, slotUI.transform);
            item.gameObject.SetActive(false);
            item.SetData(data.prefab);
            item.SetTooltip(tooltip);
            item.SlotNum = slotNum;
            item.Count = data.count;

            slotUI.SetItem(item, data.count, data.components);
            return item;
        }

        /// <summary>
        /// Снимок UI-слотов → словарь позиций для ui/inventory/index (null = пустая позиция); общий для
        /// пересейва своего инвентаря и перестановки контейнера. map — перестановка читаемых позиций
        /// (SendReorder свопает from/to); null — тождественная.
        /// </summary>
        protected static Dictionary<int, InventorySlotRecive> SnapshotSlots(SlotScript[] slots, System.Func<int, int> map = null)
        {
            Dictionary<int, InventorySlotRecive> snapshot = new Dictionary<int, InventorySlotRecive>();

            for (int i = 0; i < slots.Length; i++)
            {
                int pos = i + 1;
                SlotScript src = slots[(map != null ? map(pos) : pos) - 1];
                snapshot[pos] = src.Item != null
                    ? new InventorySlotRecive(src.Item.Prefab, src.Item.Count, src.Components)
                    : null;
            }

            return snapshot;
        }

        /// <summary>
        /// Число слотов инвентаря (0 — инвентарь ещё не приходил). У сервера один компонент
        /// inventory на всех носителей — окно контейнера (LootWindowController) строит сетку того же размера.
        /// </summary>
        public static int SlotCount
        {
            get { return _slots != null ? _slots.Length : 0; }
        }

        /// <summary>
        /// Найти первый пустой слот (1-based). 0 если нет свободных.
        /// </summary>
        public static int FindEmptySlot()
        {
            if (_slots == null) return 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Item == null)
                    return i + 1;
            }
            return 0;
        }

        /// <summary>
        /// Получить предмет по номеру слота (1-based). Null если слот пуст или не существует.
        /// </summary>
        public static Item GetItemBySlot(int slotNum)
        {
            if (_slots == null || slotNum < 1 || slotNum > _slots.Length) return null;
            return _slots[slotNum - 1].Item;
        }

        /// <summary>
        /// Локальный swap двух слотов (без отправки на сервер).
        /// Item.gameObject НЕ уничтожаются — они просто переходят к другому слоту.
        /// (Destroy'ить нельзя: тот же Item-объект используется как ссылка из EquipmentSlot,
        /// а ответ сервера сам пересоздаст все Item'ы через UpdateObject.)
        /// </summary>
        public static void LocalSwap(int fromSlot, int toSlot)
        {
            if (_slots == null) return;

            SlotScript from = _slots[fromSlot - 1];
            SlotScript to = _slots[toSlot - 1];

            Item fromItem = from.Item;
            Dictionary<string, string> fromComp = from.Components;

            Item toItem = to.Item;
            Dictionary<string, string> toComp = to.Components;

            _dirty = true;

            // detach обоих, чтобы Clear не сработал в Destroy и не убил Item, который мы переносим
            from.Detach();
            to.Detach();

            if (fromItem != null)
            {
                fromItem.SlotNum = toSlot;
                to.SetItem(fromItem, fromItem.Count, fromComp);
            }

            if (toItem != null)
            {
                toItem.SlotNum = fromSlot;
                from.SetItem(toItem, toItem.Count, toComp);
            }
        }

        /// <summary>
        /// Отвязать слот от Item без уничтожения Item.gameObject. Нужно когда предмет уезжает в курсор
        /// (swap-with-displaced) — обычный LocalDrop в этом случае уничтожает Item ещё до того как
        /// CursorController успеет взять его в TakeMoveable.
        /// </summary>
        public static void LocalDetach(int slotNum)
        {
            if (_slots == null) return;
            _slots[slotNum - 1].Detach();
            _dirty = true;
        }

        /// <summary>
        /// Локально положить предмет в слот (из руки, без swap)
        /// </summary>
        public static void LocalPlace(int toSlot, Item item)
        {
            if (_slots == null) return;
            SlotScript slot = _slots[toSlot - 1];
            item.SlotNum = toSlot;
            slot.SetItem(item, item.Count);
            _dirty = true;
        }

        /// <summary>
        /// Локально очистить слот (без отправки на сервер)
        /// </summary>
        public static void LocalDrop(int slotNum)
        {
            if (_slots == null) return;
            _slots[slotNum - 1].Clear();
            _dirty = true;
        }

        /// <summary>
        /// Отправить инвентарь если были локальные изменения (вызывать когда сессия перетаскивания завершена)
        /// </summary>
        public static void SendIfDirty()
        {
            if (_dirty)
                SendFullInventory();
        }

        /// <summary>
        /// Отправить полное состояние инвентаря на сервер
        /// </summary>
        public static void SendFullInventory()
        {
            if (_slots == null) return;

            InventoryResponse response = new InventoryResponse();
            response.inventory = SnapshotSlots(_slots);

            response.Send();
            _dirty = false;
        }
    }
}
