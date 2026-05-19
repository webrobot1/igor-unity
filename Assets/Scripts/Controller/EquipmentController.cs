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
    //   - PlayerComponentsRecive.equip: Dictionary<string, int?> = slot_slug → inventory_idx.
    //     null/отсутствие = слот пуст. inventory_idx ссылается на тот же предмет что и в inventory[idx].
    //   - Отправка: EquipmentResponse {items: slot → idx; null = снять} → event "ui/equip/index".
    //
    // Иконка экипированного item-а — копия из инвентаря (тот же sprite). НЕ создаём дублирующий
    // Item-объект — это нарушит контракт «inventory остаётся source-of-truth». EquipmentSlot.SetItem
    // принимает ссылку на тот же Item.
    abstract public class EquipmentController : InventoryController
    {
        [Header("Для работы с UI экипировки")]

        // Контейнер с 8 заранее-расставленными EquipmentSlot (позиции head/chest/hand_r/... статично
        // в prefab'е окна инвентаря). Контроллер на Awake собирает их по детям и сверяет slug-и
        // с ConnectController.equipment_slot (приходит из /auth).
        [SerializeField]
        private Transform equipmentSlotArea;

        private Dictionary<string, EquipmentSlot> _equipSlots;

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

                _equipSlots[slot.SlotSlug] = slot;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            // base сначала: чтобы InventoryController обновил _slots ДО того как мы будем брать оттуда
            // иконки для экипировки через GetItemBySlot.
            GameObject ret = base.UpdateObject(map_id, key, recive, type);

            if (key == player_key && ((PlayerRecive)recive).components != null && _equipSlots != null)
            {
                Dictionary<string, int?> equip = ((PlayerRecive)recive).components.equip;

                if (equip != null)
                {
                    // Первая сверка UI vs server. Делаем здесь (а не в Awake), потому что Awake срабатывает
                    // при LoadSceneAsync ДО того как SigninController.LoadMain установит equipment_slot.
                    if (!_serverChecked)
                    {
                        if (ConnectController.equipment_slot == null)
                        {
                            Error("ConnectController.equipment_slot не инициализирован к моменту первого equip-компонента");
                            return null;
                        }
                        foreach (var kv in ConnectController.equipment_slot)
                            if (!_equipSlots.ContainsKey(kv.Key))
                            {
                                Error("В UI экипировки нет слота для slug '" + kv.Key + "' (есть на сервере, нет в Equipment-prefab)");
                                return null;
                            }
                        foreach (var slug in _equipSlots.Keys)
                            if (!ConnectController.equipment_slot.ContainsKey(slug))
                            {
                                Error("В UI экипировки есть слот '" + slug + "' которого нет в server equipment_slot");
                                return null;
                            }
                        _serverChecked = true;
                    }

                    foreach (var pair in equip)
                    {
                        if (!_equipSlots.TryGetValue(pair.Key, out EquipmentSlot slotUI))
                        {
                            Error("Сервер прислал equip для slot '" + pair.Key + "' которого нет в UI");
                            return null;
                        }

                        slotUI.Clear();

                        if (pair.Value.HasValue)
                        {
                            Item item = GetItemBySlot(pair.Value.Value);
                            if (item == null)
                            {
                                // По контракту сервер не должен слать equip[slot]=idx если inventory[idx] пуст.
                                // Если такое пришло — рассинхрон, лог ошибки чтобы выловить серверный баг.
                                Error("equip[" + pair.Key + "] = " + pair.Value.Value + ", но в inventory этого слота нет item");
                                return null;
                            }

                            // Передаём в SlotScript.SetItem ссылку на тот же Item (не дублируем).
                            // count для экипировки всегда 1 (нельзя надеть стак); components прокидываем
                            // те же что у источника в inventory — но у нас нет прямого доступа к ним
                            // через GetItemBySlot. Для MVP передаём null — components на equip-слоте
                            // не используются (нет drop'а в actionbar и т.п. с эконо-слота).
                            slotUI.SetItem(item, 1, null);
                        }
                    }
                }
                else
                {
                    // equip = null → все слоты пустые
                    foreach (var slot in _equipSlots.Values)
                        slot.Clear();
                }
            }

            return ret;
        }
    }
}
