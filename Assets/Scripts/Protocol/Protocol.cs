using System;
using System.Collections.Generic;
using UnityEngine;

abstract public class Protocol
{
	public string error;
	public List<string> recives = new List<string>();

	public abstract void Close();
	public abstract void Send(Response data);
	public abstract void Put(string json);
}