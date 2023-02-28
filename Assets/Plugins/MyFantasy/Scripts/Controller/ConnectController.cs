using Newtonsoft.Json;
using System;
using System.Collections;
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
	/// Класс для обработки создания вебсокет соединения после перехода на MainScene
	/// </summary>
	public abstract class ConnectController : BaseController
	{
		private const string ACTION_RECONNECT = "reconnect";
		public const string ACTION_REMOVE = "remove";
		public const string ACTION_LOAD = "load";

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (что бы player наполнить и что бы индентифицироваться в StatModel что обрабатываем нашего игрока)
		/// </summary>
		public static string player_key;

		/// <summary>
		/// Префаб нашего игрока
		/// </summary>
		public static ObjectModel player;

		/// <summary>
		/// сохраним для дальнейшего запроса карт (по токену проверка идет и он отправляется)
		/// </summary>
		protected static string player_token;

		/// <summary>
		/// Ссылка на конектор
		/// </summary>
		private static WebSocket connect;

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private static List<string> recives = new List<string>();

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private static List<string> errors = new List<string>();

		/// <summary>
		/// флаг что нужно переподключаться игнорируюя все запросы в очереди
		/// </summary>
		private static bool reload = false;		
		
		/// <summary>
		/// блокирует отправку любых запросов на сервер (тк уже идет соединение). только событие load (получения с сервера игрового мира) снимает его
		/// </summary>
		private static DateTime? loading = DateTime.Now;

		/// <summary>
		/// если не null - загружаем сцену регистрации при ошибке или переподключаемся
		/// </summary>
		private static Coroutine coroutine = null;

		/// <summary>
		/// последний отправленный пинг на сервер
		/// </summary>
		private double last_send_ping = 0;

		/// <summary>
		/// время последнего запроса пинга на сервер
		/// </summary>
		private DateTime last_ping_request = DateTime.Now;

		/// <summary>
		/// среднее значение пинга (времени нужное для доставки пакета на сервере и возврата назад. вычитая половину, время на доставку, мы можем слать запросы чуть раньше их времени таймаута)
		/// </summary>
		private static double ping = 0;
		
		/// <summary>
		/// сопрограммы могут менять коллекцию pings и однойременное чтение из нее невозможно, поэтому делаем фиксированное поле ping со значением которое будетп еерсчитываться
		/// </summary>
		private static List<double> pings = new List<double>();

		/// <summary>
		/// максимальное количество секунд паузы между загрузками
		/// </summary>
		protected int max_pause_sec = 10;

		/// <summary>
		/// через сколько секунд мы отправляем на серер запрос для анализа пинга
		/// </summary>
		protected int ping_request_sec = 1;

		/// <summary>
		/// максимальное колчиество пингов для подсчета среднего (после обрезается. средний выситывается как сумма значений из истории деленое на количество с каждого запроса на сервер)
		/// </summary>
		protected static int max_ping_history = 10;

		/// <summary>
		/// до какой длинные обрезается историй пингов после достижения максимального количества
		/// </summary>
		protected static int min_ping_history = 5;

		/// <summary>
		/// Проверка наличие новых данных или ошибок соединения
		/// </summary>
		protected virtual void Update()
		{       
			// если не загружаем сцену регистрации (по ошибке)
			if (coroutine == null)
			{
				if (loading != null)
				{
					if (DateTime.Compare(((DateTime)loading).AddSeconds(max_pause_sec), DateTime.Now) < 1)
					{
						Error("Слишком долгая пауза загрузки");
					}
					else
						Debug.Log("Пауза");
				}

				if (reload)
				{
					// этот флаг снимем что бы повторно не загружать карту
					reload = false;

					StartCoroutine(HttpRequest("auth"));
				}
				if (errors.Count == 0)
				{
					// тк в процессе разбора могут появиться новые данные то обработаем только те что здесь и сейчас были
					int count = recives.Count;
					if (count > 0)
					{
						for (int i = 0; i < count && loading==null; i++)
						{
							try
							{
								Handle(recives[i]);
							}
							catch (Exception ex)
							{
								Debug.LogException(ex);
								Error("Ошибка разбора входящих данных" + recives[i] + " (" + ex.Message + ")");
							}
						}

						// и удалим только те что обработали (хотя могли прийти и новые пока обрабатвали, но это уже в следующем кадре)
						recives.RemoveRange(0, count);
					}

					if (connect != null && !reload && connect.ReadyState != WebSocketSharp.WebSocketState.Open && connect.ReadyState != WebSocketSharp.WebSocketState.Connecting)
						Error("Соединение не открыто для запросов (" + connect.ReadyState + ")");
				}
				else
				{
					coroutine = StartCoroutine(LoadRegister(String.Join(", ", errors)));
					errors.Clear();
				}	
			}
		}

		abstract protected void Handle(string json);

		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public static void Connect(SigninRecive data)
		{
			errors.Clear();
			recives.Clear();

			player_key = data.key;
			player_token = data.token;

			string address = "ws://" + data.host;
			Debug.Log("Соединяемся с сервером " + address);

			if (connect != null && (connect.ReadyState == WebSocketSharp.WebSocketState.Open || connect.ReadyState == WebSocketSharp.WebSocketState.Closing))
			{
				Error("WebSocket is already connected or is closing.");
			}

			try
			{
				connect = new WebSocket(address);

				// так в C# можно
				connect.SetCredentials("" + player_key + "", player_token, true);
				connect.OnOpen += (sender, ev) =>
				{
					Debug.Log("Соединение с севрером установлено");
				};
				connect.OnClose += (sender, ev) =>
				{
					if ((ushort)ev.Code != ((ushort)WebSocketSharp.CloseStatusCode.Normal))
					{
						// следующий Update вызовет ошибку , если мы конечно не переподключаемся и это не старое соединеник
						Debug.LogError("Соединение с сервером прервано");
					}
					else
						Debug.LogWarning("Нормальное закрытие соединения");
				};
				connect.OnError += (sender, ev) =>
				{
					Error("Ошибка соединения " + ev.Message);
				};
				connect.OnMessage += (sender, ev) =>
				{
					string text = Encoding.UTF8.GetString(ev.RawData);

					#if !UNITY_WEBGL || UNITY_EDITOR
						Debug.Log(DateTime.Now.Millisecond + ": " + text);
					#endif

					if (coroutine == null && !reload) 
					{
						Recive<ObjectRecive, ObjectRecive, ObjectRecive> recive = JsonConvert.DeserializeObject<Recive<ObjectRecive, ObjectRecive, ObjectRecive>>(text);

						if (recive.error != null)
						{
							Error(recive.error);
						}
						// эти данные нужно обработать немедленно (остальное обработается в следующем кадре) тк они связаны с открытием - закрытием соединения
						else if (recive.action == ACTION_RECONNECT)
						{
							// обнулим наше соединение что бы Close() не пытался его закрыть его сам асинхроно (а то уже при установке нового соединения может закрыть новое )
							connect = null;
							Close();

							// поставим флаг после которого на следующем кадре запустится корутина загрузки сцены (тут нельзя запускать корутину ты мы в уже в некой корутине)
							reload = true;
						}
						else
						{ 
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
								pings.Add((double)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - recive.unixtime) / 1000);

								if ((max_ping_history > 0 && pings.Count > max_ping_history) || pings.Count == 1)
								{
									ping = Math.Round((pings.Sum() / pings.Count), 3);

									if (max_ping_history > 0 && pings.Count > max_ping_history)
										pings.RemoveRange(0, pings.Count - min_ping_history);
								}
							}

							recives.Add(text);
						}
					}
				};
				connect.Connect();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Error("Ошибка октрытия соединения " + ex.Message);
			}
		}

		/// <summary>
		/// не отправляется моментально а ставиться в очередь на отправку (перезаписывает текущю). придет время - отправится на сервер (может чуть раньше если пинг большой)
		/// </summary>
		public void Send(Response data)
		{
			// если нет паузы или мы загружаем иир и не ждем предыдущей загрузки
			if (player != null && loading == null && connect!=null && !reload)
			{
				try
				{
					double ping = Ping();
					double remain = player.GetEventRemain(data.group) - (Ping() / 2); // вычтем время необходимое что бы ответ ошел до сервера (половину таймаута.тем самым слать мы можем раньше запрос чем закончится анимация)

					// Здесь интерполяция - проверим можем ли отправить мы эту команду сейчас, отнимем от времени окончания паузы события половина пинга которое будет затрачено на доставку от нас ответа серверу
					// так же мы даем возможность слать запрос повторно если события генерируются сервером и мы хотим их сбросить
					if (remain <= 0 || player.getEvent(data.group).is_client != true)
					{
						// поставим на паузу отправку и получение любых кроме данной команды данных
						if (data.group == LoadResponse.GROUP)
						{
							loading = DateTime.Now;
						}

						if (ping > 0 && ping != last_send_ping)
						{
							data.ping = last_send_ping = ping;
						}

                        // создадим условно уникальный номер нашего сообщения (она же и временная метка) для того что бы сервер вернул ее (вычив время сколько она была на сервере) и получим пинг
                        if (DateTime.Compare(last_ping_request, DateTime.Now) < 1)
                        {
							data.unixtime = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
							last_ping_request = DateTime.Now.AddSeconds(ping_request_sec);
						}

						// по умолчанию ACTION не оптравляем дабы не увеличивать пакет (на сервере по умолчанию подставит)
						if (data.action == Response.DEFAULT_ACTION) data.action = null;

						string json = JsonConvert.SerializeObject(
							data
							,
							Newtonsoft.Json.Formatting.None
							,
							new JsonSerializerSettings { Converters = { new NewJsonConverter() }, NullValueHandling = NullValueHandling.Ignore }
						);

						SetTimeout(data.group);

						// сразу пометим что текущее событие нами было выслано 
						player.getEvent(data.group).is_client = true;

						Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
						Put2Send(json);	
					}
					//else
					//	Debug.LogError("Слишком частый вызов команды " + data.group + " (" + remain + " секунд осталось)");
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					errors.Add("Ошибка отправки данных: "+ex.Message);
				}		
			}
			else
				Debug.LogWarning("Загрузка мира, команда " + data.action +"/"+ data.group + " отклонена");
		}

		public static double Ping()
		{
			return ping;
		}

		/// <summary>
		/// утановить таймаут не дожидаясь ответа от сервера на оснвое имещихся данных (при возврата с сервера данных таймаут будет пересчитан)
		/// </summary>
		private void SetTimeout(string group)
		{
			// поставим примерно време когда наступит таймаут (с овтетом он нам более точно скажет тк таймаут может и плавающий в механике быть)
			double timeout = player.getEvent(group).timeout ?? 5;
			player.getEvent(group).finish = DateTime.Now.AddSeconds(timeout + (Ping() / 2));
		}

		private static void Close()
		{	
			// что бы игрок более не мог посылать команды
			player = null;

			// поставим этот флаг что бы был таймер нашей загрузки новой карты и текущаа обработка в Update остановилась
			loading = DateTime.Now;

			if (connect != null)
			{
				if (connect.ReadyState != WebSocketSharp.WebSocketState.Closed && connect.ReadyState != WebSocketSharp.WebSocketState.Closing)
					connect.CloseAsync(WebSocketSharp.CloseStatusCode.Normal);

				connect = null;
			}
			Debug.LogWarning("Закрытие соединения вручную");
		}

		private void Put2Send(string json)
		{
			byte[] sendBytes = Encoding.UTF8.GetBytes(json);
			connect.Send(sendBytes);
		}

		public static new void Error (string text)
		{
			errors.Add(text);
			Close();

			Debug.LogError(text);
			throw new Exception(text);
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

#if UNITY_WEBGL && !UNITY_EDITOR
		public void Api(string json)
		{
			Put2Send(json);
		}
#endif

		void OnApplicationQuit()
		{
			Debug.Log("Закрытие приложения");
			Close();
		}
	}
}