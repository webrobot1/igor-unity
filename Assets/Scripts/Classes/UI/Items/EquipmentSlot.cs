using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    // UI-ячейка экипировки. Расширение SlotScript с привязкой к slot-slug (head/chest/hand_r/...).
    // Хранит только иконку item-а — сам предмет остаётся в инвентаре (по контракту server-side
    // equip[slot] = inventory_idx, см. components/equip.php). Поэтому при drop на этот слот мы
    // не двигаем Item-объект, а только шлём EquipmentResponse и ждём cascade от сервера.
    public class EquipmentSlot : SlotScript
    {
        // slug этого слота (head/chest/legs/...). EquipmentController инстанцирует ячейки динамически
        // по списку из ConnectController.equipment_slot и проставляет slug через SetSlotSlug.
        // SerializeField чтобы можно было задать в Inspector если ячейка лежит в prefab статически.
        [SerializeField]
        private string slotSlug;

        public string SlotSlug
        {
            get { return slotSlug; }
        }

        public void SetSlotSlug(string slug)
        {
            slotSlug = slug;
        }

        // Click по equipment-слоту: либо drop курсорного item (надеть), либо unequip (снять).
        // Локальную валидацию equipable_slot НЕ делаем — сервер режет невалидное (см. CLAUDE.md).
        protected override void HandlePointerClick(PointerEventData eventData)
        {
            if (CursorController.MyMoveable != null && CursorController.MyMoveable is Item)
            {
                Item dragging = (Item)CursorController.MyMoveable;

                // Контракт ui/equip/index требует inventory_idx > 0 (item должен лежать в инвентаре).
                if (dragging.SlotNum > 0)
                {
                    EquipmentResponse response = new EquipmentResponse();
                    response.items[slotSlug] = dragging.SlotNum;
                    response.Send();

                    // Отпускаем курсор — серверный cascade пришлёт обновления equip/inventory.
                    CursorController.MyMoveable = null;
                }
            }
            else if (CursorController.MyMoveable == null && Item != null)
            {
                // Берём экипированный item в курсор для перетаскивания. Реальный unequip пойдёт
                // когда положат куда-то: drop в инвентарь → серверный cascade из inventory.php
                // обнулит equip.<slot>; drop в другой equip-slot → ui/equip/index с новым slug.
                CursorController.TakeMoveable(Item);
            }
        }
    }
}
