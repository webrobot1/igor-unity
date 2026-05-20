using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    // UI-ячейка экипировки. Хранит только ИНДЕКС инвентарного слота (inventory_idx), а не
    // ссылку на Item. По контракту сервера equip[slot] = inventory_idx (см. components/equip.php),
    // и source-of-truth — это InventoryController._slots. Иконка миррорится из
    // inventory_slots[_inventorySlotNum-1].Item каждый Update — по аналогии с ActionBar.
    //
    // Зачем ярлык, а не прямая ссылка на Item:
    //   - не виснем на destroyed-Item когда InventoryController пересоздаёт slot-Item'ы
    //     (Clear() в UpdateObject → Instantiate новый Item);
    //   - не нужно явно дублировать sprite/components — всё подтягивается из инвентаря на лету;
    //   - не нужно ловить рассинхрон порядка пакетов equip vs inventory.
    public class EquipmentSlot : SlotScript
    {
        // slug этого слота (head/chest/legs/...). EquipmentController инстанцирует ячейки динамически
        // по списку из ConnectController.equipment_slot и проставляет slug через SetSlotSlug.
        // SerializeField чтобы можно было задать в Inspector если ячейка лежит в prefab статически.
        [SerializeField]
        private string slotSlug;

        // Иконка слота — миррор от Item.Icon/Image. Используем унаследованный SlotScript._icon
        // (protected, тот же Inspector-binding из equipment-prefab'а).

        // 0 = пусто. Иначе индекс слота инвентаря (1-based).
        private int _inventorySlotNum;

        public string SlotSlug
        {
            get { return slotSlug; }
        }

        public void SetSlotSlug(string slug)
        {
            slotSlug = slug;
        }

        // Чтение Item через инвентарь — НЕТ собственного _item поля. Если slot не задан или
        // в инвентаре по этому индексу пусто/Destroy'ено — null.
        public override Item Item
        {
            get { return _inventorySlotNum > 0 ? InventoryController.GetItemBySlot(_inventorySlotNum) : null; }
        }

        // Установка ярлыка на конкретный slot инвентаря (или 0 = снять экипировку).
        // EquipmentController зовёт это вместо устаревшего SetItem(item).
        public void SetInventorySlotNum(int slotNum)
        {
            _inventorySlotNum = slotNum;
            UpdateIcon();
        }

        // Вызывается из EquipmentController.UpdateObject в full-clear ветке.
        // Никакого Destroy — Item живёт в инвентаре, мы лишь обнуляем ярлык.
        public override void Clear()
        {
            SetInventorySlotNum(0);
        }

        protected void Update()
        {
            // Миррор каждый кадр — Item.gameObject может быть пересоздан inventory'ем и нам нужен
            // свежий sprite. По cost'у это копирование одного sprite-ref'а, дёшево.
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            if (_icon == null) return;

            Item item = Item;
            if (item == null)
            {
                _icon.sprite = null;
                _icon.color = new Color(1, 1, 1, 0);
                _icon.enabled = false;
                return;
            }

            // Используем Item.Icon (видимая иконка с server-size scale), fallback на Image.
            Image src = item.Icon != null ? item.Icon : item.Image;
            _icon.sprite = src.sprite;
            _icon.color = Color.white;
            _icon.preserveAspect = true;
            _icon.enabled = _icon.sprite != null;
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
                    // Клиентская валидация: prefab.equipable_slot должен содержать этот slug.
                    // Иначе сервер throws Error и дисконнектит клиента — защищаем UX.
                    var allowed = AnimationCacheService.GetEquipableSlots(dragging.Prefab);
                    if (allowed == null || !allowed.Contains(slotSlug))
                        return;

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
