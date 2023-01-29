using System;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

#if UNITY_WEBGL && !UNITY_EDITOR
	using WebGLWebsocket;
#else
using WebSocketSharp;
#endif

using Newtonsoft.Json;

public class Websocket 
{
	private WebSocket ws;

	/// <summary>
	/// true - пауза (выходим, входим или перезагружаем мир игры)
	/// </summary>
	public bool pause = true;	
	
	/// <summary>
	/// список ошибок (в разном порядке могут прийти Закрытие соединение и...реальная причина)
	/// </summary>
	public static List<string> errors = new List<string>();

	/// <summary>
	/// Время между нажатиями кнопок команд серверу
	/// </summary>
	public float command_pause;

	/// <summary>
	/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
	/// </summary>
	public List<Recive> recives = new List<Recive>();

	/// <summary>
	/// время таймаутов (для вывода игроку статистики)
	/// </summary>
	private CommandModel commands = new CommandModel();

	/// <summary>
	/// Открытие TCP соединения
	/// </summary>
	/// <param name="command_pause">Пауза в секундах между командами для предотвращения даблкликов</param>
	public Websocket(string host, int player_id, string token, float command_pause = 0.15f)
	{
		string address = "ws://" + host;
		
		Debug.Log("Соединяемся с сервером "+ address);

		// добавим единсвенную пока доступную команду на отправку данных
		commands.timeouts["load"] = new TimeoutRecive();
		commands.timeouts["load"].actions["index"] = true;


		if (this.ws != null && (this.ws.ReadyState == WebSocketSharp.WebSocketState.Open || this.ws.ReadyState == WebSocketSharp.WebSocketState.Closing))
		{
			errors.Add("WebSocket is already connected or is closing.");
			return;
		}

		try
		{
			ws = new WebSocket(address);

			// так в C# можно
			ws.SetCredentials(""+player_id+"", token, true);
			ws.OnOpen += (sender, ev) =>
			{
				Debug.Log("Соединение с севрером установлено");
			};
			ws.OnClose += (sender, ev) =>
			{
				ws = null; 
				errors.Add("Соединение с сервером закрыто (" + ev.Code + ")");
			};
			ws.OnError += (sender, ev) =>
			{
				errors.Add("Ошибка соединения " + ev.Message);
			};
			ws.OnMessage += (sender, ev) =>
			{
				string text = Encoding.UTF8.GetString(ev.RawData);
				Debug.Log(DateTime.Now.Millisecond + ": " + text);

				if (errors.Count == 0)
				{
					try
					{
						Recive recive = JsonConvert.DeserializeObject<Recive>(text);

						if (recive.error.Length > 0)
						{
							errors.Add(recive.error);
							pause = true;
						}

						if (errors.Count == 0 && recive.action == "load/index") 
						{  
							pause = false;
						}
						else 
							if (pause) return;

						if (recive.timeouts.Count > 0)
						{
							Debug.Log("Обновляем таймауты");

							foreach (KeyValuePair<string, TimeoutRecive> kvp in recive.timeouts)
							{
								if (!commands.timeouts.ContainsKey(kvp.Key))
									commands.timeouts[kvp.Key] = kvp.Value;
								else
									commands.timeouts[kvp.Key].timeout = kvp.Value.timeout;
							}

							recive.timeouts.Clear();
						}

						if (recive.commands.Count > 0)
						{
							Debug.Log("Обновляем пинги");

							foreach (KeyValuePair<string, CommandRecive> kvp in recive.commands)
							{
								commands.check(kvp.Key, kvp.Value);
							}

							recive.commands.Clear();
						}

						recives.Add(recive);
					}
					catch (Exception ex)
					{
						errors.Add("Ошибка разбора данных websocket " + ex.Message);
					}
				}
			};
			ws.Connect();

			this.command_pause = command_pause;
		}
		catch (Exception ex)
		{
			errors.Add("Ошибка октрытия соединения " + ex.Message);
		}
	}	
	
	public void Close()
    {
		recives.Clear();

		if (ws!=null && ws.ReadyState != WebSocketSharp.WebSocketState.Closed && ws.ReadyState != WebSocketSharp.WebSocketState.Closing)
        {
			ws.Close();
            Debug.LogError("Соединение закрыто");
        }
    }

	/// <summary>
	/// не отправляется моментально а ставиться в очередь на отправку (перезаписывает текущю). придет время - отправится на сервер (может чуть раньше если пинг большой)
	/// </summary>
	public void Send(Response data)
	{
		if (errors.Count == 0)
		{		
			// если нет паузы или мы загружаем иир и не ждем предыдущей загрузки
			if (!pause)
			{
				if (ws == null || (ws.ReadyState != WebSocketSharp.WebSocketState.Open && ws.ReadyState != WebSocketSharp.WebSocketState.Connecting))
				{
					errors.Add("Соединение не открыто для запросов");
					return;
				}

				// поставим на паузу отправку и получение любых кроме данной команды данных
				if (data.action == "load/index")
				{
					// актуально когда после разрыва соединения возвращаемся
					recives.Clear();
					pause = true;
				}

				// прверим есть ли вообще группа команд этих (мы таймауты собрали словарь из всех доступных команд)
				if (commands.timeouts.ContainsKey(data.group()) && commands.timeouts[data.group()].actions.ContainsKey(data.method()))
				{
					if(commands.timeouts[data.group()].actions[data.method()])
                    {
						// что бы небыло дабл кликов выдерживыем некую паузу между запросами и
						if (commands.timeouts[data.group()].time == null || DateTime.Compare(((DateTime)commands.timeouts[data.group()].time).AddSeconds(Math.Min(commands.timeouts[data.group()].timeout, command_pause)), DateTime.Now) < 0)
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
								Debug.LogWarning(commands.timeouts[data.group()].requests.Count);

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
								Put(json);

								commands.timeouts[data.group()].time = DateTime.Now;
							}
						}
					}
					else
						errors.Add("нельзя напрямую вызвать " + data.action);
				}
			}
		}
	}

	public void Put(string json) 
	{
		byte[] sendBytes = Encoding.UTF8.GetBytes(json);
		ws.Send(sendBytes);
	}
}