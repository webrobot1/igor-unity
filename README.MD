# Демонстрационная 2D MMORPG игра "Игорь" (клиент) на Unity.
## Смотрите так же [Игорь - серверная часть](https://github.com/webrobot1/igor-server).
![Слайд16](https://github.com/webrobot1/webrobot1/assets/20768848/5c93a1c2-63e6-4c49-a2d0-70898746d498)

Unity 6000.4.0f1. Клиент для 2D MMO RPG сервера.

## Структура проекта

Код конкретной игры — в `Assets/Scripts/`, интеграция с сервером — в `Assets/Plugins/Mmogick/`:

| Папка | Назначение |
|-------|-----------|
| `Controller/` | Контроллеры игровой логики (камера, игрок, заклинания, UI, таргетинг) |
| `Model/` | Модели сущностей (Player, Enemy, Object) |
| `Struct/` | Структуры данных для обмена с сервером (Recive/Response) |
| `Classes/` | UI-классы |
| `Enum/` | Перечисления игры |

Всё остальное в `Assets/` — UI-интерфейс, спрайты, анимации и ресурсы отображения. В перспективе анимации и карты будут загружаться с сервера при старте клиента (аналогично тому, как уже загружается тайлмап).

### Настройки подключения

В `Assets/Plugins/Mmogick/Client/Controller/BaseController.cs` задаются:
- `GAME_ID` — ID вашего проекта в личном кабинете (раздел Игры)
- `SERVER` — адрес сервера по умолчанию (можно переопределить через UI при авторизации)

### Авторизация

Форма авторизации (RegisterScene) содержит поля: сервер, логин, пароль. Адрес сервера предзаполняется из `SERVER`, но может быть изменён пользователем.

API маршруты:
- `POST /api/game/{gameId}/register` — регистрация (login, password в теле запроса)
- `POST /api/game/{gameId}/auth` — авторизация (login, password в теле запроса)
- `GET /maps2d/patch/get_map/{mapId}/{token}` — получение данных карты

Сервер возвращает `host` (адрес WebSocket), `key`, `token` — после чего клиент подключается по WebSocket к игровой карте.

### Типы данных в C# структурах

При разработке C# структур для десериализации серверных данных — использовать типы данных и имена полей **как они приходят с сервера** (PHP). Не подгонять сервер под клиент. Типы: если сервер отдаёт `bool` (`true`/`false`) — в C# использовать `bool`, а не `int` (0/1). Имена: slug компонентов, событий, групп событий, ключи JSON, имена полей в C# структурах должны точно совпадать с серверными. При анализе несовпадений — сверять с кодом и данными игры на стороне сервера, искать похожие варианты написания (`hpmax` vs `hp_max` vs `maxHp`).

### Описание папок

	Assets\Plugins						- сторонние плагины и интеграция с сервером
	  Assets\Plugins\GitIntegration				- интеграция с Git по статье https://habr.com/ru/post/493488/
	  Assets\Plugins\Joystick Pack				- джойстик https://assetstore.unity.com/packages/tools/input-management/joystick-pack-107631
	  Assets\Plugins\Startup				- добавляет сцены RegisterScene и MainScene в настройки сборки билда (в проекте не обязателен)
	  Assets\Plugins\WebGLSupport				- плагины сборки WebGL: фокус текстовых полей, размер окна, полноэкранный режим, WebSocket в браузере, панель отладки
	  Assets\Plugins\WebsocketSharp				- скомпилированный плагин (https://github.com/sta/websocket-sharp) для WebSocket на ПК и мобильных устройствах
	  Assets\Plugins\NuGet					- библиотеки, докачиваемые плагином AI Game Developer (см. ниже)
	  Assets\Plugins\Mmogick				- интеграция с онлайн-сервером
		Assets\Plugins\Mmogick\Client			- соединение с сервером и разбор пакетов игрового мира
		  Controller\BaseController.cs			- GAME_ID, адрес сервера по умолчанию, общий вывод ошибок, инициализация фокуса в WebGL; предок SigninController и ConnectController
		  Controller\ConnectController.cs		- создаёт WebSocket-соединение, распаковывает пакеты (GZip) и раздаёт данные моделям через SetData
		  Controller\MapController.cs			- сборка тайловой карты в сцене, выравнивание карты и сущностей
		  Controller\UpdateController.cs		- создание и обновление сущностей мира, сборка их визуала
		  Model\					- базовые классы моделей сущностей: меняют анимацию по пришедшим данным (координаты, компоненты)
		  Struct\					- структуры пакетов, не зависящих от механик игры (карта, авторизация, движение)
		Assets\Plugins\Mmogick\Patcher			- локальный кеш серверных данных: скелеты анимаций, тайлы, компоненты
		Assets\Plugins\Mmogick\Tiled2Unity		- декодирование карты сервера в Tilemap-слои сцены
		Assets\Plugins\Mmogick\VideoRig		- оснастка съёмки роликов, работает только в редакторе
		Assets\Plugins\Mmogick\MiliS4Logs		- замена UnityEngine.Debug: добавляет к сообщению отметку времени
	Assets\Resources		- графика и префабы, загружаемые по имени в рантайме: префабы сущностей, fallback-спрайт, курсоры, Universal-анимации
	Assets\Sprites			- спрайты интерфейса и сущностей
	Assets\Animations		- клипы Unity-Animator (универсальные эффекты поверх сущностей)
	Assets\Shaders			- шейдеры
	Assets\Fonts			- шрифты
	Assets\Scenes			- сцена входа/регистрации и игровая сцена. Данные карт и объектов приходят с сервера, игровую сцену заполняет ConnectController
	Assets\Scripts\Model		- модели префабов конкретной игры, наследники базовых моделей плагина
	Assets\Scripts\Struct		- структуры запросов и ответов, зависящие от механик конкретной игры
	  Recive\			- структуры, переопределяющие базовые: игроки, монстры, объекты и их пользовательские поля
	  Response\			- структуры команд, отправляемых на сервер (настраиваются в личном кабинете сервера)
	Assets\Scripts\Controller	- игровые классы и интерфейс
	  CameraController.cs		- масштаб и отдаление камеры
	  PlayerController.cs		- обрабатывает ввод игрока и отправляет команды на сервер, наследуется от UpdateController
	  NewSigninController.cs	- сцена входа и регистрации; после входа загружает MainScene и передаёт управление контроллерам игры
	  MainController.cs		- итоговый компонент игровой сцены, замыкает цепочку контроллеров
	Assets\Scripts\Classes		- UI-классы
	Assets\Scripts\Enum		- перечисления игры

Для разработки непосредственно вашей игры вам работать в папке Assets/Scripts, подробнее в документации

	PS NewTonJson выбран взамен стандартного UnityJson для работы со структурами не просто так, менять его на UnityJson - на свой страх и риск
	PS для webgl может понадобиться отключить profiling в Built Settings тк забьется память браузера в console после прихода по websocket большого количества пакетов

 Игра одинаково хорошо взаимодействует с сервером в версиях для ПК, браузеров или мобильных устройств

![Презентация игрового сервера Моя Фантазия от программиста Стрельцова Михаила Вячеславовича](https://github.com/webrobot1/webrobot1/assets/20768848/576982cd-31d9-4c70-b28b-6239911dc041)
![Игорь - 2D MMORPG на Unity, клиентская часть](https://github.com/webrobot1/webrobot1/assets/20768848/c291e8ae-9e2f-4745-93c7-2f95df620cd2)


## Настройки Unity-проекта, влияющие на клиентскую логику

Часть клиентской нормализации/сортировки сущностей опирается на конкретные настройки импортёра и Graphics Settings. Если их снять — картинка «поедет», хотя компиляция пройдёт чисто.

### Sprite Import: `spriteMeshType = Tight` (для всех player-fallback PNG)

- **Где:** `.meta` файлы PNG в `Assets/Sprites/Entitys/Players/**` (Warrior Idle/Walk/Attack/Dead/Hurt) — строка `spriteMeshType: 1`.
- **Зачем:** нормализация размера сущности (см. `UpdateController.UpdateObject` → `AnimationCacheService.TryGetTightRect`) читает *tight*-bounds спрайта через `Sprite.vertices`. При `Tight` Unity кладёт туда полигон вокруг непрозрачных пикселей; при `FullRect` — 4 угла всей `sprite.rect`, включая прозрачные поля PNG. Если поставить `FullRect`, персонажи с «воздухом» в PNG будут визуально мельче остальных в клетке карты.
- **Дополнительно:** `isReadable` (`GetPixels32`) НЕ требуется — `Sprite.vertices` работает без Read/Write Enabled.

Для **runtime-спрайтов** (Spriter-анимации, загружаются `AnimationCacheService.GetSprite` из PNG-байтов с диска сервера) то же самое — `Sprite.Create(..., SpriteMeshType.Tight)`.

### Graphics Settings: Transparency Sort Mode = Custom Axis `(0, 1, -1)`

- **Где:** `ProjectSettings/GraphicsSettings.asset` → `m_TransparencySortMode: 3` (CustomAxis), `m_TransparencySortAxis: {x: 0, y: 1, z: -1}`.
- **Зачем:** на тайловых картах с layers (деревья/стены/мосты) нужно, чтобы сущность могла зайти «за» препятствие, когда оно визуально выше/дальше. Ось Y>0 даёт классический «нижние объекты поверх верхних», Z<0 — карты-слои с «z=-1 = передний план» (передние тайлы на фоне сортируются над задними).
- **Связанное ограничение:** при Custom Axis каждая `SpriteRenderer` сортируется по проекции на ось **по отдельности**. Если у сущности несколько child-SR'ов (Spriter), части одной сущности могут вклиниваться между частями другой. Поэтому **каждая сущность оборачивается `UnityEngine.Rendering.SortingGroup`** (`UpdateController.UpdateObject`) — все её спрайты рендерятся как единое целое относительно других SortingGroup.

### PixelsPerUnit = 100 (Spriter-спрайты)

- **Где:** `AnimationCacheService.GetSprite` — `Sprite.Create(..., 100f, ...)`.
- **Зачем:** `SpriterDotNetBehaviour.Ppu = 100` вшито в библиотеку. `UnityAnimator.ApplySpriteTransform` считает позиции частей в юнитах деленных на PPU — если PPU у разных спрайтов сущности разный, части персонажа разлетаются.

### Tilemap Grid cellSize = (1, 1, 0) по умолчанию

- **Где:** `MapController.HandleData` — `new GameObject(...).AddComponent<Grid>()` без явной настройки cellSize.
- **Зачем:** 1 клетка карты = 1 мировой юнит. На это завязана `SpriterPostImportAdjuster.TARGET_HEIGHT = 1.0f` (все сущности приводятся к 1 клетке по высоте tight-bounds). Если когда-нибудь зададите `cellSize` вручную — пересчитать `TARGET_HEIGHT`.

### Resources/Sprites/unknow.png — единый fallback-спрайт

- **Где:** `Assets/Resources/Sprites/unknow.png` + загрузчики `Spell.Magic`, `Item.SetData`, `UpdateController` (fallback для enemy/animal/object без scml).
- **Зачем:** одна точка истины для «неизвестного» визуала. Ранее fallback был в подпапках (`Sprites/Spells/unknow`, `Sprites/Items/unknow`) — при переносе одной копии легко получить null и пустой ActionBar/Inventory.

### Flat prefabs: `Assets/Resources/Prefabs/{kind}.prefab`

- **Где:** 4 префаба `player.prefab`, `enemy.prefab`, `animal.prefab`, `object.prefab` на верхнем уровне `Prefabs/`.
- **Зачем:** `UpdateController.UpdateObject` грузит префаб через `Resources.Load("Prefabs/" + type)`, без имени и подпапок. Анимация и визуал приходят с сервера (Spriter scml), клиентский префаб — только «оболочка» (SortingGroup, Collider2D, Rigidbody2D Kinematic, модель скрипта, LifeBar). Только у `player.prefab` оставлен `Animator` с `PlayerController.controller` — как фолбэк-анимации, если сервер не прислал scml.

---

## Окружение разработчика

Установить расширение VS Code - C# Dev Kit
Установить расширение VS Code - Unity for Visual Studio Code (автодополнение Unity API, подсветка .shader/.asmdef)
Установить расширение VS Code - Claude Code (опционально, для работы с AI-ассистентом)

В Unity Editor: Edit → Preferences → External Tools → External Script Editor — выбрать VS Code.
Unity сгенерирует .sln и .csproj файлы — автодополнение, навигация по коду заработают.

Если работа ведётся только с Unity-клиентом без серверной части (PHP) — WSL не требуется.
VS Code открывается на Windows напрямую, все расширения и отладка работают нативно без ограничений.

При связке Windows + WSL всё программное обеспечение (Unity, VS Code, .NET SDK, расширения) устанавливается **на Windows**. В WSL ставится только серверная часть (PHP, MySQL, Apache).

#### AI Game Developer (опционально)

Плагин [AI Game Developer](https://ai-game.dev/download) (Unity MCP) позволяет AI-ассистентам (Claude Code, Cursor) управлять Unity Editor: выполнять C# код, кликать по UI, читать состояние объектов, делать скриншоты. Подробности подключения к Claude Code — в документации серверной части.

Стоит версия 0.89.0. **Обновляя плагин, помнить: сам он в проект почти ничего не приносит — его рабочие библиотеки лежат в `Assets/Plugins/NuGet/` и докачиваются с nuget.org при установке.** Требуемые версии перечислены в `Editor/DependencyResolver/NuGetConfig.cs` самого плагина, фактически установленные — в `Assets/Plugins/NuGet/.nuget-installed.json`. Пока библиотека не докачана, проект НЕ компилируется целиком: редакторная часть плагина ссылается на типы из неё (`MachineCredentialLock`, `LoginCommitResult`, `ITokenRevocationClient` и другие), а вместе с компиляцией отваливается и вход в Play Mode. Ошибка выглядит как «пакет плагина сломан», хотя дело в отставшей библиотеке: 0.89.0 требует `com.IvanMurzak.McpPlugin` 8.1.0, версия 0.86.x ставила 7.5.2.

Докачка бывает отбита сетью — в логе редактора `Curl error 60: Cert verify failed`. Тогда пакет берётся вручную: `https://api.nuget.org/v3-flatcontainer/com.ivanmurzak.mcpplugin/<версия>/com.ivanmurzak.mcpplugin.<версия>.nupkg`, из него `lib/netstandard2.1/McpPlugin.dll` кладётся в `Assets/Plugins/NuGet/` поверх старой. После обновления плагин ещё тянет свой серверный исполняемый файл; если тот запущен, обновление отбивается сообщением про занятый `gamedev-mcp-server.exe` — остановить и запустить подключение в окне Window → AI Game Developer.

В `Packages/manifest.json` держится ОДИН источник пакетов `OpenUPM` (`https://package.openupm.com`, области `com.ivanmurzak`, `org.nuget`, `extensions.unity`). Второй источник с тем же адресом и пересекающимися областями Unity считает недопустимой настройкой и отказывается разрешать пакеты вообще — в логе «Registry configuration is invalid… No packages loaded», при этом проект продолжает работать на старых копиях из кеша, а установка любого нового пакета молча не проходит.

Дополнение плагина для Cinemachine (`…mcp.cinemachine`) снято вместе с самим пакетом `com.unity.cinemachine`: игра виртуальными камерами не пользуется — камеру ведёт `CameraController` обычной камерой сцены, — а пакет Cinemachine тянуло в проект только это дополнение. Понадобится — вернуть двумя строками в манифесте.

