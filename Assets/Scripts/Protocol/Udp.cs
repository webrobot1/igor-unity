using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using System.Threading;

public class Udp : Protocol
{
	private UdpClient connect;
	private IPEndPoint endPoint;

	protected override void Connect()
	{
		try
		{
			connect = new UdpClient();
			IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 8081);

			connect.Connect("95.216.204.181", 8081);

			new Thread(() =>
			{
				while (true)
				{
					if (connect.Available > 0)
					{
						recives.Add(Encoding.UTF8.GetString(connect.Receive(ref endPoint)).ToString());
					}
				}
			}
			).Start();
		}
		catch (Exception ex)
		{
			error = "Ошибка соединения " + ex.Message;
		}
	}

	public override void Send(string data)
	{
		try
		{
			Debug.Log(DateTime.Now.Millisecond + " Отправили серверу " + data);
			byte[] sendBytes = Encoding.ASCII.GetBytes(data + "||");
			connect.Send(sendBytes, sendBytes.Length);
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
	}
}