using System;
using System.Text;
using UnityEngine;

#if UNITY_WEBGL && !UNITY_EDITOR
	using WebGLWebsocket;
#else
	using WebSocketSharp;
#endif

using Newtonsoft.Json;

public class Websocket: Protocol
{
	private WebSocket ws;

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
		}
		catch (Exception ex)
		{
			error = "Ошибка соединения " + ex.Message;
		}
	}	
	
	public override void Close()
    {
        if (ws!=null && ws.ReadyState != WebSocketSharp.WebSocketState.Closed && ws.ReadyState != WebSocketSharp.WebSocketState.Closing)
        {
			ws.Close();
            Debug.LogError("Соединение закрыто");
        }
    }


    public override void Send(Response data)
	{
        if (!ConnectController.pause) 
		{ 
			if (ws == null || (ws.ReadyState != WebSocketSharp.WebSocketState.Open && ws.ReadyState != WebSocketSharp.WebSocketState.Connecting))
			{
				error = "Соединение не открыто для запросов ";
				return;
			}
			
			try
			{
				string json = JsonConvert.SerializeObject(data,
														  Newtonsoft.Json.Formatting.None,
														  new JsonSerializerSettings
														  {
															NullValueHandling = NullValueHandling.Ignore
														  });
				Put(json);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
		}
	}

	public override void Put(string json)
    {
		Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
		byte[] sendBytes = Encoding.UTF8.GetBytes(json);
		ws.Send(sendBytes);
	}
}