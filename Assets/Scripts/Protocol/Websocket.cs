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

	private void OnOpen()
	{
		Debug.Log("Соединение с севрером установлено");
		reconnect = 0;
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
		/*if (reconnect < 5 && !e.WasClean)
		{
			Debug.Log("Соединение с сервером закрыто: " + e.Reason + ", устанавливаю новое");
			if (!connect.IsAlive)
			{
				Thread.Sleep(5000);
				reconnect++;
				connect.Connect();
			}
		}
		else*/
			error = "Соединение с сервером закрыто (" + code.ToString() + ")";
	}


	public override void Send(string data)
	{
		try
		{
			Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + data);
			byte[] sendBytes = Encoding.UTF8.GetBytes(data);
			connect.Send(sendBytes);
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
	}
}