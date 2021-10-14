using System;
using System.Text;
using UnityEngine;
using HybridWebSocket;

public class Websocket: Protocol
{
	protected WebSocket connect;

	protected override void Connect()
	{
		try
		{
			connect = WebSocketFactory.CreateInstance("ws://95.216.204.181:8081");
			connect.OnClose += OnClose;
			connect.OnMessage += OnMessage;
			connect.OnOpen += OnOpen;
			connect.OnError += OnError;
			connect.Connect();
		}
		catch (Exception ex)
		{
			error = "Ошибка соединения " + ex.Message;
		}
	}	
	
	public override void Close()
	{
		connect.Close(WebSocketCloseCode.ProtocolError);
		Debug.LogError("Соединение закрыто");
	}

	public override void Send(ResponseJson data)
	{
		try
		{
			Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + data);
			byte[] sendBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
			connect.Send(sendBytes);
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
	}

	private void OnOpen()
	{
		Debug.Log("Соединение с севрером установлено");
	}

	private void OnError(string errMsg)
	{
		error = "Ошибка соединения " + errMsg;
	}


	public void OnMessage(byte[] msg)
	{
		recives.Add(Encoding.UTF8.GetString(msg));	
	}


	private void OnClose(WebSocketCloseCode code)
	{
		error = "Соединение с сервером закрыто (" + code.ToString() + ")";
	}
}