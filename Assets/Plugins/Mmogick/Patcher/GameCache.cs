using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	// Общее устройство локальных кешей игры: корень каталога, запись файла отметки, guard рассинхрона
	// диск↔RAM и разбор текста серверной ошибки. Кешей у игры несколько — тайлы карты (TileCacheService),
	// графика и каталог префабов (AnimationCacheService), справочник компонентов (ComponentCacheService), —
	// и перечисленное у них одно на всех: различаются они подкаталогом корня да именем в тексте отказа.
	public static class GameCache
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		// Сброс записанного в файловую систему браузера: на WebGL она лежит в памяти страницы, и без явного
		// сброса записанное не переживёт перезагрузку вкладки. Вне WebGL запись доходит до диска сама.
		public static void Flush()
		{
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Корень кеша игры, а с подкаталогом — корень отдельного кеша внутри него. Каталога нет — заводится.
		public static string RootPath(int gameId, string subfolder = null)
		{
			string folder;
			#if UNITY_WEBGL && !UNITY_EDITOR
				folder = "idbfs";
			#else
				folder = Application.persistentDataPath;
			#endif
			string path = Path.Combine(folder, "games", gameId.ToString());
			if (!string.IsNullOrEmpty(subfolder))
				path = Path.Combine(path, subfolder);
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			return path;
		}

		// Запись файла кеша со сбросом на диск браузера.
		public static void WriteJson(string path, object value)
		{
			File.WriteAllText(path, JsonConvert.SerializeObject(value));
			Flush();
		}

		// Рассинхрон disk↔RAM (файл отметки удалён внешним кодом либо ручной очисткой кэша, а разобранная
		// отметка держится в памяти) — нарушение контракта: файлами своего кеша владеет только его сервис.
		// Падаем громко (skill code «Отказ и дефолт»), чтобы виновный код был починен у источника, а не
		// маскировался силент-ресетом.
		public static void RequireManifestOnDisk(string owner, object manifest, string manifestPath)
		{
			if (manifest != null && !File.Exists(manifestPath))
				throw new InvalidOperationException(owner + ": " + Path.GetFileName(manifestPath)
					+ " отсутствует на диске, но манифест загружен в память."
					+ " Кто-то очистил кэш мимо ResetCache() — почините источник.");
		}

		// Извлекает текст серверной ошибки из body ({"error":"..."} — exceptionHandler и явные 4xx/5xx).
		// Fallback — код+generic error от UnityWebRequest.
		public static string ExtractError(UnityWebRequest req)
		{
			string body = req.downloadHandler?.text;
			if (!string.IsNullOrEmpty(body))
			{
				try
				{
					var err = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
					if (err != null && err.TryGetValue("error", out string msg) && !string.IsNullOrEmpty(msg))
						return msg;
				}
				catch { }
			}
			return req.responseCode + " " + req.error;
		}
	}
}
