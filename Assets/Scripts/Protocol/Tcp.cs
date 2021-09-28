using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;
using System.Threading;

public class Tcp : Protocol
{
	private TcpClient connect;
	private NetworkStream stream;

	private static List<byte> recive = new List<byte>();

	protected override void Connect()
	{
		try
		{
			connect = new TcpClient();
			connect.Connect("95.216.204.181", 8081);
			stream = connect.GetStream();

			new Thread(() =>
				{
					while (true) { 
						if (stream != null && stream.DataAvailable)
						{
							byte[] receiveBytes = new byte[connect.ReceiveBufferSize];
							int count = stream.Read(receiveBytes, 0, connect.ReceiveBufferSize);

							try
							{
								while (count != 0)
								{
									for (int i = 0; i < count; i++)
									{
										if (receiveBytes[i] == 124 && receiveBytes[i + 1] == 124)
										{
											recives.Add(Encoding.UTF8.GetString(recive.ToArray()).ToString());
											recive.Clear();
											i++;
										}
										else
											recive.Add(receiveBytes[i]);
									}
									count = stream.Read(receiveBytes, 0, connect.ReceiveBufferSize);
								}
							}
							catch (Exception ex)
							{
								error = ex.Message;
							}
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
			byte[] sendBytes = Encoding.ASCII.GetBytes(data+"||");
			stream.Write(sendBytes, 0, sendBytes.Length);
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
	}
}