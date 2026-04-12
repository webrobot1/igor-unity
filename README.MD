# Демонстрационная 2D MMORPG игра "Игорь" (клиент) проекта [http://mmogick.ru/](http://mmogick.ru/) на Unity.
## Смотрите так же [Игорь - серверная часть](https://github.com/webrobot1/igor-server).
### Демо [тут](http://mmogick.ru/articles/frontend/index/eyJpZCI6Mn0=)
![Слайд16](https://github.com/webrobot1/webrobot1/assets/20768848/5c93a1c2-63e6-4c49-a2d0-70898746d498)

Unity 2021.3.0f1 (LTS). Клиент для 2D MMO RPG сервера.

## Структура проекта

Весь код находится в `Assets/Scripts/`:

| Папка | Назначение |
|-------|-----------|
| `Controller/` | Контроллеры игровой логики (камера, игрок, заклинания, UI, таргетинг) |
| `Model/` | Модели сущностей (Player, Enemy, Object) |
| `Struct/` | Структуры данных для обмена с сервером (Recive/Response) |
| `Classes/` | UI-классы |

Всё остальное в `Assets/` — UI-интерфейс, спрайты, анимации и ресурсы отображения. В перспективе анимации и карты будут загружаться с сервера при старте клиента (аналогично тому, как уже загружается тайлмап).

### Настройки подключения

В `Assets/Plugins/Mmogick/Client/Controller/BaseController.cs` задаются:
- `GAME_ID` — ID вашего проекта в личном кабинете http://mmogick.ru/ (раздел Игры)
- `SERVER` — адрес сервера по умолчанию (можно переопределить через UI при авторизации)

### Авторизация

Форма авторизации (RegisterScene) содержит поля: сервер, логин, пароль. Адрес сервера предзаполняется из `SERVER`, но может быть изменён пользователем.

API маршруты:
- `POST /api/game/{gameId}/register` — регистрация (login, password в теле запроса)
- `POST /api/game/{gameId}/auth` — авторизация (login, password в теле запроса)
- `GET /maps2d/patch/get_map/{mapId}/{token}` — получение данных карты

Сервер возвращает `host` (адрес WebSocket), `key`, `token` — после чего клиент подключается по WebSocket к игровой карте.

### Типы данных в C# структурах

При разработке C# структур для десериализации серверных данных — использовать типы данных и имена полей **как они приходят с сервера** (PHP). Не подгонять сервер под клиент. Типы: если сервер отдаёт `bool` (`true`/`false`) — в C# использовать `bool`, а не `int` (0/1). Имена: slug компонентов, событий, групп событий, ключи JSON, имена полей в C# структурах должны точно совпадать с серверными. При анализе несовпадений — сверять с серверным кодом (файлы `Build/Game/storage/` и БД) и искать похожие варианты написания (`hpmax` vs `hp_max` vs `maxHp`).

### Описание папок

	Assets\Plugins						- плагины для работы с игры Unity с: фокусом WebGl, страницы отладки на странцие http://mmogick.ru/api, websocket в тч webgl и др описанные в этой папке отдельно
	  Assets\Plugins\GitIntegration				- плагины интеграции с Git по статье https://habr.com/ru/post/493488/
	  Assets\Plugins\Joystick Pack				- джойстик ttps://assetstore.unity.com/packages/tools/input-management/joystick-pack-107631
	  Assets\Plugins\SharpZipLib		    		- плагин распаковки игровых карт (http://mmogick.ru/articles/frontend/index/eyJpZCI6MTd9) которые приходят с сервера
	  Assets\Plugins\Startup		    		- добалвяет сцену RegisterScene и MainScene в настрйоки сборки билда + может подтягивать доп плагины из unity (не обязателен в проекте)
	  Assets\Plugins\WebGLSupport		    		- набор плагинов нужных в сборке WebGl для: работы фокуса текстовых полей, изменение размера окна, Websocket соединения (в браузере), работы с отладкой в админ панели (http://mmogick.ru/articles/frontend/index/eyJpZCI6MjB9)
	  Assets\Plugins\WebsocketSharp				- скомпилирвоанный плагин (https://github.com/sta/websocket-sharp) для Websocket соединения (в мобильных устройствах и ПК)
	  Assets\Plugins\MyFantasy		   		- плагин для интеграции с онлайн-сервером http://mmogick.ru/articles/frontend/index/eyJpZCI6M30=
		Assets\Plugins\MyFantasy\Model  		- базовые классы моделей (PlayerModel.cs, EnemyModel.cs, ObjectModel.cs) которые изменяют анимацию в клиенте в зависимости от того что за стандартные данные (например координаты) пришли
		Assets\Plugins\MyFantasy\Protocol  		- содержит классы что создают WebSocket класс (для браузера и ПК с мобилками он разный) и вешают на него обработчики
		Assets\Plugins\MyFantasy\Scripts\Controller 	- игровые классы
		  BaseController.cs		 		- базовый класс от которого наследуются RegisterController.cs и ConnectController.csю содержит адрес сервера и общий метод для управления фокусов в WebGl и http запросов
		  ConnectController.cs				- создает инициализирует вызрв класса WebSocket, обрабатыавет пакеты с игровым миром и шлет данные дальше в модели через метод SetData
		Assets\Plugins\MyFantasy\Struct  		- описание структур запросов и ответов от сервера
		  Map\						- описание стрктур на которые разбивается декодированная карта с сервера
		  Recive\  					- описание структур полученных от сервера данных (это базовые полученыне пакеты которые не зависят от компонтентов и механик в админке, например карты, авторизация и тд)
		  Response\  					- описание структур отправляемых на сервер данных (это базовые запросы которые не зависят от компонтентов и механик в админке, например для движения)
	Assets\Resources		- папка с графикой и префабами (пока у сервера нет функционала загрузки и работы с графикой и анимацией что бы хранить ее на сервере)
	Assets\WebGLTemplates  		- содержит шаблон WebGl для работы панели отладки (http://mmogick.ru/articles/frontend/index/eyJpZCI6MjB9) - отличается от стандартного наличием дополнительного JavaScript кода
	Assets\Scenes			- содержит сцену входа/регистрации и сцену игры. тк все даныне карт и инфомрация о объектах содержится на сервере - то сцена пуста, ее заполняет Assets\Scripts\Controller\ConnectController
	Assets\Scripts\Model  		- классы моделей префабов (унаследованных от PlayerModel.cs, EnemyModel.cs, ObjectModel.cs) которые изменяют анимацию в клиенте в зависимости от того что за данные пришли (в частности компоненты)
	Assets\Scripts\Struct  		- описание структур запросов и ответов от сервера
	  Recive\  			- стуктуры что нужно переопределить в плагине (в частности это стукруты игроков, монстров и объектов и их пользовательские поля (http://mmogick.ru/articles/frontend/index/eyJpZCI6MTl9))
	  Response\  			- структуры игровых механик (команд что отправляются с клиента) с данными что ожидает сервер (настраиваются в личном кабинете сервера http://mmogick.ru/articles/frontend/index/eyJpZCI6MTh9)
	Assets\Scripts\Controller       - игровые классы
	  CameraController.cs		- изменение размер отдаления камеры для разного масштаба
	  PlayerController.cs   	- класc обрабатывает что нажал игрок и что надо отправлять на сервер, наследуется от ConnectController
	  RegisterController.cs		- класс обработчик сцены входа и регистрации в игру , после загружает сцену Main и передает управление PlayerController.cs

Для разработки непосредственно вашей игры вам работать в папке Assets/Scripts, подробнее в документации на [сайте](http://mmogick.ru/articles/frontend/index/eyJpZCI6NjB9)

	PS NewTonJson выбран взамен стандартного UnityJson для работы со структурами не просто так, менять его на UnityJson - на свой страх и риск
	PS для webgl может понадобиться отключить profiling в Built Settings тк забьется память браузера в console после прихода по websocket большого количества пакетов

 Игра одинаково хорошо взаимодействует с сервером в версиях для ПК, браузеров или мобильных устройств

![Презентация игрового сервера Моя Фантазия от программиста Стрельцова Михаила Вячеславовича](https://github.com/webrobot1/webrobot1/assets/20768848/576982cd-31d9-4c70-b28b-6239911dc041)
![Игорь - 2D MMORPG на Unity, клиентская часть](https://github.com/webrobot1/webrobot1/assets/20768848/c291e8ae-9e2f-4745-93c7-2f95df620cd2)


## Среда разработки

Unity Editor запущен на **Windows**, проект (`C:\Unity\release`) — в файловой системе Windows, доступен из WSL как `/mnt/c/Unity/release`. При работе через WSL Claude Code читает/пишет файлы через `/mnt/c/`, но Unity компилирует и запускает их на Windows-стороне.

### Workflow разработки клиентских фич (lessons → release)

Фичи реализуются **последовательно** в `/mnt/c/Unity/release`:
1. Определить что нового в очередном уроке — сравнить содержимое папки урока с предыдущим уроком
2. Понять как новая механика взаимодействует с сервером — посмотреть текущий код sandbox (`/var/www/html/game/Build/Sandbox/storage`) и существующие механики/компоненты (`/var/www/html/game/Build/Game/storage`)
3. Реализовать фичу в `/mnt/c/Unity/release` с учётом того, как устроен текущий клиент
4. Документировать реализацию (описание механики, взаимодействие клиент-сервер) в `/var/www/html/lessons/README.MD` с пометкой номера урока (lesson)

При разработке новой фичи — обязательно смотреть как была реализована предыдущая, чтобы сохранить единообразие подхода.

## Окружение разработчика

Установить расширение VS Code - C# Dev Kit
Установить расширение VS Code - Unity for Visual Studio Code (автодополнение Unity API, подсветка .shader/.asmdef)
Установить расширение VS Code - Claude Code (опционально, для работы с AI-ассистентом)

В Unity Editor: Edit → Preferences → External Tools → External Script Editor — выбрать VS Code.
Unity сгенерирует .sln и .csproj файлы — автодополнение, навигация по коду заработают.

Если работа ведётся только с Unity-клиентом без серверной части (PHP) — WSL не требуется.
VS Code открывается на Windows напрямую (`C:\Unity\release`), все расширения и отладка работают нативно без ограничений.

При связке Windows + WSL всё программное обеспечение (Unity, VS Code, .NET SDK, расширения) устанавливается **на Windows**. В WSL ставится только серверная часть (PHP, MySQL, Apache).

#### AI Game Developer (опционально)

Плагин [AI Game Developer](https://ai-game.dev/download) (Unity MCP) позволяет AI-ассистентам (Claude Code, Cursor) управлять Unity Editor: выполнять C# код, кликать по UI, читать состояние объектов, делать скриншоты. Подробности подключения к Claude Code — см. `docs/Окружение Claude Code.MD` серверного проекта.

