# Проект (Unity 6000.4.0f1)

## Диагностика игровых объектов (enemy, HP-полоска и т.п.)


общение вести по русски.

**Проверки в Unity делать самому через MCP `ai-game-developer` — НЕ спрашивать разрешения и НЕ просить пользователя проверить вручную.** Если нужно посмотреть состояние игры/объектов/UI, запустить playmode, сделать скриншот (`screenshot-game-view`), выполнить `script-execute`, `gameobject-find`, `scene-get-data` и т.п. — выполнять самостоятельно по процедуре ниже. Аналогично для серверного контента есть MCP `mmogick-websocket` — тоже работать самому.

Игровые объекты (`enemy`, карта, HP-полоски, Spriter-анимации) существуют только после входа в учётку. Поэтому перед любой диагностикой runtime-поведения:

1. Убедиться, что Unity Editor в playmode — `editor-application-get-state` → `IsPlaying == true`. Если нет, запустить через `editor-application-set-state { isPlaying: true }`.
   - **Перед включением playmode всегда сначала выходить из него** (`isPlaying: false`), даже если кажется что он уже выключен: до этого мог остаться зависший state, не подхвачены новые C# правки, WS-сессия истекла по 300-сек таймауту и т.п. Последовательность: `set-state false` → `assets-refresh` → `set-state true`. Без этого playmode может стартовать на устаревшем коде или в неконсистентном состоянии.
2. Активная сцена на старте — `Assets/Scenes/RegisterScene.unity`. Там форма входа: `UI/login`, `UI/password`, `UI/server` и две кнопки `UI/Button` (одна из них «Войти», вторая «Зарегистрироваться»).
3. **Креды уже заполнены в полях формы** (сериализованы в RegisterScene — на dev-окружении обычно `login=1, password=1, server=localhost`). **НЕ перезаписывать** поля из скриптов — просто нажать «Войти» (`Button.onClick.Invoke()`).
   - **Форма доступна сразу как стартовал playmode — НЕ ждать загрузку UI** (`Start-Sleep` перед поиском кнопки не нужен). Кнопку «Войти» искать по тексту дочернего `Text`/`TMP_Text` == «Войти» и сразу `onClick.Invoke()`. Подождать стоит только ПОСЛЕ клика — пока поднимется WS-коннект и загрузится `MainScene` с сущностями (это занимает пару секунд; проверять появление сущностей поллингом, а не фиксированной паузой).
4. После успешного логина автоматически грузится `MainScene` с enemy. Только после этого выполнять `gameobject-find`, `scene-get-data`, `screenshot-game-view` и пр. по игровым объектам — в `RegisterScene` их нет.
5. **Если на текущей карте игрока пусто (только сам player)** — переместить персонажа через MCP-админку на карту с сущностями (обычно `desert` или аналогичная). Через MCP Playwright: http://localhost/admin/ → раздел игроков → у текущего игрока сменить карту → перелогиниться в Unity-клиенте.


код сервера лежит тут Z:\var\www\html\game  (там свои скилы clude есть)
Errro() - что то типа безопсного exception , что в следдующем кадре отсоединит игрока и выведет ошибку в Ui (что бы не крашить программу, но надо retur делать что бы обратно вернулся поток программы в цикл fixedUpdate  )

Здесь есть AnimalModel, EnemyModel PlayerModel и ObjectModel и префабы на каждую только потому что на сервере сделаны такие kind
И в коде прописаны реакции на определенные event.name, event.Group.name, component.name и entity.action (анимации) в контроллерах и Моделях
Так же в игре прописан код на ряд event.code или component.code когда игроку приходят кастомного вида пакеты (вне world пакетов, например доступные настроки игры или книга заклинаний)

Для другой игры или в прцоессе разработки может состав меняться - следовательно надо менять клиент


## Образец UI в MainScene (без входа в игру)

На `Assets/Scenes/MainScene.unity` лежит **образец всех UI-элементов** игры — выложен в иерархии в выключенном виде (или с фиктивными данными). Можно открывать сцену **без playmode** и инспектировать прямо в Editor: настройки RectTransform, Anchors, Pivot, Content Size Fitter, Vertical/HorizontalLayoutGroup и т.п.

При любой задаче «что-то не так с UI» (размер, позиция, отступы, anchors, layout) — **сначала смотреть образец в MainScene**, потом править. Не делать догадок про значения по умолчанию — открыть, посмотреть конкретный объект, использовать `gameobject-find` / `gameobject-component-get` по нужному UI-элементу.

## DebugGrid

Каждая карта содержит выключенный Tilemap-слой `Map/<id>/DebugGrid` (создаётся в [MapDecodeModel.cs](Assets/Plugins/Mmogick/Tiled2Unity/MapDecodeModel.cs), визуализирует границы тайловых клеток для проверки позиционирования/выравнивания). Включить через `script-execute` → `SetActive(true)` — видно в Scene и Game View.

## Админка (dev-креды) , но для работы сокнтентом и серерами есть mcp сервер mmogick-websocket (код его в коде сервера)
- URL: http://localhost/admin/
- Login: `admin@my-fantasy.ru`
- Пароль: `123456`
- Для автоматической проверки страниц использовать MCP Playwright.

Есть mcp unity для самостоятельной проверки клиента (пакеты приходящие логируются , на сервере тоже есть лог)
Все настройки проекта Unity что делаются должны быть описаны в readme (какие и зачем)

## Архитектура анимаций: Spriter (per-prefab) + Unity-Animator (универсальные эффекты)

Механизм подробно задокументирован XML-doc'ами в коде — здесь только обзор, карта файлов и cross-file инварианты (их ни один отдельный файл не покрывает). За деталями — в код:
- `EntityModel.PlayAction` (единая точка: Spriter-приоритет → Universal-fallback), `EnsureUniversalAnimator`/`OnAnimatorAttached` (привязка overlay-Animator'а, startDisabled для image-init), rotation projectile'ов в `SetData` — [EntityModel.cs](Assets/Plugins/Mmogick/Client/Model/EntityModel.cs).
- guard `_hasParamX/_hasParamY` для `Forward` setter (Universal.controller не имеет `x/y`, без guard'а спам `Parameter 'x' does not exist`) — [ObjectModel.cs](Assets/Scripts/Model/ObjectModel.cs).
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
- Бросать exception (через `throw` или через `Error()/Errro()` — они безопасно отсоединяют игрока и показывают UI-ошибку).
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