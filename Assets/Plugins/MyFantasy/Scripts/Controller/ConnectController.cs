using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WebGLSupport;

#if UNITY_WEBGL && !UNITY_EDITOR
	using WebGLWebsocket;
#else
	using WebSocketSharp;
#endif


namespace MyFantasy
{
	/// <summary>
	/// Класс для создания и поддержания соединения с сервером
	/// </summary>
	public abstract class ConnectController : BaseController
	{
		public const string ACTION_REMOVE = "remove";
		public const string ACTION_LOAD = "load";

		/// <summary>
		/// установленная на сервере длинна шага. нужно для проверки шагаем ли мы или телепортируемся (тк даже механика быстрого полета или скачек - это тоже хотьба)
		/// </summary>
		public static float step;

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (что бы player наполнить и что бы индентифицироваться в StatModel что обрабатываем нашего игрока)
		/// </summary>
		protected static string player_key;

		/// <summary>
		/// сохраним для дальнейшего запроса карт (по токену проверка идет и он отправляется)
		/// </summary>
		protected static string player_token;

		/// <summary>
		/// текущий используемый хост
		/// </summary>
		private static string host;

		/// <summary>
		/// серверный FPS. не следует ставить в клиенте такой же fps (он может быть довольно большой или наоборот малый). в клиенте жеательно 100 не больше
		/// </summary>
		public static int server_fps;

		/// <summary>
		/// сколько чисел в дробной части шага ()высчитывается автоматом
		/// </summary>
		public static int position_precision;

		/// <summary>
		/// Префаб нашего игрока
		/// TODO переделать в статический get - set свойство возвращающее ваш (переопределенный) объект ObjectModel
		/// </summary>
		protected static EntityModel player = null;

		/// <summary>
		/// Ссылка на конектор
		/// </summary>
		private static WebSocket connect = null;
		
		/// <summary>
		/// флаг что нужно переподключаться игнорируюя все запросы в очереди
		/// </summary>
		private static ReloadStatus reload = ReloadStatus.None;
		private enum ReloadStatus
		{
			Start,
			Process,
			None
		};

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>

		private static ConcurrentQueue<string> recives = new ConcurrentQueue<string>();

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private static List<string> errors = new List<string>();

		/// <summary>
		/// блокирует отправку любых запросов на сервер (тк уже идет соединение). только событие load (получения с сервера игрового мира) снимает его
		/// </summary>
		private static DateTime? loading;

		/// <summary>
		/// если не null - загружаем сцену регистрации при ошибке или переподключаемся
		/// </summary>
		private static Coroutine coroutine;

		/// <summary>
		/// позволить слать запрос к серверу чуть раньше (на время доставки пакета - расчитвается как пол пинга) что бы к моменту таймаута события сервера запрос на новое уже был
		/// число - на сколько делим PING что бы обозначит время на доставку пакета в одну сторону (0.5 = считается половиной ping которая приближена ко времени на доставку пакета в одну сторону)
		/// меньше можно ольше не нужно тк будет ошибка на сервере что слишком быстро пришел пакет и запрос по сути будет зря
		/// </summary>
		private const float INTERPOLATION = 0.5f;

		/// <summary>
		/// максимальное количество секунд паузы между загрузками
		/// </summary>
		private const int MAX_PAUSE_SEC = 10;
		
		/// <summary>
		/// максимальное колчиество пингов для подсчета среднего (после обрезается. средний выситывается как сумма значений из истории деленое на количество с каждого запроса на сервер)
		/// </summary>
		private const int MAX_PING_HISTORY = 5;

		/// <summary>
		/// до какой длинные обрезается историй пингов после достижения максимального количества
		/// </summary>
		private const int MIN_PING_HISTORY = 2;

		/// <summary>
		/// через сколько секунд передавать на сервер результаты расчета пинга (не чаще чем сохраняется игрок в бд)
		/// </summary>
		private const double PING_SEND_SEC = 60;

		/// <summary>
		/// через сколько секунд мы отправляем на серер запрос с Unixtime для анализа пинга
		/// </summary>
		private const float PING_REQUEST_SEC = 0.5f;

		/// <summary>
		/// последний отправленный уже расчитаного пинга на сервер (если не будут отличаться новые пинг не отправится)
		/// </summary>
		private static double last_ping_send_value = 0;
		
		/// <summary>
		/// последнее время отправки уже расчитаного пинга на сервер
		/// </summary>
		private static DateTime last_ping_send = DateTime.Now;

		/// <summary>
		/// когда последний раз отправили с основным пакетом текущую метку времени для расчета пинг
		/// </summary>
		private static DateTime last_ping_request = DateTime.Now;

		/// <summary>
		/// среднее значение пинга (времени нужное для доставки пакета на сервере и возврата назад. вычитая половину, время на доставку, мы можем слать запросы чуть раньше их времени таймаута)
		/// </summary>
		private static double ping = 0;
		private static double max_ping = 0;

		/// <summary>
		/// сопрограммы могут менять коллекцию pings и однойременное чтение из нее невозможно, поэтому делаем фиксированное поле ping со значением которое будетп еерсчитываться
		/// </summary>
		private static List<double> pings = new List<double>();

		protected virtual void Update() {}


		/// <summary>
		/// Проверка наличие новых данных или ошибок соединения
		/// </summary>
		protected virtual void FixedUpdate()
		{
			// если не загружаем сцену регистрации (по ошибке)
			if (coroutine == null)
			{
				if (loading != null)
				{
					if (DateTime.Compare((DateTime)loading, DateTime.Now) < 1)
					{
						Error("Слишком долгая пауза загрузки");
					}
					else
						Debug.Log("Пауза");
				}
				
				if (errors.Count == 0)
				{
					try
					{
						// тк в процессе разбора могут появиться новые данные то обработаем только те что здесь и сейчас были
						while(recives.Count > 0) 
						{
							if(recives.TryDequeue(out var recive))
								Handle(recive);
						}
					}
					catch (Exception ex)
					{
						Error("Ошибка разбора разбора данных", ex);
					}

					if (connect!=null && reload == ReloadStatus.None && (connect.ReadyState == WebSocketState.Closed || connect.ReadyState == WebSocketState.Closing))
						Error("Соединение закрыто для запросов (" + connect.ReadyState + ")");

					if (reload == ReloadStatus.Start)
					{
						// этот флаг снимем что бы повторно не загружать карту
						reload = ReloadStatus.Process;

						// поставим этот флаг что бы был таймер нашей загрузки новой карты и текущаа обработка в Update остановилась
						loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);

						connect.CloseAsync();
						connect = null;
						Connect(host, player_key, player_token, step, position_precision, server_fps);
					}
				}
				else
				{
					coroutine = StartCoroutine(LoadRegister(String.Join(", ", errors)));
					errors.Clear();
				}	
			}
		}

		abstract protected void Handle(string json);


		protected override void Awake()
        {
			// тк пакеты обрабатываются во время FixedUpdate , но приходят чаще (в отдельном потоке onMessage)  - уменьшим паузу между запросами до 100FPS (это не зависит от FPS сервера, просто что бы небыло пауз больших) 
			// не нужно зависить и вообще знать fps сервера (он может и 1000 быть если не успевает за игрой, а при маленьком типа 30 и установки пакет в fixedupdate может запуститься попасть спустя 30мс)
			// последнее происходит если пакет пришел сразу после запуска FixedUpdate (не успел), и потом следует эта долгая пауза в 30мс до следующего
			Time.fixedDeltaTime = 0.01f;
			Application.targetFrameRate = 60;

			base.Awake();
		}

		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public static void Connect(string host, string player_key, string player_token, float step, int position_precision, int server_fps)
		{
			ConnectController.host = host;
			ConnectController.player_key = player_key;
			ConnectController.player_token = player_token;
			ConnectController.server_fps = server_fps;
			ConnectController.step = step;                                    // максимальный размер шага. умножается тк по диагонали идет больще
			ConnectController.position_precision = position_precision;        // длина шага

			recives.Clear();		

			coroutine = null;
			loading = DateTime.Now.AddSeconds(MAX_PAUSE_SEC);

			string address = "ws://" + host;
			Debug.Log("WebSocket - соединяемся сервером " + address);

			if (coroutine == null && errors.Count == 0)
            {		
				if (connect!=null && (connect.ReadyState == WebSocketState.Open || connect.ReadyState == WebSocketState.New || connect.ReadyState == WebSocketState.Connecting))
				{
					Error("WebSocket - соединение сих пор открыто");
				}
				else
				{
					try
					{
						WebSocket ws = new WebSocket(address);
						Debug.Log("WebSocket - новое соединение с сервером " + ws.Url);

						// так в C# можно
						ws.SetCredentials(player_key, player_token, true);
						ws.OnOpen += (object sender, System.EventArgs e) =>
						{

							#if !UNITY_WEBGL || UNITY_EDITOR
								// обязательно отключим алгоритм Nagle который не отправляет маленькие пакеты.  в браузерных websocket он отключен
								var tcpClient = typeof(WebSocket).GetField("_tcpClient", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ws) as System.Net.Sockets.TcpClient;
								tcpClient.NoDelay = true;
							#endif

							Debug.Log("WebSocket - соединение с сервером " + ws.Url + " установлено");
					
							// если к моменту соединения есть ошибки или загружена сцена регистрации
							if (coroutine != null || errors.Count > 0)
                            {
								ws.CloseAsync();
								throw new Exception("В процессе инициализации класса произошли ошибки");
							}							
							else
								connect = ws;
						};
						ws.OnClose += (sender, ev)  =>
						{
							if(connect != null) 
							{ 
								if (reload == ReloadStatus.None && connect == ws)
									Error("WebSocket - текущее соединение " + connect.Url+ " закрыто сервером: " + ev.Code);
								else
									Debug.Log("WebSocket - закрылось старое соединение с сервером " + ws.Url);
							}
						};
						ws.OnError += (sender, ev) =>
						{
							if (reload == ReloadStatus.None && connect!=null && connect == ws)
								Error("WebSocket - Ошибка соединения с сервером " + connect.Url + " " + ev.Message);
							else
								Debug.LogError("WebSocket - Ошибка соединени яс сервером " + ws.Url + ": " + ev.Message);
						};
						ws.OnMessage += (sender, ev) =>
						{
							try
							{
								string text = Encoding.UTF8.GetString(ev.RawData);

							#if UNITY_EDITOR
								Debug.Log("WebSocket - Пришел пакет" + text);
							#endif

								if (coroutine == null && reload == ReloadStatus.None)
								{
									Recive<EntityRecive, EntityRecive, EntityRecive> recive = JsonConvert.DeserializeObject<Recive<EntityRecive, EntityRecive, EntityRecive>>(text);

									if (recive.error != null)
									{
										Error(recive.error);
									}
									else
									{
										// сразу не подключаемя тк нужно дождаться fixed update который обработает существующие пакеты в тч и тот что пришел сейчас
										if(recive.host != null)
										{
											ConnectController.connect = null;
											ConnectController.host = recive.host;

											// поставим флаг после которого на следующем кадре запустится корутина загрузки сцены (тут нельзя запускать корутину ты мы в уже в некой корутине)
											reload = ReloadStatus.Start;
									
											// закроем соединение что бы не пришел пакет о закрытие соединения
											//Close();
											Debug.Log("WebSocket - Перезаход в игру");
										}

										if (recive.action == ACTION_LOAD)
										{
											// если это полная загрузка мира то предыдущие запросы удалим (в этом пакете есть весь мир)
											// очищать можно только тут loading  не давал Update
											recives.Clear();

											// снимем флаг загрузки и разрешим отправлять пакеты к серверу
											loading = null;
										}

										// это тоже обновим тут что бы ping и pings не делать protected 
										if (recive.unixtime > 0)
										{
											double tmp_ping = (double)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - recive.unixtime) / 1000;
											pings.Add(tmp_ping);

											// если пришедший пинг больше текущего или пришла пора обновить пинги
											if ((MAX_PING_HISTORY > 0 && pings.Count > MAX_PING_HISTORY) || pings.Count == 1 || tmp_ping > max_ping)
											{
												ping = Math.Round((pings.Sum() / pings.Count), 3);
												max_ping = Math.Round(pings.Max(), 3);
												if (MAX_PING_HISTORY > 0 && pings.Count > MAX_PING_HISTORY)
													pings.RemoveRange(0, pings.Count - MIN_PING_HISTORY);
											}
										}

										recives.Enqueue(text);
									}
								}
							}
							catch(Exception ex)
							{
								Error("WebSocket - Ошибка обработки сообщения от сервера: ", ex);
							}
						};

						connect = ws;

						#if !UNITY_WEBGL || UNITY_EDITOR
							connect.ConnectAsync();
						#else
							connect.Connect();
						#endif

						reload = ReloadStatus.None;
					}
					catch (Exception ex)
					{
						Error("WebSocket - Ошибка октрытия соединения ", ex);
					}
				}
			}
			else
				throw new Exception("В процессе инициализации класса произошли ошибки");
		}

		/// <summary>
		/// не отправляется моментально а ставиться в очередь на отправку (перезаписывает текущю). придет время - отправится на сервер (может чуть раньше если пинг большой)
		/// </summary>
		public static void Send(Response data)
		{
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
					remain -= (1 / server_fps / 2);

					// мы можем отправить запрос сброси событие сервера или если нет события и таймаут меньше или равен таймауту события (если больще - то аналогичный запрос мы УЖЕ отправили) или если есть событие но таймаут уже близок к завершению (интерполяция)
					if 
						(
							remain<=0 
								||
							// может быть и null когда событие только создано или отправили пакет когда был action == "" (что означает что на сервере нет текущего события в обработке и модно слать)
							(player.getEvent(data.group).action != null && player.getEvent(data.group).action == "" && remain <= player.getEvent(data.group).timeout) 
								||
							player.getEvent(data.group).from_client != true
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

                        // создадим условно уникальный номер нашего сообщения (она же и временная метка) для того что бы сервер вернул ее (вычив время сколько она была на сервере) и получим пинг
                        if (DateTime.Compare(last_ping_request, DateTime.Now) < 1)
                        {
							data.unixtime = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
							last_ping_request = DateTime.Now.AddSeconds(PING_REQUEST_SEC);
						}

						string json = JsonConvert.SerializeObject(
							data
							,
							Newtonsoft.Json.Formatting.None
							,
							new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
						);

						SetTimeout(data.group);

						// сразу пометим что текущее событие нами было выслано 
						player.getEvent(data.group).from_client = true;

						// если отправили пакет и небыло action  установим null что бы в следующем кадре не слать уже
						if (player.getEvent(data.group).action == "")
							player.getEvent(data.group).action = null;

						Debug.Log(player.key+": отправили в websocket " + json);
						Put2Send(json);	
					}
					//else
					//	Debug.LogError("Слишком частый вызов команды " + data.group + " (" + remain + " секунд осталось)");
				}
				catch (Exception ex)
				{
					Error("WebSocket - Ошибка отправки данных", ex);
				}		
			}
			else
				Debug.LogWarning("WebSocket - Загрузка мира, команда " + data.action +"/"+ data.group + " отклонена");
		}

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
		/// утановить таймаут не дожидаясь ответа от сервера на оснвое имещихся данных (при возврата с сервера данных таймаут будет пересчитан)
		/// </summary>
		private static void SetTimeout(string group)
		{
			// поставим примерно време когда наступит таймаут (с овтетом он нам более точно скажет тк таймаут может и плавающий в механике быть)
			double timeout = (double)player.getEvent(group).timeout;
	
			if (player.GetEventRemain(group) > Ping() / 2)								// если до конца события осталось больше чем успеет дойти запрос до сервера (время = половины пинга)  то учитываем если
				timeout += player.GetEventRemain(group);	
			else
				timeout += Ping() / 2;                                                  // если нет - то учитываем время на доставку запроса (пол пинга)

			player.getEvent(group).finish = DateTime.Now.AddSeconds(timeout);

			Debug.Log("Новое значение оставшегося времени группы событий "+ group + ": "+ player.GetEventRemain(group));
		}

		private static void Close()
		{	
			if (connect!=null)
			{
				if (connect.ReadyState != WebSocketState.Closed && connect.ReadyState != WebSocketState.Closing)
				{
					connect.CloseAsync();
					Debug.LogError("WebSocket - закрытие соедения ");
				}
				else
					Debug.LogWarning("WebSocket - содинение уже закрывается");
			}

			connect = null;
		}

		// оно публичное для отладки в WebGl через админку плагин шлет сюда запрос
		public static void Put2Send(string json)
		{
			byte[] sendBytes = Encoding.UTF8.GetBytes(json);

			// тк у нас в паралельном потоке получаются сообщения то может быть состояние гонки когда доядя до сюда уже будет null 
			if(connect!=null && connect.ReadyState == WebSocketState.Open)
            {
				#if !UNITY_WEBGL || UNITY_EDITOR
					connect.SendAsync(sendBytes, null);
				#else
					connect.Send(sendBytes);
				#endif
			}
		}

		public new static void Error (string text, Exception ex = null)
		{
			if (ex!=null)
				Debug.LogException(ex);

			Debug.LogError(text);

			Close();
			errors.Add(text);
		}


		/// <summary>
		/// Страница ошибок - загрузка страницы входа
		/// </summary>
		/// <param name="error">сама ошибка</param>
		protected virtual IEnumerator LoadRegister(string error)
		{
			Debug.LogWarning("загружаем сцену регистрации");

			if (!SceneManager.GetSceneByName("RegisterScene").IsValid())
			{
				//SceneManager.UnloadScene("MainScene");
				AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("RegisterScene", new LoadSceneParameters(LoadSceneMode.Additive));

				// Wait until the asynchronous scene fully loads
				while (!asyncLoad.isDone)
				{
					yield return null;
				}	
			}

			SceneManager.UnloadScene("MainScene");
			BaseController.Error(error);
		}

		void OnApplicationQuit()
		{
			Debug.Log("Закрытие приложения");
			Close();
		}
	}
}