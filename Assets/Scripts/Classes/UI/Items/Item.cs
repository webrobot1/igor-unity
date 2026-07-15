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
            string title = AnimationCacheService.GetPrefabName(_prefab) ?? _prefab;
            string description = AnimationCacheService.GetPrefabDescription(_prefab);
            return string.IsNullOrEmpty(description) ? title : title + "\n" + description;
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
            // Предмет ОТКРЫТОГО КОНТЕЙНЕРА (лут трупа/сундука) распознаётся по метке LootSlotMarker
            // на слоте-родителе (SlotNum у него 0 — инвентарные ветки ниже не срабатывают).
            LootSlotMarker myLootSlot = GetComponentInParent<LootSlotMarker>();
            LootSlotMarker targetLootSlot = obj != null ? obj.GetComponentInParent<LootSlotMarker>() : null;

            // Дроп на слот окна контейнера
            if (targetLootSlot != null)
            {
                if (myLootSlot != null)
                    LootWindowController.SendReorder(myLootSlot.Num, targetLootSlot.Num);   // перестановка внутри контейнера
                else if (SlotNum > 0 && CursorController.SourceEquipmentSlot == null)
                    LootWindowController.SendPut(SlotNum, targetLootSlot.Num);              // свой предмет — в контейнер
                // экипированное сперва снимается в инвентарь — напрямую в контейнер не кладём
                return;
            }

            // Предмет контейнера дропнут в свой инвентарь — атомарный забор по позициям (сервер
            // переносит сам; локально не двигаем, ответ-пакет перерисует оба инвентаря)
            if (myLootSlot != null)
            {
                if (obj != null && obj.GetComponentInParent<SlotScript>() != null && obj.GetComponentInParent<EquipmentSlot>() == null)
                    LootWindowController.SendTake(myLootSlot.Num, obj.GetComponentInParent<SlotScript>().SlotNum);
                // в мир/на экипировку из чужого контейнера не выбрасываем — только через свой инвентарь
                return;
            }

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
                    // Клиентская валидация: prefab.equipable_slot должен содержать целевой slug.
                    // Иначе сервер throws Error и отключает клиента (контракт компонента equip).
                    // Это не локальная валидация в обход сервера, а UX-защита от disconnect'а.
                    var allowed = AnimationCacheService.GetEquipableSlots(Prefab);
                    if (allowed == null || !allowed.Contains(equipSlot.SlotSlug))
                        return;

                    EquipmentResponse response = new EquipmentResponse();
                    response.items[equipSlot.SlotSlug] = SlotNum;
                    response.Send();
                }
            }
            // Дроп на слот инвентаря
            else if (obj != null && obj.GetComponentInParent<SlotScript>())
            {
                SlotScript targetSlot = obj.GetComponentInParent<SlotScript>();

                // Если предмет взят из equip-slot — drop в любой инвентарный слот = unequip.
                // Отправляем явный ui/equip/index {slug: null}, и дальше идёт обычная логика swap/place,
                // если целевой slot отличается от текущего (чтобы можно было одновременно снять и переложить).
                if (CursorController.SourceEquipmentSlot != null)
                {
                    var slug = CursorController.SourceEquipmentSlot.SlotSlug;
                    var equipResponse = new EquipmentResponse();
                    equipResponse.items[slug] = null;
                    equipResponse.Send();
                }

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
                        // Detach (а не Drop) — displaced уезжает в курсор, его gameObject нужен живым.
                        // LocalDrop бы Destroy'ил его, и TakeMoveable получил бы destroyed Unity object.
                        if (originalSlot > 0)
                            InventoryController.LocalDetach(displaced.SlotNum);
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
