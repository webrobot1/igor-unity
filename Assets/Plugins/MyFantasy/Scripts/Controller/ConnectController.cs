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
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (нужен только между методом Sign и Load)
		/// </summary>
		protected string player_key;

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (нужен только между методом Sign и Load)
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
		/// true - пауза (выходим, входим или перезагружаем мир игры)
		/// </summary>
		protected DateTime? loading = DateTime.Now;

		/// <summary>
		/// время таймаутов (для вывода игроку статистики)
		/// </summary>
		protected CommandModel commands = new CommandModel();

		/// <summary>
		/// максимальное количество секунд системной паузы
		/// </summary>
		private const float COMMAND_PAUSE_SECONDS = 0.15f;

		/// <summary>
		/// Звпускается после авторизации - заполяет id и token 
		/// </summary>
		/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
		public void Connect(SigninRecive data)
		{
			recives.Clear();

			this.player_key = data.key;
			this.player_token = data.token;

			string address = "ws://" + data.host;
			Debug.Log("Соединяемся с сервером " + address);

			if (this.connect != null && (this.connect.ReadyState == WebSocketSharp.WebSocketState.Open || this.connect.ReadyState == WebSocketSharp.WebSocketState.Closing))
			{
				Error("WebSocket is already connected or is closing.");
				return;
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
					connect = null;
					Error("Соединение с сервером закрыто");
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
						recives.Add(text);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Error("Ошибка добавления данных websocket");
					}
				};
				connect.Connect();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Error("Ошибка октрытия соединения");
			}
		}

		public void Close()
		{
			recives.Clear();
			if (connect != null && connect.ReadyState != WebSocketSharp.WebSocketState.Closed && connect.ReadyState != WebSocketSharp.WebSocketState.Closing)
			{
				connect.Close();
				Debug.LogError("Соединение закрыто");
			}
		}

		/// <summary>
		/// не отправляется моментально а ставиться в очередь на отправку (перезаписывает текущю). придет время - отправится на сервер (может чуть раньше если пинг большой)
		/// </summary>
		public void Send(Response data)
		{
			if (connect == null || (connect.ReadyState != WebSocketSharp.WebSocketState.Open && connect.ReadyState != WebSocketSharp.WebSocketState.Connecting))
			{
				Error("Соединение не открыто для запросов");
			}

			// если нет паузы или мы загружаем иир и не ждем предыдущей загрузки
			else if (loading == null)
			{
				// поставим на паузу отправку и получение любых кроме данной команды данных
				if (data.action == "load/index")
				{
					// актуально когда после разрыва соединения возвращаемся
					recives.Clear();
					loading = DateTime.Now;
				}

				// проверим можем ли отправить мы эту команду сейчас.  и если можем доадим есть временную метку
				if (commands.timeouts.ContainsKey(data.group()))
				{
					if (commands.timeouts[data.group()].actions.ContainsKey(data.method()))
					{
						// что бы небыло дабл кликов выдерживыем некую паузу между запросами и
						if (commands.timeouts[data.group()].time == null || DateTime.Compare(((DateTime)commands.timeouts[data.group()].time).AddSeconds(Math.Min(commands.timeouts[data.group()].timeout, COMMAND_PAUSE_SECONDS)), DateTime.Now) < 0)
						{
							long command_id = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();

							if (commands.timeouts[data.group()].requests.Count > 0)
							{
								double wait = commands.timeouts[data.group()].timeout + commands.ping();

								// проверим может какие то старые команды там и пора удалить их
								foreach (long kvp in commands.timeouts[data.group()].requests.ToList())
								{
									float last = (float)(command_id - kvp) / 1000;
									if (last > wait)
									{
										Debug.LogError(DateTime.Now.Millisecond + "Слишком должго ждали ответа команды " + kvp + ": " + last);
										commands.timeouts[data.group()].requests.Remove(kvp);
									}
								}
							}

							//если уже очередь есть 2 команды далее не даем слать запроса пока непридет ответ(это TCP тут они гарантировано придут) тк вторая заранее поставит в очередь следующую и 3й+ не надо
							if (commands.timeouts[data.group()].requests.Count < 2)
							{
								//Debug.LogWarning(commands.timeouts[data.group()].requests.Count);

								// создадим условно уникальный номер нашего сообщения (она же и временная метка)
								data.command_id = command_id;
								commands.timeouts[data.group()].requests.Add(command_id);

								// если подсчитан пинг то передаем его с запросом нашей команды
								if (commands.pings.Count > 10)
								{
									data.ping = commands.ping();
									commands.pings.RemoveRange(0, 5);
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

								Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
								Put2Send(json);

								commands.timeouts[data.group()].time = DateTime.Now;
							}
						}
					}
					else
						Error("нельзя напрямую вызвать " + data.action);
				}
			}
		}

		private void Put2Send(string json)
		{
			byte[] sendBytes = Encoding.UTF8.GetBytes(json);
			connect.Send(sendBytes);
		}

		public override void Error (string text)
		{
			StartCoroutine(LoadRegister(text));

			throw new Exception(text);
		}

		/// <summary>
		/// Страница ошибок - загрузка страницы входа
		/// </summary>
		/// <param name="error">сама ошибка</param>
		private IEnumerator LoadRegister(string error = "")
		{
			if (connect == null)
			{
				Debug.LogWarning("уже закрываем игру");
				yield break;
			}
			else
			{
				connect.Close();
				connect = null;
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
				connect.Close();
		}
	}
}