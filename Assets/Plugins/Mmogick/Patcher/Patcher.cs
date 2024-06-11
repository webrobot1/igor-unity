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
		private const string MAP_FILE = "map.txt";

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

		public static IEnumerator GetMap(string server, int game_id, string token, int map_id, int updated, System.Action<Patcher> callback)
		{
			string url = "http://" + server + "/maps2d/patch/get_map/?map_id=" + map_id + "&token=" + token;
			string folder;

#if UNITY_WEBGL && !UNITY_EDITOR
			folder = "idbfs";		
#elif UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
			folder = Application.persistentDataPath;
#else
			folder = Path.GetDirectoryName(Application.dataPath);
#endif
			Patcher patcher = new Patcher(Path.Combine(folder, "Maps", game_id.ToString(), map_id.ToString()), url);

			bool exists = File.Exists(Path.Combine(patcher.path, MAP_FILE)) && File.Exists(Path.Combine(patcher.path, VERSION_FILE));
			bool version_ok = exists && File.ReadAllText(Path.Combine(patcher.path, VERSION_FILE)) == updated.ToString();

			if (exists && version_ok)
			{
				patcher.result = File.ReadAllText(Path.Combine(patcher.path, MAP_FILE));
				Debug.Log("Карты: используем кеш " + map_id + " версии "+ updated + " с " + patcher.path);
			}
            else
            {
				Debug.Log("Карты: получаем " + map_id + " с " + patcher.url + " - " + (exists && !version_ok ? "версия устарела" : "фаил остутвует в кеше"));
				yield return patcher.download(updated);
			}
			callback(patcher);
			yield break;
		}

		public IEnumerator download(int updated)
		{		
			UnityWebRequest request = UnityWebRequest.Get(url);
			request.redirectLimit = 1;

			yield return request.SendWebRequest();

			// проверим что пришло в ответ
			string data = request.downloadHandler.text;
			if (data.Length > 0)
			{
				try
				{
					DataDecodeRecive recive = JsonConvert.DeserializeObject<DataDecodeRecive>(data);
					if (recive.error.Length > 0)
					{
						error = "Патчер: Ошибка запроса " + url + ": " + recive.error;
					}
					else if (recive.data.Length == 0)
						error = "Патчер: ответ запроса c сервера " + url + " не содержит данных";
					else
					{
						result = recive.data;
						if (!Directory.Exists(path))
							Directory.CreateDirectory(path);

						File.WriteAllText(Path.Combine(path, MAP_FILE), result);
						File.WriteAllText(Path.Combine(path, VERSION_FILE), updated.ToString());

#if UNITY_WEBGL && !UNITY_EDITOR
	JsSync();
#endif
					}
				}
				catch (Exception ex)
				{
					error = "Патчер: Ошибка раскодирования ответа с сервера" + url + ": " + ex;
				}
			}
			else
				error = "Патчер: пустой ответ сервера " + url + " (" + request.responseCode + "): " + request.error;

			request.Dispose(); 
			yield break;
		}		
	}
}
