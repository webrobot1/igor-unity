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

                _equipSlots[slot.SlotSlug] = slot;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            // base сначала: чтобы InventoryController обновил _slots ДО того как мы будем брать оттуда
            // иконки для экипировки через GetItemBySlot.
            GameObject ret = base.UpdateObject(map_id, key, recive);

            if (key == player_key && ((PlayerRecive)recive).components != null && _equipSlots != null)
            {
                Dictionary<string, int?> equip = ((PlayerRecive)recive).components.equip;

                // Контракт сервера (см. base/components/equip.yaml):
                //   equip == null         — поля equip нет в delta = no-op (экипировку не трогать);
                //   equip.Count == 0      — full-clear (сервер прислал JSON `[]`, конвертер дал пустой Dictionary);
                //   equip.Count > 0       — per-key delta (null значение = unequip slot, int = equip slot).
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

                    if (equip.Count == 0)
                    {
                        // full-clear: снимаем все слоты разом
                        foreach (var slotUI in _equipSlots.Values)
                            slotUI.Clear();
                        foreach (var slug in _equipSlots.Keys)
                            SyncWeapon(slug, null);
                    }
                    else
                    {
                        foreach (var pair in equip)
                        {
                            if (!_equipSlots.TryGetValue(pair.Key, out EquipmentSlot slotUI))
                            {
                                Error("Сервер прислал equip для slot '" + pair.Key + "' которого нет в UI");
                                return null;
                            }

                            if (pair.Value.HasValue)
                            {
                                // Валидация: сервер не должен слать equip[slot]=idx если inventory[idx] пуст.
                                if (GetItemBySlot(pair.Value.Value) == null)
                                {
                                    Error("equip[" + pair.Key + "] = " + pair.Value.Value + ", но в inventory этого слота нет item");
                                    return null;
                                }

                                // Ярлык на inventory_idx — sprite миррорится в EquipmentSlot.Update.
                                slotUI.SetInventorySlotNum(pair.Value.Value);
                                SyncWeapon(pair.Key, pair.Value.Value);
                            }
                            else
                            {
                                slotUI.SetInventorySlotNum(0);
                                SyncWeapon(pair.Key, null);
                            }
                        }
                    }
                }
            }

            return ret;
        }

        // Наложение экипированного предмета на скелет игрока (Этап 1: оружие в руке).
        // slot — slug экипировки, invIdx — индекс инвентаря (null = снять). Рисуется только если
        // у скелета игрока есть Spriter-точка-якорь для этого слота (object_slot, type=point) и
        // предмет — статичная картинка (image-prefab). SCML-оружие/прочие случаи — позже.
        private void SyncWeapon(string slot, int? invIdx)
        {
            if (player == null) return;
            WeaponMount mount = player.GetComponent<WeaponMount>();

            if (!invIdx.HasValue)
            {
                if (mount != null) mount.Detach(slot);
                return;
            }

            Item item = GetItemBySlot(invIdx.Value);
            if (item == null) return;

            List<AnimationCacheService.ObjectSlotEntry> entries =
                AnimationCacheService.GetSlotEntries(BaseController.GAME_ID, player.prefab, slot);
            if (entries == null)
                return;   // нет якорей на скелете для этого слота

            List<AnimationCacheService.ImageVariant> variants =
                AnimationCacheService.GetPrefabImageVariants(item.Prefab);
            if (variants == null) return;   // не image-prefab — SCML-оружие вне Этапа 1

            // Спрайты всех вариантов из локального кеша (битый файл — пропуск с warning, не валим экип).
            // canonical — вариант с angle ближайшим к 0 (вправо): по нему ниже считается effectiveSize,
            // масштаб у всех вариантов общий (size предмета один на prefab).
            var sources = new List<WeaponMount.VariantSource>();
            Sprite canonical = null;
            int canonDist = int.MaxValue;
            foreach (AnimationCacheService.ImageVariant v in variants)
            {
                Sprite s;
                try { s = AnimationCacheService.TryGetSprite(BaseController.GAME_ID, v.File); }
                catch (System.Exception ex) { Debug.LogWarning("SyncWeapon " + item.Prefab + " вариант " + v.angle + "°: " + ex.Message); continue; }
                if (s == null) continue;
                sources.Add(new WeaponMount.VariantSource { angle = v.angle, sprite = s, pivotX = v.pivotX, pivotY = v.pivotY });
                int d = Mathf.Min(v.angle, 360 - v.angle);
                if (d < canonDist) { canonDist = d; canonical = s; }
            }
            if (sources.Count == 0) return;   // ни один вариант не загрузился

            // ЦЕЛЕВОЙ МИРОВОЙ масштаб предмета = ровно тот же, что у этого же prefab'а, выброшенного на землю
            // (UpdateController.ApplyVisualPrefab image-path): нормализация max(w,h) к 1/effectiveSize клетки,
            // где effectiveSize = server size, а при его отсутствии — tight-bounds max(w,h) самого спрайта.
            // Дублируем здесь ту же effectiveSize-логику (иначе при сброшенном size рука и земля разъезжались бы:
            // земля fallback'ит на tight-bounds, а старый код брал sizeFactor=1 → предмет native-размера).
            // bodyScale носителя компенсируется уже в WeaponMount.LateUpdate (overlay живёт под нормализованной
            // Metadata-веткой), чтобы рука совпадала с землёй на скелете любого размера.
            float? size = AnimationCacheService.GetPrefabSize(item.Prefab);
            float effectiveSize;
            if (size.HasValue && size.Value > 0.0001f)
                effectiveSize = size.Value;
            else if (AnimationCacheService.TryGetTightRect(canonical, out Rect tr) && Mathf.Max(tr.width, tr.height) > 0.0001f)
                effectiveSize = Mathf.Max(tr.width, tr.height);
            else
                effectiveSize = 1f;
            float groundScale = 1f / effectiveSize;

            // Все якоря слота (per-direction: своя кость на ракурс) — активный по кадру выбирает
            // WeaponMount. scale якоря композится с groundScale здесь (целевой мировой масштаб, см. выше);
            // z — draw-rank кожи кости якоря, на нём WeaponMount строит sortingOrder предмета.
            var anchors = new List<WeaponMount.Anchor>();
            foreach (AnimationCacheService.ObjectSlotEntry entry in entries)
            {
                if (entry == null || entry.anchor == null || entry.anchor.type != "point")
                    continue;   // якорь без точки (кость сервер подменяет на «<bone>_point» сам)
                anchors.Add(new WeaponMount.Anchor
                {
                    pointName = entry.anchor.name,
                    ox        = entry.offsetX,
                    oy        = entry.offsetY,
                    angle     = entry.angle,
                    scale     = entry.scale * groundScale,
                    z         = entry.z,
                });
            }
            if (anchors.Count == 0)
                return;   // нет точек-якорей на скелете для этого слота

            if (mount == null) mount = player.gameObject.AddComponent<WeaponMount>();
            mount.Apply(slot, anchors.ToArray(), sources.ToArray(),
                AnimationCacheService.GetPrefabRotationMode(item.Prefab));
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
