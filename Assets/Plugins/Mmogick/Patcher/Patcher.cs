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
		private const string FILE = "data.txt";

		public string error;
		public string result;

		public string path;
		public string url;

		[DllImport("__Internal")]
		private static extern void JsSync();

		public Patcher(string path, string url)
        {
			string folder;

			#if UNITY_WEBGL && !UNITY_EDITOR
						folder = "idbfs";		
			#elif UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
						folder = Application.persistentDataPath;
			#else
						folder = Path.GetDirectoryName(Application.dataPath);
			#endif

			this.path = Path.Combine(folder, path);
			this.url = url;
        }

		public static IEnumerator GetAnimation(string server, int game_id, string token, string prefab, System.Action<Patcher> callback)
        {
			string url = "http://" + server + "/animations2d/patch/get/?prefab=" + prefab + "&token=" + token;
			Patcher patcher = new Patcher(Path.Combine("Animations", game_id.ToString(), prefab), url);

			yield return patcher.get(0);

			callback(patcher);
			yield break;
		} 

		public static IEnumerator GetMap(string server, int game_id, string token, int map_id, int updated, System.Action<Patcher> callback)
		{
			string url = "http://" + server + "/maps2d/patch/get_map/?map_id=" + map_id + "&token=" + token;
			Patcher patcher = new Patcher(Path.Combine("Maps", game_id.ToString(), map_id.ToString()), url);
			
			yield return patcher.get(updated);

			callback(patcher);
			yield break;
		}

		private IEnumerator get(int updated)
		{
			bool exists = File.Exists(Path.Combine(path, FILE)) && File.Exists(Path.Combine(path, VERSION_FILE));
			bool version_ok = exists && File.ReadAllText(Path.Combine(path, VERSION_FILE)) == updated.ToString();

			if (exists && version_ok)
			{
				result = File.ReadAllText(Path.Combine(path, FILE));
				Debug.Log("Патчер: используем кеш " + url + " версии " + updated + " с " + path);
			}
			else
			{
				Debug.Log("Патчер: получаем " + url + " с " + url + " - " + (exists && !version_ok ? "версия устарела" : "фаил остутвует в кеше"));

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

							File.WriteAllText(Path.Combine(path, FILE), result);
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
}
