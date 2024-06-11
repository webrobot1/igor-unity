using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Runtime.InteropServices;

namespace Mmogick
{
	public class Patcher
	{
		private const string VERSION_FILE = "version.txt";
		private const string MAP_FILE = "version.txt";

		public string error;
		public string result;

		public string path;
		public string url;

		[DllImport("__Internal")]
		private static extern void JsSync();

		public Patcher(string path, string url)
        {
			this.path = path;
			this.url = url;
        }

		public static IEnumerator GetMap(string server, int game_id, string token, int map_id, int version, System.Action<Patcher> callback)
		{
			string url = "http://" + server + "/maps2d/patch/get_map/?map_id=" + map_id + "&token=" + token;
			string folder;

#if UNITY_WEBGL && !UNITY_EDITOR
			folder = "idbfs";
			Debug.Log(Application.persistDataPath);			
#else
			folder = Application.dataPath;
#endif
			Patcher patcher = new Patcher(Path.Combine(folder, "Maps", game_id.ToString(), map_id.ToString()), url);
			if (File.Exists(Path.Combine(patcher.path, MAP_FILE)) && File.Exists(Path.Combine(patcher.path, VERSION_FILE)) && File.ReadAllText(Path.Combine(patcher.path, VERSION_FILE)) == version.ToString())
			{
				patcher.result = File.ReadAllText(Path.Combine(patcher.path, MAP_FILE));
			}
            else
            {				
				Debug.Log("Карты: получаем " + map_id + " с " + patcher.url);

				UnityWebRequest request = UnityWebRequest.Get(patcher.url);
				request.redirectLimit = 1;

				yield return request.SendWebRequest();

				// проверим что пришло в ответ
				string result = request.downloadHandler.text;
				if (result.Length > 0)
				{
					try
					{
						MapDecodeRecive recive = JsonConvert.DeserializeObject<MapDecodeRecive>(result);
						if (recive.error.Length > 0)
						{
							patcher.error = "Карты: Ошибка запроса " + map_id + ": " + recive.error;
						}
						else if (recive.map.Length == 0)
							patcher.error = "Карты: ответ запроса карты " + map_id + " c сервера " + server + " не содержит карту";
                        else
                        {
							patcher.result = recive.map;
							File.WriteAllText(Path.Combine(patcher.path, MAP_FILE), patcher.result);
							File.WriteAllText(Path.Combine(patcher.path, VERSION_FILE), version.ToString());

#if UNITY_WEBGL && !UNITY_EDITOR
	JsSync();
#endif
						}
					}
					catch (Exception ex)
					{
						patcher.error = "Карты: Ошибка раскодирования ответа с сервера" + server + ": " + ex;
					}
				}
				else
					patcher.error = "Карты: пустой ответ сервера " + server + " (" + request.responseCode + "): " + request.error;

				request.Dispose();
			}
			callback(patcher);
			yield break;
		}
	}
}
