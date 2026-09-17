using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
	using System.Diagnostics;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
	using WebGLWebsocket;
#else
	using WebSocketSharp;
	// Note: Ensure WebSocketSharp is installed via NuGet or as a local package
#endif


namespace Mmogick
{
	/// <summary>
	/// Класс для создания и поддержания соединения с сервером
	/// </summary>
	public abstract class ConnectController : BaseController
	{
		public const string ACTION_REMOVE = "remove";
		public const string ACTION_LOAD = "load";
	
		/// <summary>
		/// позволить слать запрос к серверу чуть раньше (на время доставки пакета - расчитвается как пол пинга) что бы к моменту таймаута события сервера запрос на новое уже был
		/// число - на сколько делим PING что бы обозначит время на доставку пакета в одну сторону (0.5 = считается половиной ping которая приближена ко времени на доставку пакета в одну сторону)
		/// меньше можно ольше не нужно тк будет ошибка на сервере что слишком быстро пришел пакет и запрос по сути будет зря
		/// </summary>
		private const float INTERPOLATION = 0.5f;

		/// <summary>
		/// максимальное количество секунд паузы между загрузками.
		/// Предел покрывает и ХОЛОДНЫЙ подъём карты — процесс её сервера ещё не запущен, и соединение
		/// устанавливается ощутимо дольше обычного (замер: ровно десятая секунда). Порог человеческий:
		/// паузу игроку закрывает панель загрузки, а дальше честнее показать ошибку, чем ждать молча.
		/// </summary>
		private const int MAX_PAUSE_SEC = 30;

		/// <summary>
		/// сколько последних замеров держит окно, по которому считается задержка. Оценка окна — медиана, потому
		/// длина нечётная: одиночный выброс (просадка сети, свёрнутое окно, простой сервера) не сдвигает её вовсе,
		/// тогда как в среднем он держался бы, пока окно не сменится целиком. Цена выброса тут не только в числе:
		/// завышенная задержка растягивает шаг персонажа и разрежает отправку команд, а замеры уходят вместе с
		/// ними — окно обновляется тем медленнее, чем сильнее промах.
		/// </summary>
		private const int MAX_PING_HISTORY = 5;

		/// <summary>
		/// через сколько секунд передавать на сервер результаты расчета пинга (не чаще чем сохраняется игрок в бд)
		/// </summary>
		private const double PING_SEND_SEC = 60;

		/// <summary>
		/// через сколько секунд мы отправляем на серер запрос с Unixtime для анализа пинга
		/// </summary>
		private const float PING_REQUEST_SEC = 0.5f;
			
		/// <summary>
		/// установленная на сервере длинна шага. нужно для проверки шагаем ли мы или телепортируемся (тк даже механика быстрого полета или скачек - это тоже хотьба)
		/// </summary>
		public static float step;

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (что бы player наполнить и что бы индентифицироваться в StatModel что обрабатываем нашего игрока)
		/// </summary>
		protected static string player_key;

		/// <summary>
		/// Имя server-action, обозначающее «idle»-поза (тело в покое). Приходит в /auth.
		/// Используется везде, где клиент ищет idle: ObjectModel.Update переводит им тело в покой по
		/// таймауту неактивности.
		/// Инициализируется в SigninController.LoadMain до ConnectController.Connect — т.е. до первого спавна.
		/// Default'а нет: обращение до /auth = нарушение контракта → null → сломается при первом использовании.
		/// Не хардкодить буквально "idle" в коде — использовать ЭТУ переменную, чтобы можно было
		/// переконфигурировать per-game сервером без ревизии клиента.
		/// </summary>
		public static string idle_action;

		/// <summary>
		/// Game-level список slug-ов слотов экипировки, приходит в /auth. Используется UI экипировки
		/// для рисования ровно этих ячеек. Контракт: непустое (хотя бы один slug). Установка — в SigninController.LoadMain.
		/// </summary>
		public static Dictionary<string, bool> equipment_slot;

		/// <summary>
		/// Команды, которые игрок этой игры вправе отдать (приходят в /auth): группа → {действие: true}. Пару вне
		/// перечня сервер не принимает и отключает игрока. Контракт: приходит всегда, пустой законен — у игры нет
		/// команд, доступных игроку. Установка — в SigninController.LoadMain ДО загрузки игровой сцены; спрашивать —
		/// через <see cref="HasPublicEvent"/>.
		/// </summary>
		internal static Dictionary<string, Dictionary<string, bool>> public_event;

		/// <summary>
		/// поулченный хост для нового соединения
		/// </summary>
		private static string host;

		/// <summary>
		/// сохраним для дальнейшего запроса карт (по токену проверка идет и он отправляется)
		/// </summary>
		protected static string player_token;

		/// <summary>
		/// серверный FPS. не следует ставить в клиенте такой же fps (он может быть довольно большой или наоборот малый). в клиенте жеательно 100 не больше
		/// </summary>
		public static int server_fps;

		/// <summary>
		/// сколько чисел в дробной части шага ()высчитывается автоматом
		/// </summary>
		public static int position_precision;

		/// <summary>
		/// Геометрия упора в преграду, которой сервер считает шаг существа (приходит в /auth, задаётся игрой):
		/// creep_depth — доля клетки от её центра, на которую упёршийся заходит в свою клетку в сторону преграды;
		/// corner_offset — доля клетки по каждой оси, на которую от своей позиции отходит диагональный пробник
		/// обхода угла. Ими клиент повторяет серверный расчёт шага, чтобы не слать команду движения, которой
		/// не пройти ни одной серверной веткой (CursorController). Контракт: обе строго больше нуля.
		/// Установка — в SigninController.LoadMain.
		/// </summary>
		public static float creep_depth;
		public static float corner_offset;

		/// <summary>
		/// Радиус в клетках, в котором сервер ищет проходимую клетку вокруг непроходимой цели движения
		/// (приходит в /auth, задаётся игрой). Им клиент отличает клик, на который серверу есть чем ответить,
		/// от клика вглубь сплошной преграды (CursorController). Контракт: строго больше нуля.
		/// Установка — в SigninController.LoadMain.
		/// </summary>
		public static int passable_search_radius;

		// Класс объектов карты, означающий переход на другую карту (приходит при входе). Читает разбор
		// карты — по нему он рисует метку перехода. Живёт здесь, потому что разбор карты собирается раньше
		// игровой части и её типов не видит.
		public static string warp_class;

		// Игра сессии (приходит при входе). Задаёт её учётная запись игрока, а не сборка клиента: клиент один на
		// все игры. Её номером адресуются локальные кеши и их загрузка с сервера — номер в адресе загрузки сервер
		// сверяет с игрой токена. Ставится до синхронизации кешей (SigninController.LoadMain), контракт поля — там же.
		public static int game;

		// Мир текущей карты и его название (приходят при входе, вход повторяется на каждом переходе между
		// картами). Обзорная карта берёт по ним свои карты из кеша: он копит карты всех миров, а координаты
		// открытого мира у каждого мира свои. Смена значения = уход в другой мир, прежняя раскладка не годится.
		public static int world;
		public static string world_name;

		/// <summary>
		/// Префаб нашего игрока
		/// TODO переделать в статический get - set свойство возвращающее ваш (переопределенный) объект ObjectModel
		/// </summary>
		protected static EntityModel player;

		/// <summary>
		/// Ссылка на конектор
		/// </summary>
		private static WebSocket connect;
		
		/// <summary>
		/// флаг что нужно переподключаться игнорируюя все запросы в очереди
		/// </summary>
		private static ReloadStatus reload;
		private enum ReloadStatus
		{
			Start,
			Process,
			None
		};


		/// <summary>
		/// блокирует отправку любых запросов на сервер (тк уже идет соединение). только событие load (получения с сервера игрового мира) снимает его
		/// </summary>
		private static DateTime? loading;

		// Эмуляция нестабильного пинга: задержка получения пакетов (мс). 0 = отключено
		public static int SIMULATE_LATENCY_MIN = 0;
		public static int SIMULATE_LATENCY_MAX = 0;
		private static System.Random _latencyRandom = new System.Random();
		private static Queue<KeyValuePair<DateTime, byte[]>> _delayedPackets = new Queue<KeyValuePair<DateTime, byte[]>>();

		/// <summary>
		/// если не null - загружаем сцену регистрации при ошибке или переподключаемся
		/// </summary>
		private static Coroutine coroutine;

		/// <summary>
		/// последний отправленный уже расчитаного пинга на сервер (если не будут отличаться новые пинг не отправится)
		/// </summary>
		private static double last_ping_send_value;
		
		/// <summary>
		/// последнее время отправки уже расчитаного пинга на сервер
		/// </summary>
		private static DateTime last_ping_send;

		/// <summary>
		/// когда последний раз отправили с основным пакетом текущую метку времени для расчета пинг
		/// </summary>
		private static DateTime last_ping_request;

		/// <summary>
		/// среднее значение пинга (времени нужное для доставки пакета на сервере и возврата назад. вычитая половину, время на доставку, мы можем слать запросы чуть раньше их времени таймаута)
		/// </summary>
		private static double ping;
		private static double max_ping;

		/// <summary>
		/// сопрограммы могут менять коллекцию pings и однойременное чтение из нее невозможно, поэтому делаем фиксированное поле ping со значением которое будетп еерсчитываться
		/// </summary>
		private static List<double> pings = new List<double>();

		/// <summary>
		/// Рабочая копия окна под сортировку (см. <see cref="UpdatePingStats"/>). Длина — размер окна: больше
		/// <see cref="MAX_PING_HISTORY"/> замеров в нём не держится.
		/// </summary>
		private static readonly double[] ping_window = new double[MAX_PING_HISTORY];

		/// <summary>
		/// метка времени, снятая в момент прихода уведомления о перезагрузке карты. Замер задержки с меткой
		/// СТАРШЕ этой ушёл на сервер до перезагрузки, а вернулся после неё — он мерит простой сервера, не сеть,
		/// и в окно не берётся. Не снимается: эхо таких замеров приходит уже ПОСЛЕ поднятого мира — сервер
		/// дочитывает соединение, вернувшись в свой цикл, а мир рассылает раньше этого.
		/// </summary>
		private static long reload_notice_unixtime;

		/// <summary>
		/// идёт ли перезагрузка карты — по этому признаку игроку рисуется надпись поверх игры (ReloadNotice).
		/// Отдельно от паузы loading: та стоит и на входе в игру, где перезагрузки нет вовсе.
		/// </summary>
		private static bool reloading;
		
		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>

		private static ConcurrentQueue<string> recives = new ConcurrentQueue<string>();

		/// <summary>
		/// Ошибки, ждущие показа игроку. Наполняет их <see cref="Error"/>, а зовётся он из колбэков
		/// библиотеки соединения — то есть из СЕТЕВОГО потока (см. <see cref="CloseSocket"/>: библиотека
		/// вызывает их из своего конечного автомата); читает и чистит главный, каждый кадр в
		/// <see cref="Update"/> и разбором <see cref="ErrorReturn"/>. Оттого носитель потокобезопасный — тот же,
		/// что у очереди пришедших пакетов выше: обычный список рвался бы на одновременных записи и чистке, теряя
		/// сообщение либо падая на несогласованном внутреннем массиве.
		/// </summary>
		private static ConcurrentQueue<string> errors = new ConcurrentQueue<string>();


		protected override void Awake()
		{
			// Приходящие данные, движение существ и управление игроком идут по кадрам ОТРИСОВКИ (Update), потому
			// шаг расчёта физики держим стандартным: плавность задаёт частота экрана, а на слабых устройствах
			// лишние расчёты физики дорого стоят.
			Time.fixedDeltaTime = 0.02f;
			Application.targetFrameRate = 60;
			QualitySettings.vSyncCount = 0;

			// это кажется не обязательным , но для разработки нужно что бы отключит автопресборку (Project settings->Editor->Enter Play Mode Option-> diale Domain and Scene flag)
			// Подробнее https://youtu.be/sRx14YMbLuw
			connect = null;
			coroutine = null;
			loading = null;
			player = null;

			ping = 0;
			max_ping = 0;
			last_ping_send_value = 0;
			reload_notice_unixtime = 0;
			reloading = false;

			reload = ReloadStatus.None;
			last_ping_request = DateTime.Now;
			last_ping_send = DateTime.Now;
			
			recives.Clear();
			errors.Clear();
			pings.Clear();

			base.Awake();
		}


		/// <summary>
		/// Разбор пришедших данных и проверка состояния соединения.
		///
		/// Именно в кадре отрисовки, а не в кадре расчёта физики: раньше увиденного игроком всё равно ничего не
		/// произойдёт, а частота физики — отдельная настройка, от которой скорость получения данных зависеть не
		/// должна. Пакеты приходят чаще кадра — накопленные разбираются пачкой, состояние мира всегда свежее.
		/// </summary>
		protected virtual void Update()
		{
			// обработка пакетов с эмулированной задержкой
			if (SIMULATE_LATENCY_MIN > 0)
			{
				lock (_delayedPackets)
				{
					while (_delayedPackets.Count > 0 && _delayedPackets.Peek().Key <= DateTime.Now)
					{
						byte[] rawData = _delayedPackets.Dequeue().Value;
						ProcessRawPacket(rawData);
					}
				}
			}

			// если не загружаем сцену регистрации (по ошибке)
			if (coroutine == null)
			{
				if (loading != null)
				{
					if (DateTime.Compare((DateTime)loading, DateTime.Now) < 1)
					{
						Error("WebSocket: Слишком долгая пауза загрузки");
					}
					else if (EntityModel.verbose)
						Debug.Log("WebSocket: Пауза получения запросов");
				}
				
				if (errors.Count == 0)
				{
					try
					{
						// тк в процессе разбора могут появиться новые данные то обработаем только те что здесь и сейчас были
						while(recives.Count > 0)
						{
							// Ошибку разбора Error() кладёт в errors, но выполнение продолжается — исключение он не
							// бросает. Дальше по очереди идут пакеты, опирающиеся на то, что предыдущий не доделал
							// (сущность без визуала, дельта без prefab у несозданного объекта), и валятся вторично,
							// хороня исходную ошибку под своей. Дочитывать очередь незачем: сессия уже уходит на
							// экран входа.
							if (errors.Count > 0)
								break;

							if(recives.TryDequeue(out var recive))
								Handle(recive);
						}
					}
					catch (Exception ex)
					{
						Error("WebSocket: Ошибка разбора разбора данных", ex);
					}

					if (connect!=null && reload == ReloadStatus.None && (connect.ReadyState == WebSocketState.Closed || connect.ReadyState == WebSocketState.Closing))
						Error("WebSocket: Соединение закрыто для запросов (" + connect.ReadyState + ")");

					if (reload == ReloadStatus.Start)
					{
						reload = ReloadStatus.Process;
						errors.Clear();
						loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);
						connect = null;

						if (string.IsNullOrEmpty(host))
							coroutine = StartCoroutine(LoadRegister());
						else
							Connect(host);
					}
				}
				else
					LeaveWithErrors();
			}
		}

		/// <summary>
		/// Разбор очереди ошибок снаружи цикла связи: зовёт его компонент <see cref="ErrorReturn"/> — там же,
		/// почему он живёт отдельно от контроллера.
		/// </summary>
		internal void LeaveIfErrors()
		{
			if (coroutine == null && errors.Count > 0)
				LeaveWithErrors();
		}

		/// <summary>
		/// Увести игрока на экран входа с накопленными ошибками.
		/// </summary>
		private void LeaveWithErrors()
		{
			Close();

			// Начатые загрузки (графика карт и смежных локаций, докачка визуала) снимаем: их результат
			// ляжет в мир, который уже разбирается с ошибкой и через кадр выгружается вместе со сценой.
			// Пока корутины доигрывают, они разбирают карты и валятся вторичными ошибками поверх исходной.
			// Все контроллеры клиента — одна цепочка на этом объекте, потому снятие общее; LoadRegister
			// ниже стартует уже после него.
			StopAllCoroutines();

			coroutine = StartCoroutine(LoadRegister(String.Join(", ", errors)));
			errors.Clear();   // очередь потокобезопасна: снимок для текста снят строкой выше
		}

		abstract protected void Handle(string json);

		/// <summary>
		/// Запросить переподключение с повторной авторизацией: соединение закрывается, дальше по флагу reload
		/// пойдёт вход заново. Зовётся из СЕТЕВОГО потока, когда сервер закрывает соединение, называя служебным
		/// полем конверта новую карту игрока, — он переехал туда, где мы ещё не авторизованы (см. место вызова:
		/// там же, почему разбор идёт в сетевом потоке, а не в разборе мира).
		/// </summary>
		protected static void RequestReconnect(string reason)
		{
			if (reload != ReloadStatus.None)
				return;

			reload = ReloadStatus.Start;
			Close();
			Debug.Log("WebSocket: " + reason);
		}

		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public static void Connect(string host, string player_token = null, string player_key = null)
		{
			ConnectController.host = null;

			if (player_key != null) ConnectController.player_key = player_key;
			if (player_token != null) ConnectController.player_token = player_token;

			if (string.IsNullOrEmpty(ConnectController.player_key) || string.IsNullOrEmpty(ConnectController.player_token))
				throw new Exception("WebSocket: Connect вызван без player_key/player_token и они не были заполнены ранее");

			recives.Clear();		

			coroutine = null;
			loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);

			string address = "ws://" + host;
			Debug.Log("WebSocket - соединяемся сервером " + address);

			if (errors.Count == 0)
            {		
				if (connect!=null && (connect.ReadyState == WebSocketState.Open || connect.ReadyState == WebSocketState.Connecting))
				{
					Error("WebSocket: соединение сих пор открыто");
				}
				else
				{
					try
					{
						WebSocket ws = new WebSocket(address);
						Debug.Log("WebSocket: новое соединение с сервером " + ws.Url);

						// Провал на этапе установки библиотека проводит тем же обработчиком закрытия и тем же кодом,
						// что и разрыв уже работающего соединения: OnOpen при нём не вызывается вовсе, а причину
						// (отказ разрешения имени узла, отказ в соединении) знает только её внутренний логгер —
						// в аргументы закрытия она не попадает.
						bool established = false;
						string failure = null;

						#if !UNITY_WEBGL || UNITY_EDITOR
							ws.Log.Output = (data, path) =>
							{
								if (data.Level == LogLevel.Fatal)
									failure = data.Message.Split('\n')[0].Trim();

								Debug.Log("WebSocket: " + ws.Url + " " + data.Level + ": " + data.Message);
							};
						#endif

						// так в C# можно
						ws.SetCredentials(ConnectController.player_key, ConnectController.player_token, true);
						ws.OnOpen += (object sender, EventArgs e) =>
						{
							established = true;

							#if !UNITY_WEBGL || UNITY_EDITOR
								// обязательно отключим алгоритм Nagle который не отправляет маленькие пакеты.  в браузерных websocket он отключен
								var tcpClient = typeof(WebSocket).GetField("_tcpClient", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ws) as System.Net.Sockets.TcpClient;
								tcpClient.NoDelay = true;
							#endif

							Debug.Log("WebSocket: соединение с сервером " + ws.Url + " установлено");
					
							// если к моменту соединения есть ошибки или загружена сцена регистрации
							if (coroutine != null || errors.Count > 0)
                            {
								Debug.LogWarning("WebSocket: соединение с сервером " + ws.Url + " установлено, но уже не требуется - закрываем");

								CloseSocket(ws);
							}
							// Ссылку на соединение ставит сам Connect ещё до установки; здесь её не повторяем. Проверка
							// выше и продолжение идут в сетевом потоке не атомарно: между ними главный поток может
							// снять ссылку, уводя игрока на экран входа, и повтор вернул бы её закрываемому сокету.
							else
								reload = ReloadStatus.None;
						};
						ws.OnClose += (sender, ev)  =>
						{
							if(connect != null) 
							{ 
								if (reload == ReloadStatus.None && connect == ws)
								{
									if (established)
										Error("WebSocket: текущее соединение " + connect.Url + " закрыто сервером: " + ev.Code);
									else
										Error("WebSocket: соединение с сервером " + ws.Url + " не установлено (" + ev.Code + ")" + (failure != null ? ": " + failure : ""));
								}
								else
									Debug.Log("WebSocket: закрылось старое соединение с сервером " + ws.Url);
							}
						};
						ws.OnError += (sender, ev) =>
						{
							if (reload == ReloadStatus.None && connect!=null && connect == ws)
								Error("WebSocket: " + (established ? "Ошибка соединения с сервером " : "Ошибка при установке соединения с сервером ") + connect.Url + " " + ev.Message);
							else
								// Идёт переход на другую карту либо соединение уже брошено: рвёт его сам сервер, а библиотека сообщает
								// о разрыве ошибкой. Состояние штатное — в консоль идёт обычной записью, иначе каждый переход красит
								// журнал редактора ошибкой, за которой ничего чинить не надо.
								Debug.Log("WebSocket: старое соединение " + ws.Url + " разорвано при переходе: " + ev.Message);
						};
						ws.OnMessage += (sender, ev) =>
						{
							try
							{
								// эмуляция задержки сети: буферизуем пакет для обработки позже в Update
								if (SIMULATE_LATENCY_MIN > 0)
								{
									int delay = _latencyRandom.Next(SIMULATE_LATENCY_MIN, SIMULATE_LATENCY_MAX + 1);
									byte[] copy = new byte[ev.RawData.Length];
									Buffer.BlockCopy(ev.RawData, 0, copy, 0, ev.RawData.Length);
									lock (_delayedPackets)
										_delayedPackets.Enqueue(new KeyValuePair<DateTime, byte[]>(DateTime.Now.AddMilliseconds(delay), copy));
									return;
								}

								ProcessRawPacket(ev.RawData);
							}
							catch(Exception ex)
							{
								Error("WebSocket: Ошибка обработки сообщения от сервера: ", ex);
							}
						};

						connect = ws;

						#if !UNITY_WEBGL || UNITY_EDITOR
							connect.ConnectAsync();
						#else
							connect.Connect();
						#endif

					}
					catch (Exception ex)
					{
						Error("WebSocket: Ошибка открытия соединения ", ex);
					}
				}
			}
			else
				throw new Exception("WebSocket: В процессе инициализации класса произошли ошибки");
		}

		// Обработка сырого пакета (из OnMessage или из буфера задержки)
		private static void ProcessRawPacket(byte[] rawData)
		{
			if (coroutine == null && reload == ReloadStatus.None)
			{
				string text;
				// gzip magic bytes: 0x1F 0x8B — если нет, пакет пришёл без сжатия
				if (rawData.Length >= 2 && rawData[0] == 0x1F && rawData[1] == 0x8B)
				{
					using (MemoryStream source = new MemoryStream(rawData))
					{
						using (MemoryStream target = new MemoryStream())
						{
							using (var decompressStream = new GZipStream(source, CompressionMode.Decompress))
							{
								decompressStream.CopyTo(target);
								text = Encoding.UTF8.GetString(target.ToArray());
							}
						}
					}
				}
				else
				{
					text = Encoding.UTF8.GetString(rawData);
				}

				if (text.Length == 0)
					Error("WebSocket: Пришло пустое сообщение");
				else
				{
					// Целиком пакет в лог — только по явному флагу: он приходит каждый кадр и весит килобайты,
					// а вывод в консоль редактора стоит дороже самого разбора пакета.
					if (EntityModel.verbose)
						Debug.Log("WebSocket: Пришел пакет" + text);

					// Здесь, в сетевом потоке, разбираются ТОЛЬКО служебные поля конверта: сам мир разбирает
					// главный поток (Handle), причём в свой, более конкретный тип. Разбор всего пакета и тут, и
					// там означал бы двойную сборку сотен объектов сущностей на каждый кадр.
					// Конверт читает пакет НЕ ЦЕЛИКОМ (см. его докблок), потому строгая проверка неизвестных полей
					// (BaseController.InitJsonSettings) к нему неприменима: поля мира ему и не полагается знать.
					// Гасится точечно здесь, а не отменой проверки — на своих типах она нужна.
					// NullValueHandling.Ignore обязателен любому серверному payload: сервер шлёт скаляры всегда,
					// включая null (null ≡ дефолт поля), а без Ignore Newtonsoft пишет null в не-nullable поле
					// (здесь — unixtime и reloading) и роняет разбор конверта, то есть КАЖДЫЙ пакет.
					ReciveEnvelope recive = JsonConvert.DeserializeObject<ReciveEnvelope>(text,
						new JsonSerializerSettings
						{
							MissingMemberHandling = MissingMemberHandling.Ignore,
							NullValueHandling = NullValueHandling.Ignore
						});

					if (recive.error != null)
					{
						Error(recive.error);
					}
					else
					{
						if (recive.host != null && recive.token != null)
						{
							reload = ReloadStatus.Start;
							ConnectController.host = recive.host;
							ConnectController.player_token = recive.token;
							Close();
							Debug.Log("WebSocket: смена карты — переподключение к " + recive.host);
						}

						// Адреса новой карты сервер не дал (интерьер, дальняя карта, ещё не поднятая соседняя) —
						// заходим на неё заново через авторизацию, она же карту и поднимает. Разбирается здесь, в
						// сетевом потоке: соединение сервер закрывает сразу за этим пакетом, и обработчик закрытия
						// обязан увидеть уже начатый переход — иначе примет его за обрыв и оставит игрока на экране
						// входа с ошибкой, вместо того чтобы открыть новую карту.
						else if (recive.map != null)
							RequestReconnect("переход на карту " + recive.map + " — вход заново через авторизацию");

						// Карта перезагружается: ближайшие секунды сервер не читает соединение. Держим ту же
						// паузу, что и на загрузке мира, — она уже отбивает отправку команд; надпись игроку
						// рисует отдельный признак. Снимает оба пришедший следом мир (ветка ниже).
						if (recive.reloading)
						{
							reloading = true;
							reload_notice_unixtime = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
							loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);
						}

						if (recive.action == ACTION_LOAD)
						{
							recives.Clear();
							loading = null;
							reloading = false;
						}

						// Метки нет — поле пришло нулём, и сравнение отсекает его тем же условием, что и замер,
						// ушедший до перезагрузки карты: тот мерит её простой, а не сеть (reload_notice_unixtime).
						if (recive.unixtime > reload_notice_unixtime)
						{
							pings.Add((double)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - recive.unixtime) / 1000);

							if (pings.Count > MAX_PING_HISTORY)
								pings.RemoveRange(0, pings.Count - MAX_PING_HISTORY);

							// Пересчёт на КАЖДОМ замере: оценка обязана идти и ВНИЗ. Обновление лишь на переполнении
							// окна либо на новом максимуме удерживало бы однажды снятое большое значение навсегда.
							UpdatePingStats();
						}

						if (reload == ReloadStatus.None)
							recives.Enqueue(text);
					}
				}
			}
			// Хвост пакетов уже начатого перехода: соединение сервер закрывает сразу за пакетом смены карты, и то,
			// что было в пути, приходит уже сюда. Разбирать его нечем и незачем — карта меняется.
			else if (reload != ReloadStatus.None)
			{
				if (EntityModel.verbose)
					Debug.Log("WebSocket: пакет пришёл в начатом переходе на другую карту — отброшен");
			}
			// Дальше — возврат на экран входа уже идёт (coroutine). Ошибка снимает ссылку на соединение сразу, а
			// само закрытие асинхронно: пакеты, бывшие в пути, доходят и после него, и этот хвост штатен. Живая
			// ссылка здесь исправному клиенту недостижима — её снимает закрытие до начала возврата, — потому это
			// ошибка клиента; Error заодно закрывает оставшееся соединение.
			else if (connect != null)
			{
				Error("WebSocket: пакеты продолжают приходить при возврате на экран входа, а ссылка на соединение до сих пор есть");
			}
			else if (EntityModel.verbose)
			{
				Debug.Log("WebSocket: пакет пришёл после закрытия соединения — отброшен");
			}
		}

		/// <summary>
		/// Есть ли пара «группа/действие» среди команд, которые игрок этой игры вправе отдать (<see cref="public_event"/>).
		/// Перечень кладёт вход в игру до загрузки игровой сцены: спросить раньше — ошибка порядка у вызывающего,
		/// а не «команды нет», потому бросок.
		/// </summary>
		public static bool HasPublicEvent(string group, string action)
		{
			if (public_event == null)
				throw new InvalidOperationException("Перечень команд игры не загружен: команда " + group + "/" + action + " спрошена до разбора пакета входа (SigninController.LoadMain)");

			return public_event.TryGetValue(group, out Dictionary<string, bool> actions) && actions.ContainsKey(action);
		}

		/// <summary>
		/// Отправить команду, если группа её событий свободна (условия — у проверки ниже); иначе пакет
		/// отбрасывается: очереди нет, повтор — забота вызывающего (движение шлёт команду каждый кадр, пока зажата
		/// клавиша), а в журнал отброс попадает только под <see cref="EntityModel.verbose"/> — он идёт по многу
		/// раз в секунду.
		///
		/// Команду, которой нет среди команд игры (<see cref="HasPublicEvent"/>), сюда не шлют: пару вне перечня
		/// сервер не принимает и отключает игрока, а клиент один на все игры. Отбор стоит у каждого отправителя, до
		/// сборки команды: элемент интерфейса гаснет либо действие не выполняется. Маркер элементов игры
		/// (GameElementMarker) этого отбора не заменяет — он гасит элемент, лишь когда у игры нет НИ ОДНОЙ его
		/// команды, а живой элемент шлёт каждую свою. Дошедшая сюда команда вне перечня — промах такого отбора,
		/// то есть ошибка клиента: игрок уходит на экран входа (<see cref="Error"/>).
		///
		/// Возвращает, передан ли пакет соединению на отправку; false — пакет не ушёл и сам не уйдёт: нет своего
		/// существа, идёт загрузка мира либо переподключение, соединение не открыто, существо снято с карты, группа
		/// занята прежней командой либо команды нет среди команд игры. Отправка асинхронна: доставки и ответа
		/// сервера значение не обещает.
		/// </summary>
		public static bool Send(Response data)
		{
			// Загрузка мира — служебная команда узла, не команда игры: в перечне её нет, а уходить она обязана.
			if (data.group != LoadResponse.GROUP && !HasPublicEvent(data.group, data.action))
			{
				Error("WebSocket: команды " + data.group + "/" + data.action + " нет среди команд игрока в этой игре");
				return false;
			}

			// если нет паузы или мы загружаем иир и не ждем предыдущей загрузки
			if (player != null && loading == null  && reload == ReloadStatus.None && connect!=null && connect.ReadyState == WebSocketState.Open && player.action!=ACTION_REMOVE)
			{
				try
				{
					// todo возможно не отправлять пакет если getEvent(WalkResponse.GROUP).isFinish = false

					double remain = player.GetEventRemain(data.group); // вычтем время необходимое что бы ответ ошел до сервcера (половину таймаута.тем самым слать мы можем раньше запрос чем закончится анимация)
					
					if (remain >0 && INTERPOLATION > 0 && Ping()>0)
					{
						remain -= Ping() * INTERPOLATION;
					}

					// на это время шлем пакет раньше . в нем заложена пересылка нашего пакета от websocket сервера в гейм сервер после в песочницу, отработака команды и возврат по цепочке обратно в webscoekt сервер 
					remain -= (1.0 / server_fps / 2);

					// мы можем отправить запрос сброси событие сервера или если нет события и таймаут меньше или равен таймауту события (если больще - то аналогичный запрос мы УЖЕ отправили) или если есть событие но таймаут уже близок к завершению (интерполяция)
					if
						(
							remain<=0
								||
							// на сервере нет текущего события — можно слать, но только если мы ещё не отправляли (from_client != true предотвращает повторную отправку до ответа сервера)
							(player.getEvent(data.group).action != null && player.getEvent(data.group).action == "" && remain <= player.EventTimeout(data.group) && player.getEvent(data.group).from_client != true)
								||
							// серверное событие (from_client=false) — можно перехватить. null исключён чтобы не срабатывало на неинициализированном
							(player.getEvent(data.group).from_client != true && player.getEvent(data.group).from_client != null)
						)
					{

						// поставим на паузу отправку и получение любых кроме данной команды данных
						if (data.group == LoadResponse.GROUP)
						{
							loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);
						}

						if (Ping() > 0 && Ping() != last_ping_send_value && DateTime.Compare(last_ping_send, DateTime.Now) < 1)
						{
							data.ping = last_ping_send_value = Ping();
							last_ping_send = DateTime.Now.AddSeconds(PING_SEND_SEC);
						}

                        // временная метка для расчёта пинга — сервер вернёт её обратно, клиент вычислит RTT
                        if (DateTime.Compare(last_ping_request, DateTime.Now) < 1)
                        {
							data.unixtime = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
							last_ping_request = DateTime.Now.AddSeconds(PING_REQUEST_SEC);
						}

						string json = JsonConvert.SerializeObject(
							data
							,
							Formatting.None
							,
							new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
						);

						SetTimeout(data.group);

						// сразу пометим что текущее событие нами было выслано 
						player.getEvent(data.group).from_client = true;

						// если отправили пакет и небыло action  установим null что бы в следующем кадре не слать уже
						if (player.getEvent(data.group).action == "")
							player.getEvent(data.group).action = null;

#if UNITY_EDITOR
						Debug.Log("WebSocket: Отправлен пакет " + json);
#endif
						return Put2Send(json);
					}

					if (EntityModel.verbose)
						Debug.LogWarning("Слишком частый вызов команды " + data.group + " (" + remain + " секунд осталось)");
				}
				catch (Exception ex)
				{
					Error("WebSocket: Ошибка отправки данных", ex);
				}
			}
			// отсутствие player — одна из причин попасть сюда (первое слагаемое условия выше),
			// поэтому носителя для записи тут может не быть вовсе
			else if (EntityModel.verbose)
			{
				string message = "Загрузка мира, команда " + data.action +"/"+ data.group + " отклонена";
				if (player != null)
					player.LogWarning(message);
				else
					Debug.LogWarning(message);
			}

			return false;
		}

		/// <summary>
		/// Идёт ожидание мира: соединение установлено, но полный мир (load) ещё не пришёл, либо начато
		/// переподключение при смене карты. Показывать игроку в это время нечего — по этому признаку
		/// держится панель загрузки (снятие — MapController.Update, там же видно, выложена ли карта).
		/// </summary>
		public static bool IsWaitingWorld => loading != null || reload != ReloadStatus.None;

		/// <summary>
		/// вернет время потраченное на отправку пакета и доставку его обратно в секундах (за вычетом времени задержки пакета НА сервере)
		/// </summary>
		public static double Ping()
		{
			return ping;
		}		
		
		public static double MaxPing()
		{
			return max_ping;
		}

		/// <summary>
		/// Идёт перезагрузка карты: сервер предупредил о ней и до подъёма мира соединение не читает. По этому
		/// признаку игроку рисуется надпись поверх игры — отправка команд в это время всё равно отбита паузой.
		/// </summary>
		public static bool IsReloading => reloading;

		/// <summary>
		/// Пересчёт оценок окна замеров — медианы (<see cref="ping"/>) и максимума (<see cref="max_ping"/>).
		/// Обе снимаются с одного отсортированного окна: максимум — его последний элемент.
		///
		/// Сортируем КОПИЮ, а не сам список: в нём замеры лежат по порядку прихода, и по нему из окна
		/// вытесняются самые старые. Копия — общий буфер, а не новый список на вызов: пересчёт идёт на каждом
		/// замере, до двух раз в секунду, и своя копия оседала бы мусором всю игру.
		/// </summary>
		private static void UpdatePingStats()
		{
			int count = pings.Count;
			for (int i = 0; i < count; i++)
				ping_window[i] = pings[i];

			Array.Sort(ping_window, 0, count);

			int middle = count / 2;

			ping = Math.Round(count % 2 == 1 ? ping_window[middle] : (ping_window[middle - 1] + ping_window[middle]) / 2, 3);
			max_ping = Math.Round(ping_window[count - 1], 3);
		}

		/// <summary>
		/// утановить таймаут не дожидаясь ответа от сервера на оснвое имещихся данных (при возврата с сервера данных таймаут будет пересчитан)
		/// </summary>
		private static void SetTimeout(string group)
		{
			// поставим примерно время когда наступит таймаут (с ответом он нам более точно скажет тк таймаут может и плавающий в механике быть).
			// Команду шлём и до первого ответа сервера — тогда срока он ещё не называл, и берётся умолчание.
			double timeout = player.EventTimeout(group);

			if (player.GetEventRemain(group) > Ping() / 2)								// если до конца события осталось больше чем успеет дойти запрос до сервера (время = половины пинга) то учитываем
				timeout += player.GetEventRemain(group);
			else
				timeout += Ping() / 2;                                                  // если нет - то учитываем время на доставку запроса (пол пинга)

			player.getEvent(group).finish = DateTime.Now.AddSeconds(timeout);

			player.Log("Новое значение оставшегося времени группы событий " + group + ": "+ player.GetEventRemain(group));
		}

		/// <summary>
		/// Закрытие сокета ВНЕ стека колбэка библиотеки. WebSocketSharp вызывает OnOpen/OnMessage/OnError/OnClose
		/// из своего конечного автомата, и закрытие, начатое прямо оттуда, срывается ошибкой сокета: клиент уходит
		/// на экран входа, а соединение остаётся живым — сервер по нему авторизует игрока и держит его в мире,
		/// пока сокет не оборвётся по таймауту ОС, и следующий вход накладывается на этот хвост. Ошибки же и смена
		/// карты приходят как раз из этих колбэков, поэтому уводим закрытие с их стека всегда.
		/// </summary>
		/// <param name="immediate">Закрыть синхронно — только на выходе из приложения: фоновому потоку там уже не дадут доработать</param>
		private static void CloseSocket(WebSocket ws, bool immediate = false)
		{
			#if !UNITY_WEBGL || UNITY_EDITOR
				if (immediate)
					ws.Close();
				else
					System.Threading.ThreadPool.QueueUserWorkItem(_ => ws.Close());
			#else
				// В WebGL потоков нет, а закрытие идёт через JS-обёртку, автомата библиотеки на пути нет
				ws.Close();
			#endif
		}

		private static void Close(bool immediate = false)
		{
			var c = connect;
			if (c != null)
			{
				connect = null;

				if (c.ReadyState != WebSocketState.Closed && c.ReadyState != WebSocketState.Closing)
				{
					Debug.Log("WebSocket: закрытие соединения " + c.Url);
					CloseSocket(c, immediate);
				}
				else
					Debug.LogWarning("WebSocket: соединение уже закрывается " + c.Url);
			}
		}

		// оно публичное для отладки в WebGl через админку плагин шлет сюда запрос
		// не сжимаем отправляемый на сервер пакет (он и так мал - так бы сервер лишнее время тратил на раскодировку а выхлопа сжатия малых пакетов нет и они даже больше)
		// Возвращает, передан ли пакет соединению: к этой строке его бывает уже закрыл сетевой поток, и тогда пакет не уходит.
		public static bool Put2Send(string json)
		{
			if (json.Length > 0)
			{
				// тк у нас в паралельном потоке получаются сообщения то может быть состояние гонки когда доядя до сюда уже будет null
				WebSocket c = connect;

				if (c != null && c.ReadyState == WebSocketState.Open)
				{
					byte[] bytes = Encoding.UTF8.GetBytes(json);
					#if !UNITY_WEBGL || UNITY_EDITOR
						c.SendAsync(bytes, null);
					#else
						c.Send(bytes);
					#endif
					return true;
				}
			}
			else
				Error("WebSocket: нельзя отправлять к серверу пустые строки");

			return false;
		}

		// Не бросает: кладёт текст в errors и закрывает соединение, а на экран входа с ним уводит разбор очереди
		// в конце этого кадра (ErrorReturn) либо в начале следующего (Update)
		public static new void Error(string text, Exception ex = null)
		{
			// Текст уходит игроку на экран входа, потому от исключения берём сообщение, а не весь ToString со стеком;
			// стек и без того попадает в лог строкой ниже.
			errors.Enqueue(ex != null ? text + ": " + ex.Message : text);

			if (ex!=null)
				Debug.LogException(ex);

			Debug.LogError(text);
			Close();
		}

		/// <summary>
		/// Исключение, которого код клиента не перехватил, уходит игроку тем же путём, что и <see cref="Error"/>:
		/// брошенное в корутине, Awake либо колбэке движка тот лишь пишет в консоль и обрывает вызов, а игра
		/// застывает на полпути. Общего пути у таких исключений нет, кроме журнала движка, — по нему и узнаём.
		///
		/// Подписка одна на запуск игры и переживает смену сцен. Перезагрузка домена при входе в игру выключена:
		/// подписка доживает и до режима правки, где её гасит проверка идущей игры, а новый запуск её не удваивает.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void CatchUnhandledExceptions()
		{
			Application.logMessageReceived -= OnLogMessage;
			Application.logMessageReceived += OnLogMessage;
		}

		/// <summary>
		/// Журнал зовёт обработчик внутри самой записи и только из главного потока; записи, сделанные внутри
		/// обработчика, он не принимает вовсе — ни в консоль, ни в обработчики, — потому своя запись в
		/// <see cref="Error"/> по кругу сюда не вернётся.
		/// </summary>
		private static void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			// Исключение, перехваченное и записанное самим кодом клиента (Debug.LogException), уже обработано
			// тем, кто его перехватил.
			if (type != LogType.Exception || Debug.loggingCaught || !Application.isPlaying)
				return;

			#if UNITY_EDITOR
				// Исключение редакторного инструмента в идущей игре — окна, инспектора, скрипта, собирающего или
				// включающего объекты сцены, — ошибкой игры не является, и выбрасывать за него играющего нельзя.
				// Свой код редактор зовёт через типы UnityEditor, и они стоят в стеке этого вызова; игровой цикл
				// зовёт код игры без них.
				foreach (StackFrame frame in new StackTrace().GetFrames())
					if (frame.GetMethod()?.DeclaringType?.Namespace?.StartsWith("UnityEditor") == true)
						return;

				// Продолжение async-кода инструмента либо пакета движок исполняет своим контекстом синхронизации, и
				// кадров UnityEditor в стеке у такого вызова нет. Узнаём его по месту броска — стеку самого
				// исключения. Посылка: код игры клиента async-продолжений не заводит; заведёт — брошенное в них в
				// редакторе игрока с экрана игры не уведёт.
				if (stackTrace != null && stackTrace.Contains("UnityEngine.UnitySynchronizationContext"))
					return;
			#endif

			string text = "Ошибка клиента: " + condition;

			// Из идущей игры — на экран входа; до неё, со сцены входа, — надписью на ней самой.
			if (FindAnyObjectByType<ConnectController>() != null)
				Error(text);
			else
			{
				SigninController signin = FindAnyObjectByType<SigninController>();
				if (signin == null)
					return;

				// Вход при этом снимается: исключение вложенной корутины (синхронизация кешей перед входом) движок
				// гасит по-разному — брошенное на её первом шаге отпускает внешнюю корутину дальше, и игра грузилась
				// бы поверх показанной ошибки, брошенное после ожидания оставляет внешнюю висеть навсегда.
				signin.StopAllCoroutines();
				BaseController.Error(text);
			}
		}

		public void Logout()
        {
			Error("Выход из игры");
		}

			/// <summary>
		/// Страница ошибок - загрузка страницы входа
		/// </summary>
		/// <param name="error">сама ошибка</param>
		protected virtual IEnumerator LoadRegister(string error = null)
		{
			Debug.LogWarning("WebSocket: загружаем сцену регистрации");

			// Вход, начатый на сцене входа, здесь и кончается: его корутины живут на SigninController, и снимает
			// их возврат. Иначе вход доходит до Connect и открывает соединение под уже показанным экраном входа —
			// сервер держит по нему игрока в игре, а следующий вход срывается ошибкой авторизации с другого
			// устройства. Сцены входа ещё нет (возврат из игры) — снимать нечего.
			SigninController entry = FindAnyObjectByType<SigninController>();
			if (entry != null)
				entry.StopAllCoroutines();

			// Ввод и звук отдаём сцене входа (см. SetSceneFocus): игровая сцена гасит свои до её загрузки, а сцена
			// входа включает свои, когда показана (ShowRegister ниже).
			SetSceneFocus(gameObject.scene, false);

			// Вход прервался сразу за снятием сцены входа, и она ещё выгружается: в списке сцен она есть, а загруженной
			// уже не числится. Показать на ней нечего, а выгрузить игровую сцену движок не даст — та осталась бы
			// последней. Ждём, пока сцена входа уйдёт, и грузим её заново. Иной загрузки сцены входа в это время нет:
			// грузит её только этот возврат.
			while (SceneManager.GetSceneByName(SCENE_REGISTER).IsValid() && !SceneManager.GetSceneByName(SCENE_REGISTER).isLoaded)
				yield return null;

			if (!SceneManager.GetSceneByName(SCENE_REGISTER).IsValid())
			{
				//SceneManager.UnloadScene("MainScene");
				AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(SCENE_REGISTER, new LoadSceneParameters(LoadSceneMode.Additive));

				// Unity отказалась начинать загрузку и вернула пустую операцию вместо неё. Отказ штатен, пока
				// игра сворачивается — остановлен Play Mode либо закрывается приложение: экран входа показывать
				// уже некому, а сам отказ приходит РАНЬШЕ OnApplicationQuit, поэтому завершение по нему не
				// опознать. Лежащая в сборке сцена другой причины отказать не имеет — доступность сборке и
				// отделяет эту гонку от поломки самой сборки клиента. Поломка идёт исключением, а не ошибкой
				// игроку: показать её негде, надпись ошибки живёт на той же не загрузившейся сцене.
				if (asyncLoad == null)
				{
					if (Application.CanStreamedLevelBeLoaded(SCENE_REGISTER))
						yield break;

					throw new Exception("WebSocket: сцены входа " + SCENE_REGISTER + " нет в сборке клиента — вернуть игрока на экран входа нечем");
				}

				// Wait until the asynchronous scene fully loads
				while (!asyncLoad.isDone)
				{
					yield return null;
				}
			}

			// Что показывает вернувшаяся сцена входа. Отдельной функцией, а не хвостом корутины: ждать
			// выгрузки корутине нечем — она живёт на объекте ВЫГРУЖАЕМОЙ игровой сцены, выгрузка объект
			// уничтожает, и корутина обрывается на первом же yield после её начала вместе со всем, что
			// стоит ниже. Продолжение поэтому висит на самой операции выгрузки: уничтожение объекта она
			// переживает, а работает продолжение по свершившемуся факту — ищет надпись ошибки и форму
			// входа, которых на выгруженной сцене быть уже не должно.
			void ShowRegister()
			{
				SigninController signin = FindAnyObjectByType<SigninController>();

				// Сцена входа, оставшаяся загруженной под прерванным входом, свои ввод и звук погасила перед загрузкой
				// игровой — показанная снова, включает их обратно.
				SetSceneFocus(signin.gameObject.scene, true);

				if (error != null)
					// Панель загрузки снимает сам показ ошибки: дальше игрок читает её и входит сам. В соседней
					// ветке она остаётся поднятой — переход на карту без своего адреса идёт через эту же сцену
					// входа, и закрыть её собой ровно то, ради чего панель заведена.
					BaseController.Error(error);
				else
					signin.Auth();
			}

			// Выгрузка АСИНХРОННАЯ: синхронный вызов движок объявил устаревшим и небезопасным.
			// Пустая операция — движок выгрузку не начал; продолжение всё равно исполняем сразу, иначе
			// игрок остался бы вовсе без экрана входа, ради возврата на который сюда и пришли.
			AsyncOperation unload = SceneManager.UnloadSceneAsync(SCENE_MAIN);

			if (unload == null)
				ShowRegister();
			else
				unload.completed += _ => ShowRegister();
		}

		void OnApplicationQuit()
		{
			Debug.Log("WebSocket: Закрытие приложения");
			Close(true);
			errors.Clear();
		}
	}
}