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
		protected string player_token;

		/// <summary>
		/// Ссылка на конектор
		/// </summary>
		protected WebSocket connect;

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private List<string> recives = new List<string>();

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private List<string> errors = new List<string>();			

		/// <summary>
		/// блокирует отправку любых запросов на сервер (тк уже идет соединение)
		/// </summary>
		protected DateTime? loading = DateTime.Now;

		/// <summary>
		/// последний отправленный пинг на сервер
		/// </summary>
		private double last_send_ping = 0;

		/// <summary>
		/// максимальное количество секунд паузы между загрузками
		/// </summary>
		[SerializeField]
		private int max_pause_sec = 10;		
		
		/// <summary>
		/// через сколько секунд мы отправляем на серер запрос для анализа пинга
		/// </summary>
		[SerializeField]
		private double ping_request_sec = 3;

		/// <summary>
		/// время последнего запроса пинга на сервер
		/// </summary>
		private DateTime last_ping_request = DateTime.Now;

		/// <summary>
		/// среднее значение пинга (времени нужное для доставки пакета на сервере и возврата назад. вычитая половину, время на доставку, мы можем слать запросы чуть раньше их времени таймаута)
		/// </summary>
		protected static double ping = 0;
		
		protected Coroutine coroutine = null;

		/// <summary>
		/// Проверка наличие новых данных или ошибок соединения
		/// </summary>
		protected virtual void Update()
		{
			if (loading != null)
			{
				if (DateTime.Compare(((DateTime)loading).AddSeconds(max_pause_sec), DateTime.Now) < 1 && coroutine == null)
				{
					coroutine = StartCoroutine(LoadRegister(String.Join(", ", errors)));
				}
				else
					Debug.Log("Пауза");
			}

			// тк в процессе разбора могут появиться новые данные то обработаем только те что здесь и сейчас были
			int count = recives.Count;
			if (count > 0)
			{
				for (int i = 0; i < count; i++)
				{
					try
					{
						Handle(recives[i]);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Error("Ошибка разбора входящих данных" + recives[i]+ " ("+ex.Message+")");
						break;
					}
				}

				// и удалим только те что обработали (хотя могли прийти и новые пока обрабатвали, но это уже в следующем кадре)
				recives.RemoveRange(0, count);
			}

			if (errors.Count > 0 && coroutine == null)
			{
				coroutine = StartCoroutine(LoadRegister(String.Join(", ", errors)));
				errors.Clear();
			}

			else if (coroutine == null && loading == null && connect != null && connect.ReadyState != WebSocketSharp.WebSocketState.Open && connect.ReadyState != WebSocketSharp.WebSocketState.Connecting)
				coroutine = StartCoroutine(LoadRegister("Соединение не открыто для запросов"));
		}

		abstract protected void Handle(string json);


		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public virtual void Connect(SigninRecive data)
		{
			errors.Clear();
			recives.Clear();

			player_key = data.key;
			this.player_token = data.token;

			string address = "ws://" + data.host;
			Debug.Log("Соединяемся с сервером " + address);

			if (this.connect != null && (this.connect.ReadyState == WebSocketSharp.WebSocketState.Open || this.connect.ReadyState == WebSocketSharp.WebSocketState.Closing))
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
						loading = null;
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
					Debug.Log(DateTime.Now.Millisecond + ": " + text);

					recives.Add(text);
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
			if (player != null && loading == null)
			{
				try
				{
					if(data.group == null)
                    {
						Error("Не указана группа событий в запросе");
					}

					// поставим на паузу отправку и получение любых кроме данной команды данных
					if (data.action == "load/index")
					{
						// актуально когда после разрыва соединения возвращаемся
						recives.Clear();
						loading = DateTime.Now;
					}

					double ping = Ping();
					double remain = player.GetEventRemain(data.group) - (Ping() / 2); // вычтем время необходимое что бы ответ ошел до сервера (половину таймаута.тем самым слать мы можем раньше запрос чем закончится анимация)

					// Здесь интерполяция - проверим можем ли отправить мы эту команду сейчас, отнимем от времени окончания паузы события половина пинга которое будет затрачено на доставку от нас ответа серверу
					if (remain <= 0)
					{
						if (ping > 0 && ping != last_send_ping)
						{
							data.ping = last_send_ping = ping;
						}

                        // создадим условно уникальный номер нашего сообщения (она же и временная метка) для того что бы сервер вернул ее (вычив время сколько она была на сервере) и получим пинг
                        if (DateTime.Compare(last_ping_request, DateTime.Now) <= 1)
                        {
							data.unixtime = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
							last_ping_request = DateTime.Now.AddSeconds(ping_request_sec);
						}		

						string json = JsonConvert.SerializeObject(
							data
							,
							Newtonsoft.Json.Formatting.None
							,
							new JsonSerializerSettings
							{
								NullValueHandling = NullValueHandling.Ignore
							}
						);

						SetTimeout(data.group);

						Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
						Put2Send(json);	
					}
					//else
					//	Debug.LogError("Слишком частый вызов команды " + data.group + " (" + remain + " секунд осталось)");
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					Error("Ошибка отправки данных: "+ex.Message);
				}		
			}
			else
				Debug.LogWarning("Загрузка мира, команда " + data.action+" отклонена");
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

		private void Close()
		{	
			Debug.LogWarning("Закрытие соединения вручную");
			
			recives.Clear();
			if (connect != null)
			{
				if (connect.ReadyState != WebSocketSharp.WebSocketState.Closed && connect.ReadyState != WebSocketSharp.WebSocketState.Closing)
					connect.Close(WebSocketSharp.CloseStatusCode.Normal);

				connect = null;
			}
			player = null;
		}

		private void Put2Send(string json)
		{
			byte[] sendBytes = Encoding.UTF8.GetBytes(json);
			connect.Send(sendBytes);
		}

		public override void Error (string text)
		{
			errors.Add(text);
			player = null;

			Debug.LogError(text);
			throw new Exception(text);
		}

		/// <summary>
		/// Страница ошибок - загрузка страницы входа
		/// </summary>
		/// <param name="error">сама ошибка</param>
		protected IEnumerator LoadRegister(string error)
		{
			Debug.LogWarning("загружаем сцену регистрации");
			Close();

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

			Camera.main.GetComponent<BaseController>().Error(error);
		}

#if !UNITY_EDITOR && (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS)
		// повторная загрузка всего пира по новой при переключении между вкладками браузера
		// если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
		// TODO придумать как отказаться от этого
		private void Load()
		{
			if (connect == null || loading != null) return;

			Response response = new Response();
			response.group = "load";

			Send(response);
		}
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
		public void OnApplicationPause(bool pause)
		{
			Debug.Log("Пауза " + pause);
			Load();
		}
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
		public void OnApplicationFocus(bool focus)
		{
			Debug.Log("фокус " + focus);
			Load();
		}

		public void Api(string json)
		{
			Put2Send(json);
		}
#endif

		void OnApplicationQuit()
		{
			Debug.Log("Закрытие приложения");

			if(connect!=null)
				Close();
		}
	}
}