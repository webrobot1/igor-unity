# План: интеграция lessons → release (18 этапов)

## Контекст

Источник: `Z:\var\www\html\lessons\` — 39 уроков по [Unity RPG Tutorial](https://www.youtube.com/playlist?list=PLX-uZVK_0K_6JEecbu3Y-nVnANJznCzix) (RPG_00..RPG_38).

Однако release — это MMO-клиент с серверной стороной (`Z:\var\www\html\game`), не singleplayer-RPG из туториала. Поэтому уроки служат **референсом UI/UX и потоков**, но не копируются дословно — данные приходят с сервера, а singleplayer-логика (save-load, локальные характеристики) заменена на сетевые пакеты.

### Что уже есть в release (не нужно переделывать)
- **Lesson #1** Player movement — есть.
- **Lesson #2** Health/Mana bars, attack — есть.
- **Lesson #3** Spells & targeting, ActionBar — есть ([ActionBar.cs](../Assets/Scripts/Classes/UI/ActionBar.cs), [Spell.cs](../Assets/Scripts/Classes/UI/Spell/Spell.cs)).
- **Lesson #4** Casting bar — есть.
- **Lesson #5** Enemies (HP bar, killing, unit frame) — есть.
- **Lesson #6–7** Worldmap, Tilemap, Camera — есть.
- **Lesson #8** Enemy AI states — серверная (`EnemyModel`).
- **Lesson #9** Keybinds — частично.
- **Lesson #10** Spellbook — есть (SpellBookController).
- **Lesson #11–12** Inventory, drag-and-drop — есть ([InventoryController.cs](../Assets/Scripts/Controller/InventoryController.cs), [SlotScript.cs](../Assets/Scripts/Classes/UI/Items/SlotScript.cs), [Item.cs](../Assets/Scripts/Classes/UI/Items/Item.cs), [MoveableObject.cs](../Assets/Scripts/Classes/UI/MoveableObject.cs), `CursorController`).
- **Lesson #13** Tooltip — есть ([Tooltip.cs](../Assets/Scripts/Classes/UI/Tooltip.cs)).
- **Lesson #20** Combat text — есть ([CombatText.cs](../Assets/Scripts/Classes/UI/CombatText.cs)).
- **Lesson #22** Saving — серверная, не нужна.
- **Lesson #29** Enemy pathfinding — серверная.

### Что НЕ сделано (берём в этапы ниже)
Lesson #15 Equipment UI, #16 Gear visuals, #17 Interactables/Chests, #14 Loot window, #18 Vendor, #19 Quests, #21 XP/Leveling, #23 Multi-loot/Tab-target, #24 Gathering, #25 Minimap, #26 Crafting, #28 Click-to-move, #30 Ranged enemy adjustments, #31 Talent tree, #32 Debuff indicator, #33–34 Blizzard/Chain lightning (специальные spell-эффекты), #35 Dialogue, #36–37 Stats window/Combat/Regen, #38 Cooldowns.

## Принципы

1. **От дешёвых к дорогим**: чистый UI на готовом сервере → UI + новый server-action → новая система (server + UI).
2. **Адаптация, не копирование**: каждый Unity-RPG-урок переносится в текущую архитектуру (Spriter+Universal animator, EntityModel, MMO-пакеты, серверный source-of-truth). Singleplayer-механики (SaveManager, локальный XP) — выбрасываем.
3. **«UI меньше — режем сервер»**: если в плане UI меньше slot-ов/полей/etc, чем шлёт сервер — сужаем сервер, а не раздуваем UI.
4. **Серверная разработка идёт параллельно** — части этапов уже имеют ready/in-progress серверные планы (см. ниже в каждом этапе).

## Этапы

### Этап 1 — Player Equipment UI (Lesson #15.0–15.4 + #16 ArmorType) ← **СЕЙЧАС** [MEDIUM]

Параллель с **ActionBar** (Lesson #3.4) и **Inventory** (Lesson #11): UI на готовых серверных данных, отправка через стандартный WS-event mechanism. Третий публичный event `ui/equip/index` добавляется по тому же паттерну что `ui/inventory/index` и `ui/actionbars/index`.

#### Зафиксировано: 8 слотов (Lesson #16 `ArmorType` + Character panel из Lesson #15)

В уроке `CharacterPanel.cs` ([Z:\var\www\html\lessons\RPG_16\RPG_16_3\RPG\Assets\Scripts\UIRelated\CharacterPanel.cs:13](Z:\var\www\html\lessons\RPG_16\RPG_16_3\RPG\Assets\Scripts\UIRelated\CharacterPanel.cs#L13)) ровно 8 `CharButton`: `head, shoulders, chest, hands, legs, feet, main, off`. Спрайты-плейсхолдеры лежат в [Z:\var\www\html\lessons\RPG_15\RPG_Part15_0\RPG\Assets\Sprites\Character panel\](Z:\var\www\html\lessons\RPG_15\RPG_Part15_0\RPG\Assets\Sprites\Character panel\) — берём оттуда `character_panel.png` (фон), `*_slot.png` (плейсхолдеры).

**Сопоставление: ячейка урока ↔ серверный slot-slug:**

| `ArmorType` (Lesson #16) | Спрайт-плейсхолдер (Lesson #15) | Серверный slug (`SigninRecive.equipment_slot`) |
|---|---|---|
| `Head` | `helmet_slot.png` | `head` |
| `Shoulders` | `shoulder_slot.png` | `shoulder` |
| `Chest` | `chest_slot.png` | `chest` |
| `Hands` | `gloves_slot.png` | `gloves` |
| `Legs` | `pants_slot.png` | `legs` |
| `Feet` | `boots_slot.png` | `feet` |
| `MainHand` | `staff_slot.png` | `hand_r` |
| `Offhand` | `orb_slot.png` | `hand_l` |

**Сервер ужимает `Game.equipmentSlot` в админке до этих 8 slug-ов** — всё что не в списке убирает чекбоксами после `shiny-enchanting-hamming.md`. UI рисует ячейки строго по `SigninRecive.equipment_slot`; если сервер пришлёт лишний slug — это нарушение контракта (падаем с `Error()`, см. CLAUDE.md).

UI-разметка окна (силуэт + 8 ячеек) — копируется из `RPG_16_3/RPG/Assets/Prefabs/UIPrefabs/CharacterPanel.prefab` (позиции `CharButton`-ов вокруг `character_panel.png`).

#### Точки входа клиента (конкретно по коду release)

**1. Список slug-ов слотов для рисования ячеек** — `SigninRecive.equipment_slot` ([Assets/Plugins/Mmogick/Client/Struct/Recive/SigninRecive.cs:43](../Assets/Plugins/Mmogick/Client/Struct/Recive/SigninRecive.cs#L43)).
- Приходит в `/auth` (HTTP) при логине, доставляется до клиента через `ConnectController` (там же где `idle_action`, `entity_actions` инжектятся в синглтоны).
- `EquipmentController.Awake()` — подписаться на готовность `SigninRecive` (по образцу `InventoryController.Awake` ([Assets/Scripts/Controller/InventoryController.cs:32](../Assets/Scripts/Controller/InventoryController.cs#L32)) → ждать `inventorySlotArea`, `slotPrefab`, `itemPrefab` валидности). Достать `SigninRecive.equipment_slot`, проверить ровно 8 slug-ов = ожидаемому набору; иначе `Error("получено N слотов экипировки, ожидалось 8")`.
- Инстанцировать 8 `EquipmentSlot` в фиксированный `equipmentSlotArea` префаба окна (позиции расставлены вручную в префабе, не через layout).

**2. Текущее состояние экипировки игрока** — компонент `equip` от сервера.
- Новое поле в [Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs](../Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs):
  ```csharp
  public Dictionary<string, int?>? equip = null;  // slug → inventory_idx; null = слот пуст
  ```
- Обрабатывается в `EquipmentController.UpdateObject(map_id, key, recive, type)` — override от `InventoryController`-pattern ([Assets/Scripts/Controller/InventoryController.cs:65](../Assets/Scripts/Controller/InventoryController.cs#L65)). Если `key == player_key && recive.components.equip != null`:
  - Для каждой пары `(slug, inv_idx)`:
    - `inv_idx == null` → `_equipSlots[slug].Clear()`.
    - `inv_idx != null` → достать item из `InventoryController.GetItemBySlot(inv_idx)` (уже есть static helper в [InventoryController.cs:150](../Assets/Scripts/Controller/InventoryController.cs#L150)) и установить иконку в `_equipSlots[slug]`. Не дублировать `Item`-объект — храним только sprite/count, потому что сам item остаётся в inventory (по контракту `inventory[N]` = тот же предмет, который "висит" в equip-slot).

**3. Allowed-slot для item-prefab** — `prefab.equipable_slot` для UX-подсказки (greying-out при drag).
- Источник: `AnimationCacheService._library[prefab].equipable_slot: List<string>` ([Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs:98](../Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs#L98)). Загружается в `/animation/patch/.../prefabs`.
- Доступ из UI: добавить статический геттер `AnimationCacheService.GetEquipableSlots(int gameId, string prefab) → List<string>` (рядом с уже существующим `GetObjectSlots` на [строке 657](../Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs#L657)). gameId доступен через `Player.MyInstance.GameId` / синглтон с авторизацией.

**4. Отправка изменений** — `EquipmentResponse` (новый), по образцу [InventoryResponse.cs:7-15](../Assets/Scripts/Struct/Response/InventoryResponse.cs):
   ```csharp
   public class EquipmentResponse : Response {
       public Dictionary<string, int?> items = new();   // slug → inv_idx; null = снять
       public override string group => "ui/equip";
   }
   ```
- Шлётся через тот же send-pipeline что `InventoryResponse` (метод `.Send()`, унаследованный от `Response`).
- Партиально — только изменённые слоты в `items`; если drop sword из inv[1] в hand_r, payload = `{items: {hand_r: 1}}`. Сервер каскадом обнулит `inventory[1]` и пришлёт обновлённые `equip` + `inventory` компоненты.

#### Тип компонента и точки вызова `EquipmentResponse.Send()`

| Действие | Что собирается в `items` |
|---|---|
| Drop item из инвентаря в `EquipmentSlot` | `{slotSlug: source.SlotNum}` |
| Drop экипированного предмета обратно в `SlotScript` инвентаря | `{slotSlug: null}` (сервер каскадом положит item в inventory) |
| Click-to-equip из инвентаря (Lesson #15.2) | `{first_matching_empty_slug: this.SlotNum}` (matching = `slug ∈ prefab.equipable_slot`) |
| Click-to-unequip (клик по предмету в `EquipmentSlot`) | `{slotSlug: null}` |

#### Что НЕ делаем (по контракту сервера)

- ❌ Не валидировать `slot/equipable_slot/inventory_idx` локально перед отправкой — сервер сам режет невалидное (UI допустимо подсветить greying-out для UX, но **отправку не блокировать**).
- ❌ Не различать «прямой апдейт» и «cascade от inventory» — обработка `equip`-component одинаковая (просто перерисовать слоты).
- ❌ Не отправлять `ui/equip/index` в ответ на серверный update компонента `equip` (петля) — слушать только локальный drag/click.
- ❌ Не делать render-overlay на скелете персонажа — это Этап 18 ([equipment-overlay.md](equipment-overlay.md)).
- ❌ Не реализовывать локальный кеш экипированных Item-объектов — `inventory[N]` остаётся source-of-truth для предмета, equip-slot держит только ссылку (slot → inv_idx).

#### Зависимые серверные планы

- `Z:\root\.claude\plans\shiny-enchanting-hamming.md` (in-progress) — `equipableSlot` в форме админки + валидация slug-а в `Prefab`-сущности. **Через эту форму ужимаем `Game.equipmentSlot` до 8 slug-ов выше.**
- `Z:\root\.claude\plans\sandbox-partitioned-hartmanis.md` (in-progress) — `Game.equipmentSlot` и `Prefab.equipableSlot` в sandbox-payload (нужно чтобы серверный триггер `equip`-компонента мог валидировать).
- Серверный public event `ui/equip/index` + триггер компонента `equip` со slotMap-cascade в `inventory.php` — формулировка контракта дана пользователем, отдельного плана пока нет.

#### Верификация

- `ws-command` → `ui/equip/index {items: {hand_r: 1}}` → в `ws-inbox` увидеть обновлённые компоненты `equip.hand_r=1` и `inventory[1]=null` (cascade).
- Unity Play Mode: drag sword из инвентаря в слот hand_r → иконка sword в hand_r, слот 1 инвентаря пуст; обратный drag → sword возвращается.
- Drag sword в неподходящий слот (head): локально подсветить красным, отправка идёт, сервер шлёт `Error()` → отключение клиента (это контракт — UI greying-out предотвращает в нормальном flow, но если игрок упорно тащит, сервер прав).
- Через MCP Playwright админки убедиться, что `Game.equipmentSlot` ужат до 8 slug-ов и `prefab.equipable_slot` у item'ов соответствует.

#### Файлы (создаются / правятся)

- `Assets/Scripts/Controller/EquipmentController.cs` *(новый, наследник от родителя `InventoryController` если удобно — общий `UpdateObject` pipeline; иначе самостоятельный класс)*
- `Assets/Scripts/Classes/UI/Items/EquipmentSlot.cs` *(новый, наследник `SlotScript`)*
- `Assets/Scripts/Struct/Response/EquipmentResponse.cs` *(новый)*
- `Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs` *(добавить поле `equip`)*
- `Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs` *(добавить статический геттер `GetEquipableSlots`)*
- `Assets/Resources/Prefabs/UI/CharacterPanel.prefab` *(новый, разметка скопирована из `Z:\var\www\html\lessons\RPG_16\RPG_16_3\RPG\Assets\Prefabs\UIPrefabs\CharacterPanel.prefab`, спрайты — из RPG_15)*
- `Assets/Resources/Sprites/UI/CharacterPanel/*.png` *(импортировать `character_panel.png`, `helmet_slot.png`, `shoulder_slot.png`, `chest_slot.png`, `gloves_slot.png`, `pants_slot.png`, `boots_slot.png`, `staff_slot.png`, `orb_slot.png` из урока)*
- `Assets/Scripts/Controller/InventoryController.cs` *(click-to-equip в `SlotScript.OnPointerClick` либо отдельный handler)*

---

### Этап 2 — Tooltip для item/equipment (Lesson #13, #15.3) [SMALL]

Расширение существующего `Tooltip.cs` для предметов: stat-ы, quality color (rarity), requirements. На текущем сервере item-stats частично есть в `prefab.component`, остальное — добавить серверный план.

**Сервер:** в yaml префабов `prefabs/item/*.yaml` добавить `quality` (poor/common/uncommon/rare/epic/legendary), `display_name`, `description`, `stack_max`, `bonus_stats`. Опц. справочник `ItemQuality` в `config.yaml` по аналогии с `EquipmentSlot`. Прокинуть в sandbox-payload.
**Клиент:** `Item.GetTooltipText()` — многострочный текст с display_name (цвет quality), описание, stack info; `Tooltip.cs` — поддержать rich text/color; расширить `InventorySlotRecive`/`PrefabsRecive` чтобы поля доходили до клиента.
**Зависимости:** —
**Верификация:** Unity Play Mode — hover слотов с яблоком и мечом, проверить tooltip с названием/цветом/описанием.
**Файлы:** `storage/php/igor/prefabs/item/*.yaml`, опц. `storage/php/igor/config.yaml`, `Assets/Scripts/Classes/UI/Items/Item.cs`, `Assets/Scripts/Classes/UI/Tooltip.cs`, `Assets/Scripts/Struct/Recive/InventorySlotRecive.cs`.

---

### Этап 3 — Cooldowns на ActionBar (Lesson #38) [SMALL]

Pie-fill overlay (`_cooldownOverlay`, `_cooldownText` уже зарезервированы в `ActionBar.cs:22-23`). Сервер: cooldown timestamp в action-state.

**Сервер:** новый компонент `cooldowns.yaml` (type=json map `spell_slug → unixtime_ready`, `max_compare_level=2`) на player. В `fight/bolt/index.php` после успешного выстрела записывать `cooldowns[spell] = time() + prefab.cooldown`, в начале выстрела — проверять. Поле `cooldown` (сек) в `prefabs/spell/*.yaml`.
**Клиент:** добавить `cooldowns` в `PlayerComponentsRecive`, в `Spell.cs`/`ActionBar.cs` использовать его вместо `event.timeout` (метод `GetCooldownProgress` уже есть, поменять источник).
**Зависимости:** —
**Верификация:** WebSocket — выстрелить firebolt, в `ws-inbox` увидеть `cooldowns: {firebolt: <unixtime>}`; в Unity overlay тикает корректно.
**Файлы:** `storage/php/igor/components/cooldowns.{yaml,php}`, `storage/php/igor/events/fight/bolt/index.php`, `storage/php/igor/prefabs/spell/*.yaml`, `Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs`, `Assets/Scripts/Classes/UI/Spell/Spell.cs`, `Assets/Scripts/Classes/UI/ActionBar.cs`.

---

### Этап 4 — Stats window (Lesson #36–37.0) [MEDIUM]

Окно характеристик персонажа. Сервер: stats уже в `player.component` — нужна агрегация (bonus_stats от экипа из Этапа 2) и UI.

**Сервер:** новый компонент `bonus_stats.yaml` (json, max_compare_level=2) на player; в `equip.php` после успешного апдейта собрать сумму `prefab.bonus_stats` экипированных предметов и записать в `bonus_stats`. Опц. event `ui/recalc_stats`. Поля `bonus_stats` в yaml префабов item.
**Клиент:** окно `Stats.prefab` (силуэт + список stat-ов), `StatsWindowController`, toggle по hotkey (например `P`). Добавить `bonus_stats` в `PlayerComponentsRecive`. Из этого же окна — переход к Этапу 1 (Equipment).
**Зависимости:** Этап 1 (Equipment UI), Этап 2 (item bonus_stats).
**Верификация:** `ws-command` — `ui/equip` с iron_sword в hand_r, прочитать `bonus_stats` через `ws-inbox`; в Unity stats-window должен показать прибавку.
**Файлы:** `storage/php/igor/components/bonus_stats.{yaml,php}`, `storage/php/igor/components/equip.php`, `storage/php/igor/prefabs/item/iron_sword.yaml`, `Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs`, `Assets/Scripts/Controller/StatsWindowController.cs`, `Assets/Resources/Prefabs/UI/Stats.prefab`.

---

### Этап 5 — Combat & Regen (Lesson #37.1–37.2) [SMALL]

Индикатор «в бою», regen out-of-combat. Сервер: combat-state у entity (добавить).

**Сервер:** компонент `in_combat.yaml` (bool/json `{end_at}`) на player. В `fight/melee` и `fight/bolt` устанавливать `in_combat.end_at = time() + 5`. В `regenerationhp`/`regenerationmp` — пропускать tick если `in_combat` активен (или применять reduced regen).
**Клиент:** UI-индикатор «in combat» (sword-icon около портрета), `TargetController` подсветка.
**Зависимости:** Этап 4 (общая работа с player-stat компонентами).
**Верификация:** атаковать моба — увидеть индикатор; не атаковать 5 сек — индикатор пропадает, regen ускоряется.
**Файлы:** `storage/php/igor/components/in_combat.{yaml,php}`, `storage/php/igor/events/fight/{melee,bolt}/index.php`, `storage/php/igor/events/regenerationhp/index.php`, `storage/php/igor/events/regenerationmp/index.php`, `Assets/Scripts/Controller/TargetController.cs`.

---

### Этап 6 — XP & Leveling UI (Lesson #21) [MEDIUM]

XP bar, level up effect, mob/quest XP indicator. Сервер: xp-changed packet.

**Сервер:** компоненты `xp.yaml`, `level.yaml`, `xp_to_next.yaml` (max_compare_level=2). В `status/dead/index.php` найти killer'а (поле data.from последнего hurt) и добавить xp; в триггере `xp.php` при xp>=xp_to_next вызывать каскад `ui/levelup` который инкрементит level, пересчитывает hp_max/mp_max, обнуляет xp. Поле `xp_reward` в prefab enemy.
**Клиент:** XP-bar UI (аналог hp), visual effect levelup. Добавить в `PlayerComponentsRecive` поля `xp`, `level`, `xp_to_next`.
**Зависимости:** Этап 3 (общий шаблон cascading state), Этап 4 (bonus_stats после уровня).
**Верификация:** убить скелета, увидеть `xp += 10`; повторить до levelup; overlay на клиенте.
**Файлы:** `storage/php/igor/components/{xp,level,xp_to_next}.{yaml,php}`, `storage/php/igor/events/status/dead/index.php`, `storage/php/igor/events/ui/levelup/index.{yaml,php}`, `storage/php/igor/prefabs/enemy/skeleton.yaml`, `Assets/Scripts/Struct/Recive/PlayerComponentsRecive.cs`, `Assets/Scripts/Controller/XPBarController.cs`.

---

### Этап 7 — Debuff/Buff indicator (Lesson #32.0) [MEDIUM]

UI отображение активных эффектов с их таймерами. Сервер: debuff-list в entity state.

**Сервер:** компонент `debuffs.yaml` (json массив `{prefab, end_at, source}`). Prefab kind=debuff (`ignite`, `permafrost`). При тике — self-event `status/dot/tick` через `event.add` с timeout (periodic damage). Применение через `status/collision/bolt` (см. Этап 14 — талант Ignite добавляет debuff после firebolt).
**Клиент:** UI-prefab `DebuffIcon.prefab`, контейнер `DebuffBar` над head-bar/портретом. Slot отображает иконку, оставшееся время. Серия overlay'ев через `TargetController`.
**Зависимости:** Этап 14 (debuff применяется только при изученном таланте).
**Верификация:** Кастнуть firebolt в скелета (Ignite talent изучен) — тикающий урон 3 раза с иконкой debuff.
**Файлы:** `storage/php/igor/components/debuffs.{yaml,php}`, `storage/php/igor/events/status/dot/tick/index.{yaml,php}`, `storage/php/igor/prefabs/debuff/*.yaml`, `storage/php/igor/events/status/collision/bolt.php`, `Assets/Scripts/Classes/UI/DebuffIcon.cs`, `Assets/Resources/Prefabs/UI/DebuffIcon.prefab`.

---

### Этап 8 — Minimap (Lesson #25) [SMALL]

Маленькая карта с POI и игроком. Использует существующий `MapDecodeModel`.

**Сервер:** ничего (map_id уже известен клиенту).
**Клиент:** `MinimapController` рендерит уменьшенную копию тайл-карты в правый верхний угол canvas. Маркеры: player (центр), other entities (точки по типу), POI (vendor/quest_giver — после Этапа 11/13). Toggle hotkey `N`.
**Зависимости:** —
**Верификация:** Play Mode — на сцене с известной картой видна миникарта с пятнышками мобов и игроком в центре.
**Файлы:** `Assets/Scripts/Controller/MinimapController.cs`, `Assets/Resources/Prefabs/UI/Minimap.prefab`.

---

### Этап 9 — Interactables / Chests (Lesson #17.1–17.3) [SMALL]

Клик-интеракшен с объектом, окно сундука со слотами. Сервер: open-action и chest-content.

**Сервер:** prefab `prefabs/object/chest.yaml` с компонентом `loot` (заполняется при размещении на карте), event `object/chest/open` (валидирует distance до игрока, рассылает loot клиенту).
**Клиент:** в `CursorController` при клике на entity с `kind=object` и `prefab.openable=true` — отправить `object/chest/open`, при ответе открыть `LootWindow` (см. Этап 10).
**Зависимости:** Этап 10 (общий LootWindow UI).
**Верификация:** разместить chest через `entity-save`, подойти, кликнуть, забрать.
**Файлы:** `storage/php/igor/prefabs/object/chest.yaml`, `storage/php/igor/events/object/chest/{open}/index.{yaml,php}`, `Assets/Scripts/Controller/CursorController.cs`, `Assets/Scripts/Controller/LootWindowController.cs`.

---

### Этап 10 — Loot Window (Lesson #14, #23.0 multi-loot) [MEDIUM]

Окно лута с npc, multi-loot. Сервер: loot-table и loot-pickup-action.

**Сервер:** в `status/dead/index.php` после смерти моба собрать `prefab.loot_table` (массив `{prefab, chance, count}`) и создать `prefabs/object/loot_bag.yaml`-сущность с компонентом `loot` на координатах трупа.
**Клиент:** при клике на ground-item / loot-bag открыть `LootWindow` (новый UI prefab). `LootButton` отправляет `item/pickup` для конкретного слота; «Loot all» — последовательно.
**Зависимости:** Этап 1 (Equipment UI готов — drop из лута в инвентарь через тот же `ui/inventory`).
**Верификация:** WebSocket — убить скелета, увидеть в `ws-inbox` новую entity prefab='loot_bag'; в Unity открыть и забрать iron_sword.
**Файлы:** `storage/php/igor/events/status/dead/index.php`, `storage/php/igor/components/loot.{yaml,php}`, `storage/php/igor/prefabs/enemy/skeleton.yaml` (loot_table), `storage/php/igor/prefabs/object/loot_bag.yaml`, `Assets/Scripts/Controller/LootWindowController.cs`, `Assets/Resources/Prefabs/UI/LootWindow.prefab`.

---

### Этап 11 — Vendor (Lesson #18) [MEDIUM]

Окно vendor с купить/продать, страницы, tooltip цены. Сервер: vendor-buy/sell action, vendor-stock.

**Сервер:** компонент `gold.yaml` (int, max_compare_level=2) на player. Prefab `prefabs/object/vendor.yaml` с компонентами `inventory` (продаваемые) и `vendor_pricing` (цена за prefab). Event `ui/vendor/buy` — списать gold, добавить item в player.inventory через тот же inventory.php. Event `ui/vendor/sell` — наоборот.
**Клиент:** `VendorWindowController` + `VendorSlot.cs` + `VendorResponse` (buy/sell). Открытие — клик на vendor-NPC (паттерн как chest).
**Зависимости:** Этап 10 (общий MoveableObject pattern), Этап 1 (drop в vendor = sell).
**Верификация:** подойти к vendor, открыть, купить apple — увидеть gold-=цена и apple в inventory.
**Файлы:** `storage/php/igor/components/gold.{yaml,php}`, `storage/php/igor/prefabs/object/vendor.yaml`, `storage/php/igor/events/ui/vendor/{buy,sell}/index.{yaml,php}`, `Assets/Scripts/Controller/VendorWindowController.cs`, `Assets/Resources/Prefabs/UI/VendorWindow.prefab`, `Assets/Scripts/Struct/Response/VendorResponse.cs`.

---

### Этап 12 — Quest UI / log (Lesson #19.0–19.3) [LARGE]

Quest log, выбор квеста, objectives. Сервер: quest-log и quest-state.

**Сервер:** prefab kind=quest (новый kind в `Game::kindList()`), компонент `quest_log.yaml` (json map quest_id → {state, objectives}). Event `ui/quest/accept`, `ui/quest/abandon`, `ui/quest/complete`. В `status/dead/index.php` для active kill-quest инкрементить objectives; в `item/pickup/index.php` — для collect-quest.
**Клиент:** `QuestLogController`, prefab `QuestLog.prefab` со списком квестов, описанием, objectives. Hotkey `L`.
**Зависимости:** Этап 6 (xp_reward по квесту).
**Верификация:** через ws-command принять quest "kill 3 skeletons", убить 3, увидеть progress и кнопку complete.
**Файлы:** `storage/php/igor/components/quest_log.{yaml,php}`, `storage/php/igor/prefabs/quest/*.yaml` (новый kind), `storage/php/igor/events/ui/quest/{accept,complete,abandon}/index.{yaml,php}`, `storage/php/igor/events/status/dead/index.php`, `storage/php/igor/events/item/pickup/index.php`, `Assets/Scripts/Controller/QuestLogController.cs`, `Assets/Resources/Prefabs/UI/QuestLog.prefab`.

---

### Этап 13 — Quest giver / completion (Lesson #19.4–19.15) [MEDIUM]

NPC с квестами, hand-in, indicator над npc, quest feed.

**Сервер:** prefab `prefabs/object/quest_giver.yaml` с компонентом `quests_offered` (массив quest_id). Event `ui/quest_giver/open` (фильтрует по player.quest_log какие можно принять/сдать), `ui/quest_giver/accept` (= `ui/quest/accept`), `ui/quest_giver/handin` (= complete + reward).
**Клиент:** `QuestGiverWindowController` (open по клику на NPC); `QuestIndicator.cs` на NPC-prefab (sprite `!`/`?`/dot читает `Player.quest_log`); `QuestFeedController` — overlay «Quest 'X': 2/3» при изменении objectives.
**Зависимости:** Этап 12.
**Верификация:** разместить quest_giver, кликнуть, увидеть список; принять, выполнить, сдать; на голове меняется `!` → `?` → пусто.
**Файлы:** `storage/php/igor/prefabs/object/quest_giver.yaml`, `storage/php/igor/events/ui/quest_giver/{open,accept,handin}/index.{yaml,php}`, `Assets/Scripts/Controller/QuestGiverWindowController.cs`, `Assets/Scripts/Classes/UI/QuestIndicator.cs`, `Assets/Scripts/Controller/QuestFeedController.cs`.

---

### Этап 14 — Talent tree UI (Lesson #31.0–31.3) [LARGE]

Дерево талантов, spend points, unlock.

**Сервер:** prefab kind=talent (как для quest в Этапе 12), компоненты `talents.yaml` (json map talent_id → points_spent), `talent_points.yaml` (int). Каждый talent prefab — `requires`, `max_rank`, `effect` (например `mp_cost_reduce: {firebolt: 1}`). Event `ui/talent/spend`. На levelup — `talent_points++`. В `fight/bolt/index.php` при расчёте mp_cost/cooldown учитывать talents.
**Клиент:** `TalentTreeController` + prefab `TalentTree.prefab` с деревом узлов; клик → `talent/spend`.
**Зависимости:** Этап 6 (levelup), Этап 3 (cooldowns — талант может менять cooldown).
**Верификация:** levelup, открыть talent tree, потратить очко в Improved Fireball, увидеть что firebolt mp_cost уменьшился.
**Файлы:** `storage/php/igor/components/{talents,talent_points}.{yaml,php}`, `storage/php/igor/prefabs/talent/*.yaml`, `storage/php/igor/events/ui/talent/spend/index.{yaml,php}`, `storage/php/igor/events/fight/bolt/index.php`, `Assets/Scripts/Controller/TalentTreeController.cs`.

---

### Этап 15 — Dialogue (Lesson #35) [MEDIUM]

Окно диалога с npc. Сервер: dialogue-tree и dialogue-choice-action.

**Сервер:** prefab `prefabs/object/dialogue_giver.yaml` с компонентом `dialogue` (json дерево `{node_id: {text, options:[{label, next, action}]}}`). Event `ui/dialogue/start`, `ui/dialogue/choose` (action может быть = accept quest / open vendor / give item).
**Клиент:** `DialogueWindowController` + prefab. Может быть переиспользован quest_giver/vendor для приветственного экрана.
**Зависимости:** Этап 13 (dialogue использует quest-flow через action="accept_quest").
**Верификация:** кликнуть на NPC, увидеть диалоговое окно, выбрать ответ, увидеть результат (новый квест/открытие vendor).
**Файлы:** `storage/php/igor/components/dialogue.{yaml,php}`, `storage/php/igor/prefabs/object/dialogue_giver.yaml`, `storage/php/igor/events/ui/dialogue/{start,choose}/index.{yaml,php}`, `Assets/Scripts/Controller/DialogueWindowController.cs`.

---

### Этап 16 — Crafting (Lesson #26) [LARGE]

Окно крафта, рецепты, материалы, craft-action.

**Сервер:** prefab kind=recipe, компонент `known_recipes.yaml` (json массив recipe_id) на player. Recipe-prefab — `ingredients: [{prefab, count}]`, `result: {prefab, count}`, `skill_required` (опц). Event `ui/craft/craft` (валидирует наличие ingredients в inventory через inventory.php, списывает, добавляет result).
**Клиент:** `CraftingWindowController` + prefab `Crafting.prefab` (список рецептов, требуемые материалы с счётчиком, кнопка Craft / Craft all). Hotkey `K`.
**Зависимости:** Этап 11 (vendor может продавать рецепты).
**Верификация:** иметь 1 iron_bar + 1 wood_handle в inventory, выбрать рецепт iron_sword, кликнуть Craft — материалы списались, sword добавился.
**Файлы:** `storage/php/igor/components/known_recipes.{yaml,php}`, `storage/php/igor/prefabs/recipe/*.yaml`, `storage/php/igor/events/ui/craft/craft/index.{yaml,php}`, `Assets/Scripts/Controller/CraftingWindowController.cs`, `Assets/Resources/Prefabs/UI/Crafting.prefab`.

---

### Этап 17 — Gathering / Click-to-move (Lesson #24, #28) [MEDIUM]

Сбор ресурсов (точка интереса с прогрессом), click-to-move с pathfinding.

**Сервер:** click-to-move — уже работает (walk/to + pathfinding library). Gathering: prefab `prefabs/object/gather_node.yaml` (rock/herb/tree) с компонентом `gather_loot` (loot-table) и `respawn_time`. Event `object/gather/start` — кастинг-каст (см. Этап 6, casting bar, серверный казал), по окончании — раздать loot и заспавнить self-destroy с timeout=respawn_time.
**Клиент:** при клике на gather-node — анимация «gathering», прогрессbar (тот же casting bar). По окончании — loot-bag через Этап 10.
**Зависимости:** Этап 10 (loot), опц. casting bar в Этапе 6.
**Верификация:** на карте есть iron_node, кликнуть, увидеть прогресс 3с, получить iron_ore в инвентарь, node исчезает.
**Файлы:** `storage/php/igor/prefabs/object/gather_node.yaml`, `storage/php/igor/components/gather_loot.{yaml,php}`, `storage/php/igor/events/object/gather/start/index.{yaml,php}`, `Assets/Scripts/Controller/CursorController.cs`.

---

### Этап 18 — Gear visuals / Render overlay (Lesson #16) — ОТЛОЖЕНО

Render-overlay item-prefab на скелете персонажа: подцепить prefab экипированного предмета к bone-anchor (`AnimationCacheService.GetObjectSlots` → `entity → slot → {objectId, offset, angle, scale}`).

Детальный план: [Plans/equipment-overlay.md](equipment-overlay.md) — серверная и транспортная часть готовы (`equipable_slot`, `object_slot`, sidecar `.slots.json`), нужен только клиентский render-overlay-контроллер. Зависит от **Этапа 1** (без UI экипировки нет точки входа).

---

## Уроки, не вошедшие в этапы

- **Lesson #22** Saving — не нужно (сервер).
- **Lesson #27** Bug fixing — точечные правки по обстоятельствам.
- **Lesson #30** Ranged enemy — серверная AI, не UI.
- **Lesson #33–34** Blizzard / Chain lightning — конкретные spell-эффекты, делать в рамках Этапа 7 (debuff) или отдельно по запросу.
