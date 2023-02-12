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
		/// Префаб нашего игрока
		/// </summary>
		[NonSerialized]
		public ObjectModel player;

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (что бы player наполнить и что бы индентифицироваться в StatModel что обрабатываем нашего игрока)
		/// </summary>
		public string player_key;

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
		/// список таймаутов по умолчанию
		/// </summary>
		[NonSerialized]
		public static Dictionary<string, float> timeouts = new Dictionary<string, float>();		
		
		/// <summary>
		/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
		/// </summary>
		private List<string> errors = new List<string>();

		/// <summary>
		/// пауза если не null (может быть как первая загрузка мира, так и перезаход в игру когда может прийти ошибка о закрытие старого соединения)
		/// </summary>
		private DateTime? loading = DateTime.Now;

		/// <summary>
		/// максимальное количество секунд системной паузы
		/// </summary>
		private const int PAUSE_SECONDS = 10;
		
		[SerializeField]
		private Text ping;


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
				Handle();
		}


		abstract protected void Handle();


		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public virtual void Connect(SigninRecive data)
		{
			errors.Clear();
			recives.Clear();

			this.player_key = data.key;
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
						// это хозйство относится к пингам и таймингам, не хочу обрабатывать это в UpdateController
						Recive<PlayerRecive, EnemyRecive, ObjectRecive> recive = JsonConvert.DeserializeObject<Recive<PlayerRecive, EnemyRecive, ObjectRecive>>(text);
						if (recive.action == "load/reconnect")
						{
							player = null;
							connect = null;
						}						
						else if (recive.action == "load/index")
						{
							loading = null;
						}

                        if (recive.timeouts!=null)
                        {
							foreach (KeyValuePair<string, float> kvp in recive.timeouts)
							{
								if (!timeouts.ContainsKey(kvp.Key))
									timeouts.Add(kvp.Key, kvp.Value);
								else
									timeouts[kvp.Key] = kvp.Value;
							}
						}

						recives.Add(text);
					}
					catch (Exception ex)
					{
						Error("Ошибка добавления данных websocket "+ ex.Message);
					}
				};
				connect.Connect();
			}
			catch (Exception ex)
			{
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
					// поставим на паузу отправку и получение любых кроме данной команды данных
					if (data.action == "load/index")
					{
						// актуально когда после разрыва соединения возвращаемся
						recives.Clear();
						loading = DateTime.Now;
					}

					double ping = player.Ping();
					double timeout = player.GetTimeout(data.group()) - (ping / 2);
						
					// проверим можем ли отправить мы эту команду сейчас, отнимем от времени окончания паузы события половина пинга которое будет затрачено на доставку от нас ответа серверу
					if (timeout <= 0)
					{
						if (ping > 0)
						{
							data.ping = ping;
							if (this.ping != null)
								this.ping.text = (int)(1 / ping) + " RPS";
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

						Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
						Put2Send(json);

						player.SetTimeout(data.group());
					}
					else
						Debug.LogError("Слишком частый вызов команды " + data.group()+" ("+ timeout + " секунд осталось)");
				}
				else
					Error("Соединение не открыто для запросов");
			}
			else
				Debug.LogWarning("Загрузка мира, команда " + data.action+" отклонена");
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