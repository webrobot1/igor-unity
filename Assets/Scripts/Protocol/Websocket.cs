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
	/// если это поле не пустое то запускается загрузка странца входа и выводится ошибка из данного поля
	/// </summary>
	public string error = "";

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


	public Websocket(int map_id, float command_pause)
	{
		Debug.Log("Соединяемся с сервером "+ MainController.SERVER);

		if (this.ws != null && (this.ws.ReadyState == WebSocketSharp.WebSocketState.Open || this.ws.ReadyState == WebSocketSharp.WebSocketState.Closing))
		{
			error = "WebSocket is already connected or is closing.";
			return;
		}

		try
		{
			ws = new WebSocket("ws://"+MainController.SERVER+":"+(MainController.PORT + map_id));
			ws.OnOpen += (sender, ev) =>
			{
				Debug.Log("Соединение с севрером установлено");

				// добавим единсвенную пока доступную команду на отправку данных
				commands.timeouts["load"] = new TimeoutRecive();

			};
			ws.OnClose += (sender, ev) =>
			{
				error = "Соединение с сервером закрыто (" + ev.Code + ")";
			};
			ws.OnMessage += (sender, ev) =>
			{
				string text = Encoding.UTF8.GetString(ev.RawData);
				Debug.Log(DateTime.Now.Millisecond + ": " + text);

				if (error.Length == 0)
				{
					try
					{
						Recive recive = JsonConvert.DeserializeObject<Recive>(text);

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
						error = "Ошибка разбора данных websocket " + ex.Message;
					}
				}
			};
			ws.OnError += (sender, ev) =>
			{
				error = "Ошибка соединения " + ev.Message;
			};
			ws.Connect();

			this.command_pause = command_pause;
		}
		catch (Exception ex)
		{
			error = "Ошибка соединения " + ex.Message;
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
		if (!ConnectController.pause)
		{
			if (ws == null || (ws.ReadyState != WebSocketSharp.WebSocketState.Open && ws.ReadyState != WebSocketSharp.WebSocketState.Connecting))
			{
				error = "Соединение не открыто для запросов ";
				return;
			}

			// прверим есть ли вообще группа команд этих (мы таймауты собрали словарь из всех доступных команд)
			if (commands.timeouts.ContainsKey(data.group()))
			{
				long command_id = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();

				// что бы небыло дабл кликов выдерживыем некую паузу между запросами и
				if (commands.timeouts[data.group()].requests.Count == 0 || command_id - commands.timeouts[data.group()].requests.Last()>command_pause*1000)
				{
					double wait = commands.timeouts[data.group()].timeout + commands.ping();
					if (commands.timeouts[data.group()].requests.Count > 0)
                    {
						foreach (long value in commands.timeouts[data.group()].requests)
						{
							float last = (command_id - value) / 1000;
							if (last > wait)
							{
								Debug.LogError("Слишком должго ждали ответа команды " + value + ": " + last);
								commands.timeouts[data.group()].requests.Remove(value);
							}
						}
					}

					//если уже очередь есть 2 команды далее не даем слать запроса пока непридет ответ(это TCP тут они гарантировано придут) тк вторая заранее поставит в очередь следующую и 3й+ не надо
					if (commands.timeouts[data.group()].requests.Count<2)
					{
						Debug.LogWarning(commands.timeouts[data.group()].requests.Count);

						// создадим условно уникальный номер нашего сообщения (она же и временная метка)
						data.command_id =  command_id;
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
					}
				}
			}
			else
				error = "неизвестная команда " + data.group();
		}
	}

	public void Put(string json) 
	{	
		byte[] sendBytes = Encoding.UTF8.GetBytes(json);
		ws.Send(sendBytes);
	}
}