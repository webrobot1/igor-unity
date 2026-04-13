using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    public class Item : MoveableObject, IPointerClickHandler
    {
        private string _itemId;

        /// <summary>
        /// Номер слота инвентаря (1-based), в котором находится этот предмет
        /// </summary>
        public int SlotNum { get; set; }

        public string ItemId { get { return _itemId; } }

        public void SetData(string id)
        {
            _itemId = id;

            Sprite sprite = Resources.Load<Sprite>("Sprites/Items/" + id);
            if (sprite == null)
                sprite = Resources.Load<Sprite>("Sprites/Items/unknow");

            if (image != null && sprite != null)
                image.sprite = sprite;
        }

        public void SetTooltip(Tooltip t)
        {
            tooltip = t;
        }

        public override string GetTooltipText()
        {
            return _itemId;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            CursorController.TakeMoveable(this);
        }

        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            // Дроп на ActionBar — назначить предмет на быструю клавишу
            if (obj != null && obj.GetComponent<ActionBar>())
            {
                ActionBar bar = obj.GetComponent<ActionBar>();
                ActionBarsResponse response = new ActionBarsResponse();

                if (bar.Item != this)
                    response.actionbars.Add(bar.num, new ActionBarsRecive("item", _itemId));
                else
                    response.actionbars.Add(bar.num, null);

                response.Send();
            }
            // Дроп на слот инвентаря — swap слотов
            else if (obj != null && obj.GetComponentInParent<SlotScript>())
            {
                SlotScript targetSlot = obj.GetComponentInParent<SlotScript>();
                if (targetSlot.SlotNum != SlotNum)
                {
                    InventoryController.SendSwap(SlotNum, targetSlot.SlotNum);
                }
            }
            // Дроп в мир — выбросить предмет
            else if (obj == null)
            {
                InventoryResponse response = new InventoryResponse();
                response.action = "drop";
                response.slot = SlotNum;
                response.Send();
            }
        }
    }
}
