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

	protected override void Connect()
	{
		if (this.ws != null && (this.ws.ReadyState == WebSocketSharp.WebSocketState.Open || this.ws.ReadyState == WebSocketSharp.WebSocketState.Closing))
		{
			error = "WebSocket is already connected or is closing.";
			return;
		}

		try
		{
			ws = new WebSocket("ws://my-fantasy.ru:8080");
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
				Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + json);
				byte[] sendBytes = Encoding.UTF8.GetBytes(json);
				ws.Send(sendBytes);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
		}
	}
}