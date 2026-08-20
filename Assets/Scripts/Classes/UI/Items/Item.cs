using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Mmogick
{
    public class Item : MoveableObject
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

        /// <summary>
        /// Отличия ЭТОГО экземпляра от префаба (компоненты слота хранилища): своя цена и тому подобное.
        /// null — обычный предмет, все свойства префабные. Едут вместе с предметом, а не с ячейкой:
        /// предмет переезжает между слотами и курсором, а ячейка при этом остаётся на месте — из неё
        /// отличия терялись бы на первом же перекладывании и уходили на сервер пустыми (SnapshotSlots).
        /// Значение свойства спрашивать не отсюда, а у Values: здесь лежит только своя половина, и
        /// уезжает она обратно на сервер как есть (SnapshotSlots) — дообогащать её нельзя.
        /// </summary>
        public Dictionary<string, string> Components { get; set; }

        /// <summary>
        /// Свойства предмета целиком: slug компонента → значение этого экземпляра (своё, иначе префабное).
        /// Собирается один раз при разборе слота (AnimationCacheService.GetComponentValues), дальше предмет
        /// отвечает о себе сам — окнам не нужно знать, чем отличие экземпляра дополняется до полного набора.
        /// </summary>
        public Dictionary<string, JToken> Values { get; set; }

        /// <summary>
        /// Базовая цена предмета — сколько он стоит сам по себе, без торговца рядом: та же величина
        /// и в инвентаре, и в сундуке, и в лавке. Цену СДЕЛКИ (со множителем торговца) считает ценник
        /// лавки, не предмет. null — цены у предмета нет (вещь вне торговли).
        /// Монета — единица неделимая, поэтому дробная база показывается округлённой.
        /// </summary>
        public int? Price
        {
            get
            {
                if (Values == null || !Values.TryGetValue(TradeRecive.COMPONENT_PRICE, out JToken value) || value == null)
                    return null;

                int price = Mathf.RoundToInt(value.Value<float>());
                return price > 0 ? price : (int?)null;
            }
        }

        public string Prefab { get { return _prefab; } }

        public void SetData(string prefab)
        {
            _prefab = prefab;

            // Иконка из серверной library + server-size scale (см. MoveableObject.ApplyPrefabImage).
            ApplyPrefabImage(prefab);
        }

        /// <summary>
        /// Подсказка предмета: что это, что о нём известно и сколько он стоит. Цены тут две и они о разном:
        /// БАЗОВАЯ — свойство самого предмета, одинаковое и в инвентаре, и в сундуке, и в лавке; строка
        /// скупки — предложение конкретного торговца, и появляется она только пока его лавка открыта.
        /// Роли строк размечает <see cref="TextStyle"/> — предмет их только называет.
        /// </summary>
        public override string GetTooltipText()
        {
            string title = AnimationCacheService.GetPrefabName(_prefab) ?? _prefab;
            string description = AnimationCacheService.GetPrefabDescription(_prefab);
            int? price = Price;

            string text = TextStyle.Title(title);

            if (!string.IsNullOrEmpty(description))
                text += "\n" + TextStyle.Hint(description);

            if (price != null)
                text += "\n" + TextStyle.Value("Цена: " + InventoryController.Coins(price.Value));

            // Сколько за него дадут ЗДЕСЬ — величина торговца, а не предмета, и стоит отдельной строкой
            // под базовой ценой: стак из одной штуки продаётся сразу, без вопроса о количестве, и другого
            // места узнать цену у игрока нет.
            string trade = LootWindowController.TradeHint(this);

            if (trade != null)
                text += "\n" + TextStyle.Value(trade);

            return text;
        }

        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            // Предмет ОТКРЫТОЙ ДОБЫЧИ трупа распознаётся по метке LootSlotMarker на слоте-родителе
            // (SlotNum у него 0 — инвентарные ветки ниже не срабатывают).
            LootSlotMarker myLootSlot = GetComponentInParent<LootSlotMarker>();
            LootSlotMarker targetLootSlot = obj != null ? obj.GetComponentInParent<LootSlotMarker>() : null;
            LootWindowMarker targetLootWindow = obj != null ? obj.GetComponentInParent<LootWindowMarker>() : null;

            // Дроп в окно добычи: попал в ячейку — кладём в неё, промахнулся мимо ячеек (отступы сетки,
            // заголовок, фон), но попал в окно — позицию подбирает сервер. Намерение у обоих попаданий
            // одно и то же, и терять его из-за нескольких пикселей промаха незачем: у лавки это читалось
            // бы отказом от продажи, хотя игрок нёс вещь именно торговцу.
            if (targetLootSlot != null || targetLootWindow != null)
            {
                // Внутри добычи предметы не переставляются: у ui/inventory/index нет адресации чужого
                // контейнера, писать в добычу может только серверный код механик.
                if (myLootSlot == null && SlotNum > 0 && CursorController.SourceEquipmentSlot == null)
                    LootWindowController.SendPut(SlotNum, targetLootSlot != null ? targetLootSlot.Num : (int?)null);
                // экипированное сперва снимается в инвентарь — напрямую в добычу не кладём
                return;
            }

            // Предмет добычи дропнут в свой инвентарь — атомарный забор по позициям (сервер переносит
            // сам; локально не двигаем, ответ-дельта перерисует и инвентарь, и окно добычи).
            // idx — список даже для одного предмета: контракт ui/inventory/take.
            if (myLootSlot != null)
            {
                if (obj != null && obj.GetComponentInParent<SlotScript>() != null && obj.GetComponentInParent<EquipmentSlot>() == null)
                    LootWindowController.SendTake(
                        new List<int> { myLootSlot.Num },
                        obj.GetComponentInParent<SlotScript>().SlotNum);
                // в мир/на экипировку из чужой добычи не выбрасываем — только через свой инвентарь
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
                // Стак уходит на землю столько, сколько назвал игрок: выброшенного не вернуть, и
                // терять весь запас из-за одного движения он не должен. Одна единица вопроса не стоит.
                if (SlotNum > 0 && Count > 1)
                {
                    int slot = SlotNum;
                    string title = "Выбросить: " + (AnimationCacheService.GetPrefabName(Prefab) ?? Prefab);

                    QuantityPromptController.Ask(title, Count, count =>
                    {
                        InventoryController.LocalDropCount(slot, count);
                        InventoryController.SendIfDirty();
                    });
                    return;
                }

                if (SlotNum > 0)
                    InventoryController.LocalDrop(SlotNum);
                InventoryController.SendIfDirty();
            }
        }
    }
}
