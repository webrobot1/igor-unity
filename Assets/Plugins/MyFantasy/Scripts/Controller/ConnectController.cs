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
		private WebSocket connect;

		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		protected List<string> recives = new List<string>();		
		
		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private List<string> errors = new List<string>();			

		/// <summary>
		/// пауза если не null (может быть как первая загрузка мира, так и перезаход в игру когда может прийти ошибка о закрытие старого соединения)
		/// </summary>
		private DateTime? loading = DateTime.Now;

		private double last_ping = 0;

		/// <summary>
		/// максимальное количество секунд системной паузы
		/// </summary>
		private const int PAUSE_SECONDS = 10;

		/// <summary>
		/// среднее значение пинга.
		/// </summary>
		private static double ping = 0;
		
		/// <summary>
		/// сопрограммы могут менять коллекцию pings и однойременное чтение из нее невозможно, поэтому делаем фиксированное поле ping со значением которое будетп еерсчитываться
		/// </summary>
		private List<double> pings = new List<double>();

		/// <summary>
		/// Проверка наличие новых данных или ошибок соединения
		/// </summary>
		protected virtual void Update()
		{
			if (loading != null)
			{
				if (DateTime.Compare(((DateTime)loading).AddSeconds(PAUSE_SECONDS), DateTime.Now) < 1)
				{
					loading = null;
					Error("Слишком долгая системная пауза загрузки");
				}
				else
					Debug.Log("Пауза");
			}

			if (errors.Count > 0)
			{
				if (loading == null) StartCoroutine(LoadRegister());
			}
			else
            {
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
							Error("Ошибка разбора входящих данных" + ex.Message);
							break;
						}
					}

					// и удалим только те что обработали (хотя могли прийти и новые пока обрабатвали, но это уже в следующем кадре)
					recives.RemoveRange(0, count);
				}
			}		
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
					if (ev.Code != ((ushort)CloseStatusCode.Normal))
					{
						if(connect!=null)
							Error("Соединение с сервером закрыто " + ev.Code + "/" + ev.Reason);
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

					try
					{
						// эти данные нужно обработать немедленно тк они связаны с открытием - закрытием соединения
						Recive<ObjectRecive, ObjectRecive, ObjectRecive> recive = JsonConvert.DeserializeObject<Recive<ObjectRecive, ObjectRecive, ObjectRecive>>(text);

						if (recive.action == "load/reconnect")
						{
							player = null;
							connect = null;
						}						
						else if (recive.action == "load/index")
						{
							loading = null;
						}

						if (recive.error.Length > 0)
						{
							errors.Add(recive.error);
							loading = null;
						}						
						
						if (recive.pings!=null)
						{
							foreach (PingRecive _ping in recive.pings)
							{
								if (pings.Count > 10)
								{
									pings.RemoveRange(0, 5);
									ping = (pings.Count > 0 ? Math.Round((pings.Sum() / pings.Count), 3) : 0);
								}

								pings.Add((double)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - _ping.command_id) / 1000 - _ping.wait_time);
							}
						}

						recives.Add(text);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Error("Ошибка добавления данных websocket "+ ex.Message);
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
			if (loading == null)
			{
				if (connect!=null && (connect.ReadyState == WebSocketSharp.WebSocketState.Open || connect.ReadyState == WebSocketSharp.WebSocketState.Connecting))
				{
					try
					{
						// поставим на паузу отправку и получение любых кроме данной команды данных
						if (data.action == "load/index")
						{
							// актуально когда после разрыва соединения возвращаемся
							recives.Clear();
							loading = DateTime.Now;
						}

						double ping = Ping();
						double remain = player.GetEventRemain(data.group()) - (Ping() / 2); // вычтем время необходимое что бы ответ ошел до сервера (половину таймаута.тем самым слать мы можем раньше запрос чем закончится анимация)

						// проверим можем ли отправить мы эту команду сейчас, отнимем от времени окончания паузы события половина пинга которое будет затрачено на доставку от нас ответа серверу
						if (remain <= 0)
						{
							if (ping > 0 && ping!=last_ping)
							{
								data.ping = last_ping = ping;
							}

							// создадим условно уникальный номер нашего сообщения (она же и временная метка)
							data.command_id = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();

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

							SetTimeout(data.group());

							Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
							Put2Send(json);	
						}
						else
							Debug.LogError("Слишком частый вызов команды " + data.group() + " (" + remain + " секунд осталось)");
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Error("Ошибка отправки данных: "+ex.Message);
					}
				}
				else
					Error("Соединение не открыто для запросов");
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
					connect.Close(CloseStatusCode.Normal);

				connect = null;
			}
			player = null;
			loading = DateTime.Now;
		}

		private void Put2Send(string json)
		{
			byte[] sendBytes = Encoding.UTF8.GetBytes(json);
			connect.Send(sendBytes);
		}

		public override void Error (string text)
		{
			errors.Add(text);
			loading = null;

			Debug.LogError(text);
			throw new Exception(text);
		}

		/// <summary>
		/// Страница ошибок - загрузка страницы входа
		/// </summary>
		/// <param name="error">сама ошибка</param>
		private IEnumerator LoadRegister()
		{
			if (loading != null)
			{
				Debug.LogWarning("уже закрываем игру");
				yield break;
			}
			else
			{
				Close();
			}
			
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

			Camera.main.GetComponent<BaseController>().Error(String.Join(", ", errors));
		}

#if !UNITY_EDITOR && (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS)
		// повторная загрузка всего пира по новой при переключении между вкладками браузера
		// если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
		// TODO придумать как отказаться от этого
		private void Load()
		{
			if (connect == null || loading != null) return;

			Response response = new Response();
			response.action = "load/index";

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