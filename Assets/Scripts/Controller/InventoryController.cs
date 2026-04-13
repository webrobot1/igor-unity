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

                        if (slot.Value != null)
                        {
                            Item item = Instantiate(itemPrefab);
                            item.gameObject.SetActive(false);
                            item.SetData(slot.Value.id);
                            item.SetTooltip(tooltip);
                            item.SlotNum = slot.Key;

                            slotUI.SetItem(item, slot.Value.count);

                            if(slot.Value.components != null)
                                slotUI.SetComponents(slot.Value.components);

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
        /// Отправить swap двух слотов на сервер (полный инвентарь с перестановкой)
        /// </summary>
        public static void SendSwap(int fromSlot, int toSlot)
        {
            if (_slots == null) return;

            InventoryResponse response = new InventoryResponse();
           
            response.inventory[toSlot] = response.inventory[fromSlot];
            response.inventory[fromSlot] = null;

            response.Send();
        }
    }
}
