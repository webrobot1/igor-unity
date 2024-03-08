Клиентская часть 2D MMORPG онлайн игры "Игорь" для демонстрации проекта [http://my-fantasy.ru/](http://my-fantasy.ru/) на Unity.
Видео описание работы входящего в игру плагина сетевого взаимодействия [YouTube](https://youtu.be/NyRiODUeChE).
Серверная часть игры доступна в реппозитории [Игорь - серверная часть](https://github.com/webrobot1/igor-server).

Общее описание папок:

	Assets\Plugins						- плагины для работы с игры Unity с: фокусом WebGl, страницы отладки на странцие http://my-fantasy.ru/api, websocket в тч webgl и др описанные в этой папке отдельно
	  Assets\Plugins\GitIntegration				- плагины интеграции с Git по статье https://habr.com/ru/post/493488/
	  Assets\Plugins\Joystick Pack				- джойстик ttps://assetstore.unity.com/packages/tools/input-management/joystick-pack-107631
	  Assets\Plugins\SharpZipLib		    		- плагин распаковки игровых карт (http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTd9) которые приходят с сервера
	  Assets\Plugins\Startup		    		- добалвяет сцену RegisterScene и MainScene в настрйоки сборки билда + может подтягивать доп плагины из unity (не обязателен в проекте)
	  Assets\Plugins\WebGLSupport		    		- набор плагинов нужных в сборке WebGl для: работы фокуса текстовых полей, изменение размера окна, Websocket соединения (в браузере), работы с отладкой в админ панели (http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MjB9)
	  Assets\Plugins\WebsocketSharp				- скомпилирвоанный плагин (https://github.com/sta/websocket-sharp) для Websocket соединения (в мобильных устройствах и ПК)
	  Assets\Plugins\MyFantasy		   		- плагин для интеграции с онлайн-сервером http://my-fantasy.ru/articles/frontend/index/eyJpZCI6M30=
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
	Assets\WebGLTemplates  		- содержит шаблон WebGl для работы панели отладки (http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MjB9) - отличается от стандартного наличием дополнительного JavaScript кода 
	Assets\Scenes			- содержит сцену входа/регистрации и сцену игры. тк все даныне карт и инфомрация о объектах содержится на сервере - то сцена пуста, ее заполняет Assets\Scripts\Controller\ConnectController
	Assets\Scripts\Model  		- классы моделей префабов (унаследованных от PlayerModel.cs, EnemyModel.cs, ObjectModel.cs) которые изменяют анимацию в клиенте в зависимости от того что за данные пришли (в частности компоненты)
	Assets\Scripts\Struct  		- описание структур запросов и ответов от сервера
	  Recive\  			- стуктуры что нужно переопределить в плагине (в частности это стукруты игроков, монстров и объектов и их пользовательские поля (http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTl9))
	  Response\  			- структуры игровых механик (команд что отправляются с клиента) с данными что ожидает сервер (настраиваются в личном кабинете сервера http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTh9)
	Assets\Scripts\Controller       - игровые классы 
	  CameraController.cs		- изменение размер отдаления камеры для разного масштаба
	  PlayerController.cs   	- класc обрабатывает что нажал игрок и что надо отправлять на сервер, наследуется от ConnectController
	  RegisterController.cs		- класс обработчик сцены входа и регистрации в игру , после загружает сцену Main и передает управление PlayerController.cs 
	


Для разработки непосредственно вашей игры вам понадобиться обновлять из данного репозитория Assest/Plugins (тк я доблавяю все новые функции) и работать в папке Assets/Scripts:

+ изменить дизайн Assets\Scenes\RegisterScene (опционально)
+ изменить Assets\Scripts\Controller\PlayerController (что бы обрабатывать action команды которые глобально что то делают из [событий](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTh9) в админ панели) - он висит на Camera MainScene, [пример](https://github.com/webrobot1/unity/tree/master/Assets/Scripts/Controller/PlayerController.cs) 
+ изменить CameraController из примера  (опционально возможно захочется добавить pixelPerfect или cinemachine camera механики и тд) - он так же висит на на Camera в MainScene, [пример](https://github.com/webrobot1/unity/tree/master/Assets/Scripts/Controller/CameraController.cs)
+ изменить Assets\Scripts\Struct\Recive\ComponentsRecive добавив туда пользовательские поля - [компонтенты](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTl9) созданные Вами в админ панели, [пример](https://github.com/webrobot1/unity/tree/master/Assets/Scripts/Struct/Recive/) 
+ изменить Assets\Scripts\Model\NewPlayerModel, NewEnemyModel, NewObjectModel что бы указвать что делать в игре при изменении полей и компонентов (какую анимацию включать. компонетнты меняются на сервере благодаря [игровым событиям](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTh9) - [пример](https://github.com/webrobot1/unity/tree/master/Assets/Scripts/Model/)	
+ скомпилировать билд webgl для [отладки](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MjB9), после чего [загрузить его](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MjF9) в админ панель и добавлять [карты](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTd9), игровые механики, [пользователские поля](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTl9). 
+ После откладки можно так же создать игру для мобильных устройств и ПК с загрузкой ее на маркет плейсы (Google Play Market, Steam) тк данная демо игра поддерживает портирование своих библиотек на различные устройства
	
Все вышеуказанные действия уже сделаны в игре и вы можете просто скопировать проект и добавить-изменить механики и компонтенты в админ панели на те что вам нужны убрав-изменит в моделях какая анимация должна запускаться. И пусть Вас не смущает что сцен всего две - карты сами будут подгружаться с сервера на MainScene при изменении на сервере карты игрока (сами карты и объекты на них менять можно в [админ панели](http://my-fantasy.ru/articles/frontend/index/eyJpZCI6MTd9))	


	PS NewTonJson выбран взамен стандартного UnityJson для работы со структурами не просто так, менять его на UnityJson - на свой страх и риск 
	PS для webgl может понадобиться отключить profiling в Built Settings тк забьется память браузера в console после прихода по websocket большого количества пакетов
	PS если вы захотите поставить взамен WebsocketSharp из данного проекта что то свое знайте:
	
+ в подобных библиотеках используется код примерно следующего содержания: "_tcpClient = new TcpClient (_uri.DnsSafeHost, _uri.Port);" - который необходимо дополнять строками вида _tcpClient.NoDelay = true; (из коробки в WebSocketSharp этого нетб но в данном прокете уже все скомпилировано как надо). В противном случае TcpClient в C# НЕ отправит ваш пакет пока буфер не заполнится (то есть если пакеты у вас на телефоне не шлются достаточно часто он будет ждать их) - это назвается алгоритм Нейгла.	 

![Игорь - 2D MMORPG на Unity, клиентская часть](https://github.com/webrobot1/webrobot1/assets/20768848/c291e8ae-9e2f-4745-93c7-2f95df620cd2)
	
