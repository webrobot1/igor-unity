using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    public class Item : MoveableObject, IPointerClickHandler
    {
        private string _prefab;

        /// <summary>
        /// Номер слота инвентаря (1-based). 0 = предмет в руке (не в слоте)
        /// </summary>
        public int SlotNum { get; set; }

        /// <summary>
        /// Количество в стеке (сохраняется при chain-swap)
        /// </summary>
        public int Count { get; set; }

        public string Prefab { get { return _prefab; } }

        public void SetData(string prefab)
        {
            _prefab = prefab;

            // Иконка из серверной library + server-size scale (см. MoveableObject.ApplyPrefabImage).
            ApplyPrefabImage(prefab);
        }

        public void SetTooltip(Tooltip t)
        {
            tooltip = t;
        }

        public override string GetTooltipText()
        {
            return _prefab;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            // Защита от двойного взятия (см. MoveableObject).
            if (CursorController.MyMoveable != null)
                return;
            CursorController.TakeMoveable(this);
        }

        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            // Дроп на ActionBar — назначить предмет на быструю клавишу
            if (obj != null && obj.GetComponent<ActionBar>())
            {
                // если предмет в руке (не в слоте) — сначала положить в первый свободный
                if (SlotNum == 0)
                {
                    int freeSlot = InventoryController.FindEmptySlot();
                    if (freeSlot > 0)
                        InventoryController.LocalPlace(freeSlot, this);
                }

                if (SlotNum > 0)
                {
                    ActionBar bar = obj.GetComponent<ActionBar>();
                    ActionBarsResponse response = new ActionBarsResponse();

                    if (bar.Item != this)
                        response.actionbars.Add(bar.num, new ActionBarsRecive("item", SlotNum.ToString()));
                    else
                        response.actionbars.Add(bar.num, null);

                    response.Send();
                }

                InventoryController.SendIfDirty();
            }
            // Дроп на equipment-слот: отправляем ui/equip/index, не двигаем item локально
            // (item остаётся в inventory[SlotNum], серверный cascade обновит equip-компонент).
            else if (obj != null && obj.GetComponentInParent<EquipmentSlot>())
            {
                EquipmentSlot equipSlot = obj.GetComponentInParent<EquipmentSlot>();
                if (SlotNum > 0)
                {
                    EquipmentResponse response = new EquipmentResponse();
                    response.items[equipSlot.SlotSlug] = SlotNum;
                    response.Send();
                }
            }
            // Дроп на слот инвентаря
            else if (obj != null && obj.GetComponentInParent<SlotScript>())
            {
                SlotScript targetSlot = obj.GetComponentInParent<SlotScript>();
                if (targetSlot.SlotNum != SlotNum)
                {
                    Item displaced = targetSlot.Item;
                    int originalSlot = SlotNum;

                    if (originalSlot > 0)
                        InventoryController.LocalSwap(originalSlot, targetSlot.SlotNum);
                    else
                        InventoryController.LocalPlace(targetSlot.SlotNum, this);

                    if (displaced != null)
                    {
                        if (originalSlot > 0)
                            InventoryController.LocalDrop(displaced.SlotNum);
                        displaced.SlotNum = 0;
                        CursorController.TakeMoveable(displaced);
                    }
                    else
                    {
                        InventoryController.SendIfDirty();
                    }
                }
            }
            // Дроп в мир — выбросить предмет
            else if (obj == null)
            {
                if (SlotNum > 0)
                    InventoryController.LocalDrop(SlotNum);
                InventoryController.SendIfDirty();
            }
        }
    }
}
