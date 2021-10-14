using System;
using System.Collections.Generic;
using UnityEngine;

abstract public class Protocol
{
	public string error;
	public List<string> recives = new List<string>();

	public Protocol()
	{
		Debug.Log("Соединяемся с сервером");
		Connect();
	}

	protected abstract void Connect();
	public abstract void Close();
	public abstract void Send(ResponseJson data);
}