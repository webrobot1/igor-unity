using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    // Контроллер UI экипировки игрока. Расширяет InventoryController в цепочке наследования
    // (SpellBook → Inventory → Equipment → ActionBars → Cursor → ...), так что UpdateObject
    // получает PlayerComponentsRecive с полями inventory + equip + actionbars в одном пакете.
    //
    // Контракт сервера (см. base/config.yaml equipment_slot и components/equip.php):
    //   - SigninRecive.equipment_slot: список slug-ов слотов разрешённых в игре.
    //   - CreatureComponentsRecive.equip: slot_slug → {idx, prefab}, null значение = слот пуст.
    //     Компонент ПУБЛИЧНЫЙ: приходит на любую видимую сущность, потому наложение предметов на скелет
    //     делается и для чужих игроков с мобами; idx (ячейка инвентаря) полезен только своему игроку —
    //     чужой инвентарь не приходит вовсе, внешний вид несёт prefab.
    //   - Отправка: EquipmentResponse {items: slot → idx; null = снять} → event "ui/equip/index".
    //
    // Контракт «inventory остаётся source-of-truth» реализован через ярлык-pattern:
    // EquipmentSlot хранит ТОЛЬКО inventory_idx (см. EquipmentSlot.SetInventorySlotNum), а sprite
    // миррорится из inventory_slots[idx].Item в EquipmentSlot.Update каждый кадр (по аналогии
    // с ActionBar). Это устраняет stale-ссылки на destroyed Item, когда InventoryController
    // пересоздаёт slot-Item'ы через Clear+Instantiate в UpdateObject.
    abstract public class EquipmentController : InventoryController
    {
        [Header("Для работы с UI экипировки")]

        // Контейнер с 8 заранее-расставленными EquipmentSlot (позиции head/chest/hand_r/... статично
        // в prefab'е окна инвентаря). Контроллер на Awake собирает их по детям и сверяет slug-и
        // с ConnectController.equipment_slot (приходит из /auth).
        [SerializeField]
        private Transform equipmentSlotArea;

        // static — мирорит паттерн InventoryController._slots, чтобы CursorController.TakeMoveable
        // мог из любого места включить подсветку совместимых слотов без поиска инстанса контроллера.
        private static Dictionary<string, EquipmentSlot> _equipSlots;

        // Сверку UI vs ConnectController.equipment_slot делаем не в Awake (он срабатывает при LoadSceneAsync
        // ДО того как SigninController.LoadMain установит equipment_slot), а на первом UpdateObject с компонентом equip.
        private bool _serverChecked;

        protected override void Awake()
        {
            base.Awake();

            if (equipmentSlotArea == null)
            {
                Error("не указан Transform контейнер для слотов экипировки");
                return;
            }

            // Собираем EquipmentSlot из детей контейнера и индексируем по slug. Дочерние GameObject'ы
            // должны иметь компонент EquipmentSlot с заполненным slotSlug в Inspector.
            _equipSlots = new Dictionary<string, EquipmentSlot>();
            foreach (Transform child in equipmentSlotArea)
            {
                EquipmentSlot slot = child.GetComponent<EquipmentSlot>();
                if (slot == null)
                    continue;

                if (string.IsNullOrEmpty(slot.SlotSlug))
                {
                    Error("EquipmentSlot " + child.name + " не имеет проставленного slotSlug");
                    return;
                }

                if (_equipSlots.ContainsKey(slot.SlotSlug))
                {
                    Error("Дубль slot_slug '" + slot.SlotSlug + "' в equipmentSlotArea");
                    return;
                }

                slot.SetTooltip(tooltip);
                _equipSlots[slot.SlotSlug] = slot;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            // base сначала: чтобы InventoryController обновил _slots ДО того как мы будем брать оттуда
            // иконки для экипировки через GetItemBySlot.
            GameObject ret = base.UpdateObject(map_id, key, recive);

            // Экипировка приходит на ЛЮБУЮ видимую сущность; разбор затенённых components — у них самих.
            Dictionary<string, EquipSlotRecive> equip = CreatureComponentsRecive.Of(recive)?.equip;

            // Контракт сервера (см. base/components/equip.yaml):
            //   equip == null         — поля equip нет в delta = no-op (экипировку не трогать);
            //   equip.Count == 0      — full-clear (снять всё);
            //   equip.Count > 0       — per-key delta (null значение = слот снят, объект = надетый предмет).
            if (equip == null)
                return ret;

            SyncEquipVisual(ret, equip);

            if (key == player_key && _equipSlots != null && !SyncEquipUI(equip))
                return null;

            return ret;
        }

        // Наложение надетых предметов на скелет носителя — своего игрока, чужого игрока, моба.
        // Рисуется, если у скелета носителя есть Spriter-точка-якорь этого слота (object_slot, type=point);
        // якоря резолвятся отложенно самим WeaponMount (структура носителя качается асинхронно).
        private static void SyncEquipVisual(GameObject entity, Dictionary<string, EquipSlotRecive> equip)
        {
            EntityModel wearer = entity != null ? entity.GetComponent<EntityModel>() : null;
            if (wearer == null)
                return;

            if (equip.Count == 0)
            {
                WeaponMount.DetachAll(wearer);
                return;
            }

            foreach (var pair in equip)
                WeaponMount.Sync(wearer, pair.Key, pair.Value != null ? pair.Value.prefab : null);
        }

        // Окно экипировки СВОЕГО игрока: ячейки держат ярлык на слот инвентаря (idx), спрайт миррорится
        // из inventory в EquipmentSlot.Update. false — расхождение с сервером, пакет дальше не обрабатываем.
        private bool SyncEquipUI(Dictionary<string, EquipSlotRecive> equip)
        {
            // Первая сверка UI vs server. Делаем здесь (а не в Awake), потому что Awake срабатывает
            // при LoadSceneAsync ДО того как SigninController.LoadMain установит equipment_slot.
            if (!_serverChecked)
            {
                if (ConnectController.equipment_slot == null)
                {
                    Error("ConnectController.equipment_slot не инициализирован к моменту первого equip-компонента");
                    return false;
                }
                foreach (var kv in ConnectController.equipment_slot)
                    if (!_equipSlots.ContainsKey(kv.Key))
                    {
                        Error("В UI экипировки нет слота для slug '" + kv.Key + "' (есть на сервере, нет в Equipment-prefab)");
                        return false;
                    }
                foreach (var slug in _equipSlots.Keys)
                    if (!ConnectController.equipment_slot.ContainsKey(slug))
                    {
                        Error("В UI экипировки есть слот '" + slug + "' которого нет в server equipment_slot");
                        return false;
                    }
                _serverChecked = true;
            }

            if (equip.Count == 0)
            {
                // full-clear: снимаем все слоты разом
                foreach (var slotUI in _equipSlots.Values)
                    slotUI.Clear();

                return true;
            }

            foreach (var pair in equip)
            {
                if (!_equipSlots.TryGetValue(pair.Key, out EquipmentSlot slotUI))
                {
                    Error("Сервер прислал equip для slot '" + pair.Key + "' которого нет в UI");
                    return false;
                }

                if (pair.Value != null)
                {
                    // Валидация: сервер не должен слать equip[slot]=idx если inventory[idx] пуст.
                    if (GetItemBySlot(pair.Value.idx) == null)
                    {
                        Error("equip[" + pair.Key + "] = " + pair.Value.idx + ", но в inventory этого слота нет item");
                        return false;
                    }

                    // Ярлык на inventory_idx — sprite миррорится в EquipmentSlot.Update.
                    slotUI.SetInventorySlotNum(pair.Value.idx);
                }
                else
                    slotUI.SetInventorySlotNum(0);
            }

            return true;
        }

        /// <summary>
        /// Надеть предмет инвентаря в слот slug. Единственная точка надевания: путей к нему два —
        /// клик по слоту экипировки (EquipmentSlot) и перетаскивание предмета на него (Item.Use), —
        /// а правило у обоих одно, и разъехавшись, они пускали бы к серверу разное.
        ///
        /// Гард equipable_slot ЗЕРКАЛИТ серверную валидацию: невалидный слот сервер считает
        /// контрактным нарушением и снимает игрока с карты, поэтому заведомо негодную команду не шлём.
        /// Контракт ui/equip/index требует inventory_idx > 0 — предмет обязан лежать в инвентаре
        /// (у вещи чужого контейнера SlotNum == 0, надеть её напрямую нельзя).
        ///
        /// false — команда НЕ ушла; вызывающему тогда нечего доделывать (курсор не отпускать).
        /// </summary>
        public static bool SendEquip(Item item, string slug)
        {
            if (item == null || item.SlotNum <= 0)
                return false;

            var allowed = AnimationCacheService.GetEquipableSlots(item.Prefab);
            if (allowed == null || !allowed.Contains(slug))
                return false;

            EquipmentResponse response = new EquipmentResponse();
            response.items[slug] = item.SlotNum;
            response.Send();

            return true;
        }

        // Подсветить equipment-слоты, в которые можно положить этот item (по prefab.equipable_slot).
        // Невалидные/несовместимые слоты гасятся (восстанавливают original-цвет рамки) — это позволяет
        // безопасно звать метод с любым Item при «перехвате» курсора через chain-swap, не накапливая
        // подсветку с предыдущего предмета.
        public static void HighlightForItem(Item item)
        {
            if (_equipSlots == null)
                return;

            var allowed = item != null ? AnimationCacheService.GetEquipableSlots(item.Prefab) : null;

            foreach (var kv in _equipSlots)
                kv.Value.SetHighlighted(allowed != null && allowed.Contains(kv.Key));
        }

        public static void ClearHighlight()
        {
            if (_equipSlots == null)
                return;

            foreach (var slotUI in _equipSlots.Values)
                slotUI.SetHighlighted(false);
        }
    }
}
