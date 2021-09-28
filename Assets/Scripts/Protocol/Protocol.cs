using System;
using System.Collections.Generic;
using UnityEngine;

abstract public class Protocol
{
	public static string error;
	public static List<string> recives = new List<string>();


	protected int reconnect = 0;
	public Protocol()
	{
		Debug.Log("Соединяемся с сервером");
		Connect();
	}

	protected abstract void Connect();
	public abstract void Send(string data);
}