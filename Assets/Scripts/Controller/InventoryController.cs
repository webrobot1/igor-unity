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

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
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

                        if (slot.Value != null && !string.IsNullOrEmpty(slot.Value.id))
                        {
                            Item item = Instantiate(itemPrefab);
                            item.gameObject.SetActive(false);
                            item.SetData(slot.Value.id);
                            item.SetTooltip(tooltip);
                            item.SlotNum = slot.Key;
                            item.Count = slot.Value.count;

                            slotUI.SetItem(item, slot.Value.count, slot.Value.components);

                            _items[slot.Value.id] = item;

                            player.Log("Инвентарь слот " + slot.Key + ": " + slot.Value.id + " x" + slot.Value.count);
                        }
                        else
                        {
                            slotUI.Clear();
                        }
                    }
                }
            }

            return base.UpdateObject(map_id, key, recive, type);
        }

        // создает пустые ячейки инвентаря в количестве count
        private void InitializeSlots(int count)
        {
            foreach (Transform child in inventorySlotArea)
                Destroy(child.gameObject);

            _slots = new SlotScript[count];

            for (int i = 0; i < count; i++)
            {
                GameObject obj = Instantiate(slotPrefab, inventorySlotArea);
                obj.name = "Slot" + (i + 1);
                SlotScript slot = obj.GetComponent<SlotScript>();
                slot.SlotNum = i + 1;
                _slots[i] = slot;
            }
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
        /// Локальный swap двух слотов (без отправки на сервер)
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

            // swap
            if (fromItem != null)
            {
                fromItem.SlotNum = toSlot;
                to.SetItem(fromItem, fromItem.Count, fromComp);
            }
            else
                to.Clear();

            if (toItem != null)
            {
                toItem.SlotNum = fromSlot;
                from.SetItem(toItem, toItem.Count, toComp);
            }
            else
                from.Clear();
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

            for (int i = 0; i < _slots.Length; i++)
            {
                int slotNum = i + 1;
                SlotScript slot = _slots[i];

                response.inventory[slotNum] = slot.Item != null
                    ? new InventorySlotRecive(slot.Item.ItemId, slot.Item.Count, slot.Components)
                    : null;
            }

            response.Send();
            _dirty = false;
        }
    }
}
