# Проект (Unity 6000.4.0f1)

**Все durable-знания о клиенте — только в этот файл.** Репозиторий самостоятелен: дорабатывается и без конкретного сервера, внешние корпусы (например, серверный проект) ссылаются сюда — обратных ссылок на их пути здесь не держать. Других носителей знаний в репо не заводить: `.claude/skills` здесь — авто-генерённые tool-доки плагина, README — окружение и настройки Unity-проекта, не корпус. Дубли (внутри репо и с внешними корпусами) запрещены — устаревают молча.

Общение вести по-русски.

## Сцены

- **RegisterScene** — авторизация. Открыта по умолчанию в редакторе. После изменений в MainScene — вернуть активную сцену на RegisterScene. Форма входа: `UI/login`, `UI/password`, `UI/server` и две кнопки `UI/Button` (одна «Войти», вторая «Зарегистрироваться»).
- **MainScene** — игровая, загружается после авторизации. Все игровые объекты (UI, Map, World) здесь.

## Вход в игру (Play Mode → авторизация)

1. Полный рестарт ВСЕГДА, даже если Play Mode уже запущен: `editor-application-set-state(isPlaying: false)` → `assets-refresh` (пересборка правок C#) → `editor-application-set-state(isPlaying: true)`. Уже-запущенный Play Mode держит устаревший код/зависший state, WS-сессия могла истечь по таймауту — «доигрывание» в чужом состоянии даёт ложные исходы.
2. Нажать кнопку «Войти» через `script-execute`. Креды сериализованы в RegisterScene — поля формы скриптом НЕ перезаписывать, только Invoke кнопки «Войти». Форма доступна сразу после загрузки сцены — фиксированная пауза перед поиском кнопки не нужна:
```csharp
using UnityEngine;
using UnityEngine.UI;
public class Script {
    public static object Main() {
        var buttons = GameObject.FindObjectsOfType<Button>(true);
        foreach (var btn in buttons) {
            var text = btn.GetComponentInChildren<UnityEngine.UI.Text>();
            if (text != null && text.text == "Войти") { btn.onClick.Invoke(); return "Clicked"; }
        }
        return "Button not found";
    }
}
```
3. После клика ждать смену сцены на MainScene поллингом `scene-list-opened` (макс 30с): асинхронную загрузку дождаться явно, по таймауту — fail-fast, не оптимистичное «наверное загрузилось».
4. **После сбора данных** — остановить Play Mode СРАЗУ: `editor-application-set-state(isPlaying: false)`. Безусловно: закончил свои Unity-действия → стоп немедленно; «пользователь сейчас смотрит/взаимодействует с игрой» — НЕ основание оставлять (висящий Play Mode потребляет ресурсы машины), оставить можно только по явной просьбе пользователя. Триггер срыва: работа завершена, Play Mode остался висеть — пользователь: «не вышел из Play Mode, он потребляет ресурсы».
5. **Скриншоты** — только `screenshot-game-view`, не `screenshot-scene-view`. Перед скриншотом проверять `editor-application-get-state` → `isPlaying: true`. Скриншот из Edit Mode или Scene View бесполезен для верификации UI.

## Диагностика игровых объектов (enemy, HP-полоска и т.п.)

**Проверки в Unity делать самому через MCP `ai-game-developer` — НЕ спрашивать разрешения и НЕ просить пользователя проверить вручную.** Если нужно посмотреть состояние игры/объектов/UI, запустить playmode, сделать скриншот (`screenshot-game-view`), выполнить `script-execute`, `gameobject-find`, `scene-get-data` и т.п. — выполнять самостоятельно. Аналогично для серверного контента есть MCP `mmogick-websocket` — тоже работать самому.

Игровые объекты (`enemy`, карта, HP-полоски, Spriter-анимации) существуют только после входа в учётку — флоу в «Вход в игру».

**Если на текущей карте игрока пусто (только сам player)** — переместить персонажа на карту с сущностями (обычно `desert` или аналогичная): MCP-тул `entity-save` (`type=player`; поля `map` — ID карты, `x`, `y` — в схеме). Данные player менять ТОЛЬКО пока он ВНЕ игры: живое состояние онлайн-игрока сервер периодически сохраняет в БД — перетрёт правку. Порядок ОБЯЗАТЕЛЬНЫЙ: выйти из игры (остановить Play Mode) → `entity-save` → зайти — значение применяется при заходе (вход читает позицию из БД).

Анализ производительности кода — по timestamps между Debug.Log() определять скорость между блоками.

Код сервера — `Z:\var\www\html\game`.

## C# правила

- `FindFirstObjectByType<T>()`, не `FindObjectOfType<T>()`. Singleton ищет объект на сцене, не создаёт.
- Статические UI-панели — на сцене через Inspector. Динамическое создание — только повторяющиеся элементы (слоты, списки, боевой текст). `World` и `Map` — контейнеры: дочерние объекты удаляются в `Awake()` и создаются из JSON сервера.
- UI-префабы (боевой текст, слоты, элементы списков) — создавать через Inspector/MCP как `.prefab` в `Resources/Prefabs/`, не программно в коде. `[SerializeField]` + проверка в `Awake()`.
- Данные с сервера — `*Recive` (`Struct/Recive/`), отправка — `*Response` (`Struct/Response/`). Именно `Recive`, не `Receive`.
- Ссылки на UI/префабы — `[SerializeField]` + Inspector, не `Find`/`GetComponent` (кроме singleton).
- Анимации управляются сервером через `action`, клиент переключает слой Animator. Клиентские триггеры не добавлять.
- Имена полей и типы C# — **как приходят с сервера**. Slug компонентов, событий, ключи JSON совпадают с серверными. Несовпадения → сверять с серверным `storage/game/`.
- Модели и префабы (`AnimalModel`, `EnemyModel`, `PlayerModel`, `ObjectModel`) существуют по серверным kind. Реакции на `event.name`, `eventGroup.name`, `component.name` и `entity.action` (анимации) прописаны в контроллерах и моделях; кастомные пакеты вне world (настройки игры, книга заклинаний) — по `event.code`/`component.code`. У другой игры состав иной → менять клиент.
- UI-объекты (Tooltip, CombatTextManager и т.д.) — `[SerializeField]` на контроллере, проверка в `Awake()`, доступ через `MainController.Instance`. Не использовать `FindFirstObjectByType` singleton. `ConnectController.Error()` если не назначен.
- **Model — данные, Controller — UI**: модели (`EnemyModel`, `ObjectModel`) не обращаются к UI-объектам и контроллерам. Все взаимодействия с UI (боевой текст, тултипы, фреймы) — в контроллерах (`UpdateObject`, `HandleData`).
- Текст в мировых координатах (боевой текст, имена) — через World Space Canvas + UI Text, не TextMesh. Sorting через `Canvas.sortingOrder`. TextMesh (MeshRenderer) не совместим с 2D sorting pipeline.
- Контроллеры — линейная цепочка наследования; новый UI = новый контроллер, встраиваемый в цепочку; актуальный порядок — по наследованию классов в `Assets/Scripts/Controller/`. Каждый контроллер владеет своими `[SerializeField]` UI-объектами.
- Newtonsoft «Cannot deserialize JSON array into Dictionary» — серверный дефект (PHP-словарь ушёл JSON-массивом), чинить на сервере. Конвертер-обход на клиенте не добавлять.
- `JsonConvert.DeserializeObject<T>` серверного payload — обязательно с `new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }`. Сервер шлёт скаляры всегда, включая `null` (канон серверной сериализации: null ≡ дефолт поля). Без `Ignore` Newtonsoft пишет `null` в не-nullable C#-поле и падает. Проверять при каждом новом клиентском десериализаторе серверного payload.
- Валидация команд двухслойная: клиент — UX-фильтр, заведомо невалидную команду не отправляет (сервер наказывает игрока Error + дисконнектом); сервер — истина, всё дошедшее невалидное режет жёстко, покрывая обход клиента. Ни снимать клиентский фильтр («сервер разберётся»), ни смягчать серверное наказание ради клиентских багов. Образец фильтра — equip-ветка `EquipmentSlot.HandlePointerClick`.

## Образец UI в MainScene (без входа в игру)

На `Assets/Scenes/MainScene.unity` лежит **образец всех UI-элементов** игры — выложен в иерархии в выключенном виде (или с фиктивными данными). Можно открывать сцену **без playmode** и инспектировать прямо в Editor: настройки RectTransform, Anchors, Pivot, Content Size Fitter, Vertical/HorizontalLayoutGroup и т.п.

При любой задаче «что-то не так с UI» (размер, позиция, отступы, anchors, layout) — **сначала смотреть образец в MainScene**, потом править. Не делать догадок про значения по умолчанию — открыть, посмотреть конкретный объект, использовать `gameobject-find` / `gameobject-component-get` по нужному UI-элементу.

## DebugGrid

Каждая карта содержит выключенный Tilemap-слой `Map/<id>/DebugGrid` (создаётся в [MapDecodeModel.cs](Assets/Plugins/Mmogick/Tiled2Unity/MapDecodeModel.cs), визуализирует границы тайловых клеток для проверки позиционирования/выравнивания). Включить через `script-execute` → `SetActive(true)` — видно в Scene и Game View.

## Админка (dev-креды)
- URL: http://localhost/admin/
- Login: `admin@my-fantasy.ru`
- Пароль: `123456`
- Для автоматической проверки страниц использовать MCP Playwright.

Приходящие пакеты логируются на клиенте; на сервере есть свой лог.
Изменяемые настройки Unity-проекта — описывать в README (какие и зачем).

## Архитектура анимаций: Spriter (per-prefab) + Unity-Animator (универсальные эффекты)

Механизм подробно задокументирован XML-doc'ами в коде — здесь только обзор, карта файлов и cross-file инварианты (их ни один отдельный файл не покрывает). За деталями — в код:
- `EntityModel.PlayAction` (единая точка: Spriter-приоритет → Universal-fallback), `EnsureUniversalAnimator`/`OnAnimatorAttached` (привязка overlay-Animator'а, startDisabled для image-init), rotation projectile'ов в `SetData` — [EntityModel.cs](Assets/Plugins/Mmogick/Client/Model/EntityModel.cs).
- семантика полей prefab'а (size/sha256/equipable_slot…), throw vs null политика кеша — [AnimationCacheService.cs](Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs).

Два слоя на одном GameObject:
- **Per-prefab Spriter (SCML с сервера)** — индивидуальные `idle/walk/attack/hurt/dead`. Компонент `SpriterDotNetBehaviour`, кеш — `AnimationCacheService`.
- **Универсальные эффекты в Unity-Animator** — декораторы поверх любой сущности (сейчас `remove`=Puff, `dead`=силуэты тела). Файлы: [Universal.controller](Assets/Resources/Animations/Universal.controller), [Universal/Remove/*.anim](Assets/Animations/Universal/Remove/) + [Universal/Dead/*.anim](Assets/Animations/Universal/Dead/), кадры — [Sprites/Entitys/Remove/Puff*.png](Assets/Sprites/Entitys/Remove/) и [Sprites/Entitys/Dead/dead*.png](Assets/Sprites/Entitys/Dead/). Спрайты эффектов кладутся в `Sprites/Entitys/<ActionName>/` симметрично .anim-файлам.

**Структура Universal.controller** (это asset — описание только здесь, в коде нет): 1 слой, параметры `direction` (Int 0..3: 0=down, 1=left, 2=right, 3=up) и `remove` (Trigger). AnyState→`remove_{down,left,right,up}` по `remove If 0` + `direction Equals N`. Возврат в `Idle` (motion=null) по `hasExitTime=true, exitTime=1`.

**Инварианты — чего НЕ делать:**
- Не выключать `writeDefaults=false` в state'ах Universal.controller — иначе при transition в Idle Animator сбрасывает `SpriteRenderer.m_Sprite` в default и сущность мелькает «пустым» спрайтом перед уничтожением.
- Не удалять [Sprites/Entitys/Remove/Puff*.png](Assets/Sprites/Entitys/Remove/) и [Sprites/Entitys/Dead/dead*.png](Assets/Sprites/Entitys/Dead/) — Universal/{Remove,Dead}/*.anim ссылается на них по GUID.
- Не возвращать `DestroyImmediate(Animator)` в Spriter/image-init — это убьёт Universal-overlay.
- Расширяя Universal-fallback на НЕ-удаляющие action'ы (hurt-flash и т.п.) — дописать восстановление SR детей Spriter'а после эффекта: сейчас они выключаются безвозвратно (после remove GameObject всё равно уничтожается).

## Логирование

Куда писать НОВЫЕ логи (механизм флага — в XML-doc на `EntityModel.verbose`):
- может спамить каждый кадр (FixedUpdate, клик, тайминги событий) → через `verbose`/`player.Log()`. `false` по умолчанию, включать вручную в рантайме (`EntityModel.verbose = true`) только при отладке — иначе консоль забивается на 100 сообщений/сек.
- событийный (приём/отправка пакетов, раз в N) → через `#if UNITY_EDITOR`: в редакторе выводится всегда, не отключается.

## Политика по коллизиям/нарушениям контракта

При коллизиях (race condition, state-invariant нарушен, метод вызван до готовности зависимости, null там где по контракту не должно быть null и т.п.) — **выводить ошибку, а не глушить и не ставить заплатки**:
- Бросать exception (через `throw` или через `Error()/Errro()` — они в следующем кадре безопасно отсоединяют игрока и показывают UI-ошибку, не крашат программу; после вызова — `return`, чтобы поток вернулся в игровой цикл FixedUpdate).
- Не возвращать молча `null`/`default`/пустые коллекции.
- Не добавлять «защитные» вызовы типа `EnsureLoaded` на горячем пути только чтобы скрыть симптом — это маскирует timing-баги и делает debug невозможным.
- Если контракт гарантирует инвариант (например, «GetPrefabSize вызывается только после SyncAll»), нарушение — баг вызывающей стороны; он должен падать громко, чтобы его починили по месту, а не в месте-потребителе.

### Дефолт vs throw: когда возврат null/false/0/пустого — норма, а когда маскировка

«Не возвращать молча дефолт» — НЕ значит «никогда не возвращать дефолт». Дефолт бывает законным ответом. Различать по смыслу, который значение несёт для вызывающего. У любого метода-аксессора возможны два разных «пустых» исхода:

- **«Легитимное отсутствие»** — спроектированный ответ, на который у вызывающего есть корректная реакция: «записи нет в справочнике», «у action нет спец-клипа → fallback», «точка не активна в этом кадре». Это нормальный контракт — **дефолт оставляем**.
- **«Зависимость не готова»** — кеш ещё не загружен, init не отработал, метод вызван до своей предпосылки. Это timing-баг — **бросаем exception**.

**Главное правило:** если одно и то же возвращаемое значение означает И «легитимное отсутствие», И «зависимость не готова» — это и есть маскировка; разведи их: throw на «не готова», дефолт оставь только на «отсутствие».

Признаки, что дефолт — маскировка (→ throw):
- значение приходит из ветки по полю-кешу (`if (cache == null)`, `cache?.`, `cache ?? default`), которое заполняется один раз при логине/инициализации;
- у вызывающего нет отдельной реакции на «не готов» — он трактует дефолт как «отсутствие» и едет дальше (сущность без визуала, флаг тихо false и т.п.);
- предпосылка гарантирована порядком инициализации (напр. SyncAll до Connect, LoadMain до спавна) → дефолт-от-«не готов» в норме недостижим, а значит его появление — баг, который обязан падать.

Признаки, что дефолт легитимен (→ оставить):
- это Try-семантика поиска (null = «не найдено»), и «не найдено» — нормальное, обрабатываемое состояние;
- состояние «не готов» отдельно невозможно (валидируется у источника на входе) ЛИБО у вызывающего есть корректный fallback именно на этот дефолт, а не просто «поехали дальше».

Если по этим признакам непонятно — не угадывай дефолтом: проверь вызывающие места (что они делают с дефолтом) и порядок инициализации. Эталоны в `AnimationCacheService`: `GetPrefabSize`/`HasPrefab`/`GetPrefabImage`/`GetPrefabs` — throw на незагруженном кеше; `GetClipName`/`GetClipNameSimple` — оставлены с null (null доминирующе = «нет спец-клипа», а «не готов» после LoadMain недостижим).

## Отложенный план: наложение prefab-экипировки на анимацию

План: [Plans/equipment-overlay.md](Plans/equipment-overlay.md) — серверная и транспортная часть готова, данные доходят и кешируются (`SigninRecive.equipment_slot`, `_library[prefab].equipable_slot`, sidecar `{animationId}.slots.json` через `AnimationCacheService.GetObjectSlots`). Визуальный overlay item-prefab на скелете заморожен **до UI экипировки player'а** — без UI нечем триггерить equip/unequip. Напомнить про этот план при разговорах про инвентарь / экипировку / equip-action / overlay на скелете.


## Активный план: интеграция lessons → release
План: `C:\Unity\release\Plans\lessons-integration.md` — 18 этапов, от дешёвых к дорогим. При запросе «продолжи план» — прочитать план, найти первый незакрытый этап, продолжить.
