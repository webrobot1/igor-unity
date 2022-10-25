using System;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
	public string error;

	/// <summary>
	/// список полученных от сервера данных (по мере игры они отсюда будут забираться)
	/// </summary>
	public List<string> recives = new List<string>();	
	
	/// <summary>
	/// список всех возможных отправок команд с их пингами, таймаутами и подготовленными запросами
	/// </summary>
	public Dictionary<string, PingsRecive> pings = new Dictionary<string, PingsRecive>();

	public Websocket(string server, int port, int map_id)
	{
		Debug.Log("Соединяемся с сервером");

		if (this.ws != null && (this.ws.ReadyState == WebSocketSharp.WebSocketState.Open || this.ws.ReadyState == WebSocketSharp.WebSocketState.Closing))
		{
			error = "WebSocket is already connected or is closing.";
			return;
		}

		try
		{
			ws = new WebSocket("ws://"+server+":"+(port + map_id));
			ws.OnOpen += (sender, ev) =>
			{
				Debug.Log("Соединение с севрером установлено");
			};
			ws.OnClose += (sender, ev) =>
			{
				error = "Соединение с сервером закрыто (" + ev.Code + ")";
			};
			ws.OnMessage += (sender, ev) =>
			{
				recives.Add(Encoding.UTF8.GetString(ev.RawData));
			};
			ws.OnError += (sender, ev) =>
			{
				error = "Ошибка соединения " + ev.Message;
			};
			ws.Connect();

			// добавим единсвенную пока доступную команду на отправку
			pings["load/index"] = new PingsRecive();
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

			Debug.Log(DateTime.Now.Millisecond + " Отправили серверу (" + pings[data.action].ping + "/" + pings[data.action].work + ") " + json);
			Put(json);
		}
	}

	public void Put(string json) 
	{	
		byte[] sendBytes = Encoding.UTF8.GetBytes(json);
		ws.Send(sendBytes);
	}
}