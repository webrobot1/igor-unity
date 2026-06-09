# Проект (Unity 6000.4.0f1)

## Диагностика игровых объектов (enemy, HP-полоска и т.п.)


общение вести по русски.

Игровые объекты (`enemy`, карта, HP-полоски, Spriter-анимации) существуют только после входа в учётку. Поэтому перед любой диагностикой runtime-поведения:

1. Убедиться, что Unity Editor в playmode — `editor-application-get-state` → `IsPlaying == true`. Если нет, запустить через `editor-application-set-state { isPlaying: true }`.
   - **Перед включением playmode всегда сначала выходить из него** (`isPlaying: false`), даже если кажется что он уже выключен: до этого мог остаться зависший state, не подхвачены новые C# правки, WS-сессия истекла по 300-сек таймауту и т.п. Последовательность: `set-state false` → `assets-refresh` → `set-state true`. Без этого playmode может стартовать на устаревшем коде или в неконсистентном состоянии.
2. Активная сцена на старте — `Assets/Scenes/RegisterScene.unity`. Там форма входа: `UI/login`, `UI/password`, `UI/server` и две кнопки `UI/Button` (одна из них «Войти»).
3. **Креды уже заполнены в полях формы** (сериализованы в RegisterScene — на dev-окружении обычно `login=1, password=1, server=localhost`). **НЕ перезаписывать** поля из скриптов — просто нажать «Войти» (`Button.onClick.Invoke()`).
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

Каждая карта содержит выключенный Tilemap-слой `Map/<id>/DebugGrid` (создаётся в `MapDecodeModel.generate()`). Визуализирует границы тайловых клеток — для проверки позиционирования сущностей, выравнивания спрайтов и координат на карте. Включить через `script-execute` → `SetActive(true)`. Отображается в Scene View и Game View.

## Админка (dev-креды) , но для работы сокнтентом и серерами есть mcp сервер mmogick-websocket (код его в коде сервера)
- URL: http://localhost/admin/
- Login: `admin@my-fantasy.ru`
- Пароль: `123456`
- Для автоматической проверки страниц использовать MCP Playwright.

Есть mcp unity для самостоятельной проверки клиента (пакеты приходящие логируются , на сервере тоже есть лог)
Все настройки проекта Unity что делаются должны быть описаны в readme (какие и зачем)

## Архитектура анимаций: Spriter (per-prefab) + Unity-Animator (универсальные эффекты)

Анимации сущностей идут двумя слоями, сосуществующими на одном GameObject:

- **Per-prefab Spriter (SCML с сервера)** — индивидуальные анимации (`idle`, `walk`, `attack`, `hurt`, `dead`). Компонент `SpriterDotNetBehaviour`, кеш — [AnimationCacheService](Assets/Plugins/Mmogick/Patcher/AnimationCacheService.cs).
- **Универсальные эффекты в Unity-Animator** — общие декораторы поверх любой сущности. Сейчас покрыты `remove` (Puff-кадры) и `dead` (силуэты тела). Файлы: [Universal.controller](Assets/Resources/Animations/Universal.controller), [Universal/Remove/*.anim](Assets/Animations/Universal/Remove/) + [Universal/Dead/*.anim](Assets/Animations/Universal/Dead/), кадры — [Sprites/Entitys/Remove/Puff*.png](Assets/Sprites/Entitys/Remove/) и [Sprites/Entitys/Dead/dead*.png](Assets/Sprites/Entitys/Dead/). Спрайты универсальных эффектов кладутся в `Sprites/Entitys/<ActionName>/` симметрично .anim-файлам.

**Universal.controller**: 1 слой, параметры `direction` (Int 0..3, 0=down, 1=left, 2=right, 3=up) и `remove` (Trigger). AnyState→`remove_{down,left,right,up}` по `remove If 0` + `direction Equals N`. Возврат в `Idle` (motion=null) по `hasExitTime=true,exitTime=1`. **`writeDefaults=false` обязательно** на всех state'ах — иначе при transition в Idle Animator сбрасывает `SpriteRenderer.m_Sprite` в default и сущность мелькает «пустым» спрайтом перед уничтожением.

**Единая точка проигрывания — `EntityModel.PlayAction(action)`**:
1. Spriter-приоритет: если `SpriterDotNetBehaviour.Animator.HasAnimation(GetClipName(prefab, action, forward))` → `Play(clip)`.
2. Иначе Universal Animator: включает `anim.enabled=true`, корневой `SpriteRenderer.enabled=true`, выключает SR детей Spriter'а (Puff не должен перекрываться телом), `SetInteger("direction", N)` + `SetTrigger(action)`.
3. Возвращает `false`, если ни Spriter, ни Universal не отвечают — вызывающий код выполняет действие без визуала.

**Привязка Universal Animator'а** — `EntityModel.EnsureUniversalAnimator(startDisabled)`:
- Spriter-init ([NewSpriterRuntimeImporter](Assets/Plugins/Mmogick/Spriter2Unity/NewSpriterRuntimeImporter.cs)) зовёт с `false` — Animator-overlay живёт поверх Spriter'а.
- Image-init ([UpdateController](Assets/Plugins/Mmogick/Client/Controller/UpdateController.cs)) зовёт с `true` — иначе свежий Animator перехватывает SR.sprite до того как `TryGetSprite` поставит правильную картинку, и item'ы (apple, firebolt) рендерятся пустыми. PlayAction сам включает Animator перед эффектом.
- Подкласс перехватывает привязку через `protected virtual OnAnimatorAttached(Animator)`. [ObjectModel](Assets/Scripts/Model/ObjectModel.cs) обновляет в нём кеш `animator` (Awake может срабатывать до Spriter-init).

**Direction projectile'ов**: `EntityModel.SetData` поворачивает `transform.rotation = Atan2(forward.y, forward.x)` только если **нет Spriter** И **нет Animator с параметрами `x`/`y`** (последнее отсекает legacy `PlayerController.controller` с blend-tree). Universal.controller имеет только `direction`/`remove` — projectile'и с ним крутятся.

**ObjectModel.Forward setter**: `Animator.SetFloat("x"/"y")` только под guard'ом `_hasParamX`/`_hasParamY` (заполняется в `RebuildTriggersCache`). Без guard'а Universal.controller спамил бы `Parameter 'x' does not exist`.

**Чего НЕ делать**:
- Не удалять [Sprites/Entitys/Remove/Puff*.png](Assets/Sprites/Entitys/Remove/) и [Sprites/Entitys/Dead/dead*.png](Assets/Sprites/Entitys/Dead/) — Universal/{Remove,Dead}/*.anim ссылается на них по GUID.
- Не возвращать `DestroyImmediate(Animator)` в Spriter/image-init — это убьёт Universal-overlay.
- Не выключать `writeDefaults=false` в Universal.controller state'ах.
- Если расширяешь Universal-fallback на НЕ-удаляющие action'ы (hurt-flash и т.п.) — нужно дописать восстановление SR детей Spriter'а после эффекта; сейчас они выключаются безвозвратно, потому что после remove GameObject всё равно уничтожается.

## Логирование

Два уровня логов:

- **`#if UNITY_EDITOR`** — событийные логи (приём/отправка пакетов). Выводятся всегда в редакторе, не отключаются. Используются для нечастых, но важных событий.
- **`EntityModel.verbose`** (`false` по умолчанию) — высокочастотные логи (каждый FixedUpdate, каждый клик, тайминги событий). Включать вручную в рантайме (`EntityModel.verbose = true`) только при отладке конкретной проблемы, иначе консоль забивается на 100 сообщений/сек.

Новые логи добавлять по тому же принципу: если может спамить каждый кадр — через `verbose`/`player.Log()`. Если событийный (раз в N пакетов) — через `#if UNITY_EDITOR`.

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