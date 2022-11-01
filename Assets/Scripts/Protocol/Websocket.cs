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

	public  float ping = 0;

	/// <summary>
	/// если это поле не пустое то запускается загрузка странца входа и выводится ошибка из данного поля
	/// </summary>
	public string error;

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
	private Dictionary<string, CommandModel> commands = new Dictionary<string, CommandModel>();


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
				commands["load/index"] = new CommandModel();

			};
			ws.OnClose += (sender, ev) =>
			{
				error = "Соединение с сервером закрыто (" + ev.Code + ")";
			};
			ws.OnMessage += (sender, ev) =>
			{
				string text = Encoding.UTF8.GetString(ev.RawData);
				Debug.Log(DateTime.Now.Millisecond + ": " + text);

				Recive recive = JsonConvert.DeserializeObject<Recive>(text);

				if (recive.timeouts.Count > 0)
				{
					Debug.Log("Обновляем таймауты");

					foreach (KeyValuePair<string, float> kvp in recive.timeouts)
					{
						if (!commands.ContainsKey(kvp.Key))
							commands[kvp.Key] = new CommandModel();

						commands[kvp.Key].timeout = kvp.Value;
					}

					recive.timeouts.Clear();
				}				
				
				if (recive.pings.Count > 0)
				{
					Debug.Log("Обновляем пинги");

					foreach (KeyValuePair<string, PingsRecive> kvp in recive.pings)
					{
						if (!commands.ContainsKey(kvp.Key))
                        {
							error = "Пинга на насуществующую команду " + kvp.Key;
							return;
						}
						
						commands[kvp.Key].check(kvp.Value);
						Debug.Log("Ping команды "+ kvp.Key + "составил " + commands[kvp.Key].ping);
					}

					recive.pings.Clear();
				}


				recives.Add(recive);
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

			// либо загружаем игру (а там и все доступные команды) либо команда должна у нас быть указана
			if (commands.ContainsKey(data.action))
			{
				// что бы небыло дабл кликов выдерживыем некую паузу между запросами
			
				if (DateTime.Compare(commands[data.action].time, DateTime.Now) < 1) 
				{
					commands[data.action].time = DateTime.Now.AddSeconds(command_pause);

					// создадим условно уникальный номер нашего сообщения (она же и временная метка)
					data.command_id = (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
					commands[data.action].requests[data.command_id] = data.command_id;
					
					// если подсчитан пинг то передаем его с запросом нашей команды
					if (ping > 0)
                    {
						data.ping = ping;
						ping = 0;
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

					Debug.Log(DateTime.Now.Millisecond + " Отправили серверу (" + commands[data.action].ping + "/" + commands[data.action].work_time + ") " + json);
					Put(json);
				}
			}
			else
				error = "неизвестная команда";
		}
	}

	public void Put(string json) 
	{	
		byte[] sendBytes = Encoding.UTF8.GetBytes(json);
		ws.Send(sendBytes);
	}
}