using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;
using System.Runtime.InteropServices;

namespace Mmogick
{
	// Content-addressable кеш тайлов игры. Работает с endpoint'ами сервера:
	//   GET /map/patch/{game}/{token}/archive           — ZIP со всеми PNG графики (If-Modified-Since)
	//   GET /map/patch/{game}/{token}/map/{mapId}       — terrain.json карты (If-Modified-Since)
	//   GET /map/patch/{game}/{token}/tileset           — список тайлсетов с timestamp'ами
	//   GET /map/patch/{game}/{token}/tileset/{id}      — per-tileset meta (name, property, tile meta, wangsets)
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/
	//   tiles/{sha256}.png
	//   tileset/{tilesetId}.json  — per-tileset кэш {name, property, tile: {sha → meta}, wangset[]}
	//   sync.json                 — { archive_last_modified, tileset_versions: {id: ts}, map_versions: {mapId: ts} }
	public static class TileCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE = "sync.json";
		private const string TILES_DIR = "tiles";
		private const string TILESET_DIR = "tileset";
		private const string MAPS_DIR = "maps";

		private static SyncManifest _manifest;
		private static Dictionary<string, TilesetMeta> _tilesets;
		private static Dictionary<string, Tile> _meta;
		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		[System.Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public Dictionary<string, long> tileset_versions = new Dictionary<string, long>();
			public Dictionary<int, string> map_versions = new Dictionary<int, string>();
		}


		// Корень кеша для игры
		private static string GamePath(int gameId)
		{
			string folder;
			#if UNITY_WEBGL && !UNITY_EDITOR
				folder = "idbfs";
			#else
				folder = Application.persistentDataPath;
			#endif
			string path = Path.Combine(folder, "games", gameId.ToString());
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			return path;
		}

		private static string TilesPath(int gameId)  => Path.Combine(GamePath(gameId), TILES_DIR);
		private static string MapsPath(int gameId)   => Path.Combine(GamePath(gameId), MAPS_DIR);

		private static string ManifestPath(int gameId) => Path.Combine(GamePath(gameId), MANIFEST_FILE);
		private static string TilesetPath(int gameId) => Path.Combine(GamePath(gameId), TILESET_DIR);
		private static string TilesetFilePath(int gameId, string tilesetId) => Path.Combine(TilesetPath(gameId), tilesetId + ".json");

		// Извлекает текст серверной ошибки из body ({"error":"..."} — exceptionHandler и явные 4xx/5xx).
		// Fallback — код+generic error от UnityWebRequest.
		private static string ExtractError(UnityWebRequest req)
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

		private static void EnsureLoaded(int gameId)
		{
			if (_manifest == null)
			{
				string mp = ManifestPath(gameId);
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();
			}
			if (_tilesets == null)
			{
				_tilesets = new Dictionary<string, TilesetMeta>();
				_meta = new Dictionary<string, Tile>();
				string dir = TilesetPath(gameId);
				if (Directory.Exists(dir))
				{
					foreach (string file in Directory.GetFiles(dir, "*.json"))
					{
						string id = Path.GetFileNameWithoutExtension(file);
						try
						{
							var ts = JsonConvert.DeserializeObject<TilesetMeta>(File.ReadAllText(file));
							if (ts != null)
							{
								_tilesets[id] = ts;
								if (ts.tile != null)
									foreach (var kv in ts.tile)
										_meta[kv.Key] = kv.Value;
							}
						}
						catch (System.Exception ex)
						{
							Debug.LogError("TileCache: битый кеш тайлсета " + id + ", удаляем: " + ex.Message);
							File.Delete(file);
						}
					}
				}
			}
			if (!Directory.Exists(TilesPath(gameId))) Directory.CreateDirectory(TilesPath(gameId));
			if (!Directory.Exists(TilesetPath(gameId))) Directory.CreateDirectory(TilesetPath(gameId));
			if (!Directory.Exists(MapsPath(gameId))) Directory.CreateDirectory(MapsPath(gameId));
		}

		private static void SaveManifest(int gameId)
		{
			File.WriteAllText(ManifestPath(gameId), JsonConvert.SerializeObject(_manifest));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		public static void ResetCache(int gameId)
		{
			Debug.LogWarning("TileCache: сброс кеша игры " + gameId);
			_manifest = new SyncManifest();
			_tilesets = new Dictionary<string, TilesetMeta>();
			_meta = new Dictionary<string, Tile>();
			_spriteCache.Clear();

			try
			{
				if (File.Exists(ManifestPath(gameId))) File.Delete(ManifestPath(gameId));
				if (Directory.Exists(TilesPath(gameId)))   Directory.Delete(TilesPath(gameId), true);
				if (Directory.Exists(TilesetPath(gameId))) Directory.Delete(TilesetPath(gameId), true);
				if (Directory.Exists(MapsPath(gameId)))    Directory.Delete(MapsPath(gameId), true);
			}
			catch (Exception ex) { Debug.LogWarning("TileCache: ошибка при сбросе кеша: " + ex.Message); }

			Directory.CreateDirectory(TilesPath(gameId));
			Directory.CreateDirectory(TilesetPath(gameId));
			Directory.CreateDirectory(MapsPath(gameId));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полная синхронизация перед входом в игру: архив PNG + мета. Вызывать ДО Connect.
		public static IEnumerator SyncAll(string host, int gameId, string token, Action<string> onError = null)
		{
			EnsureLoaded(gameId);
			yield return SyncArchive(host, gameId, token, onError);
			yield return SyncMeta(host, gameId, token, onError);
		}

		// Архив: GET с If-Modified-Since. 304 → ничего. 200 → unzip в tiles/.
		public static IEnumerator SyncArchive(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/archive";
			Debug.Log("Запрашиваю архив изображения карт "+url);
			
			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.archive_last_modified))
				req.SetRequestHeader("If-Modified-Since", _manifest.archive_last_modified);
			req.downloadHandler = new DownloadHandlerBuffer();

			yield return req.SendWebRequest();

			if (req.responseCode == 304)
			{
				Debug.Log("TileCache: архив тайлов актуален (кеш)");
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("TileCache archive: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string lastMod = req.GetResponseHeader("Last-Modified");
			byte[] zipBytes = req.downloadHandler.data;
			req.Dispose();

			int extractedCount = 0;
			try
			{
				string tilesDir = TilesPath(gameId);
				using (var ms = new MemoryStream(zipBytes))
				using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
				{
					foreach (var entry in zip.Entries)
					{
						if (string.IsNullOrEmpty(entry.Name)) continue; // скип директорий
						string dest = Path.Combine(tilesDir, entry.Name);
						// Ручное чтение stream — без ExtractToFile, т.к. на WebGL он иногда стрипается
						using (var src = entry.Open())
						using (var dst = File.Create(dest))
						{
							src.CopyTo(dst);
						}
						extractedCount++;
					}
				}
			}
			catch (Exception ex)
			{
				onError?.Invoke("TileCache archive unzip: " + ex.Message);
				yield break;
			}

			Debug.Log("TileCache: архив тайлов обновлён, распаковано " + extractedCount + " файлов");
			_manifest.archive_last_modified = lastMod;
			SaveManifest(gameId);
			_spriteCache.Clear(); // новые PNG могли появиться — сбросим кеш спрайтов
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Tileset meta: 1) GET /tileset → список {id: timestamp}  2) GET /tileset/{id} для изменившихся
		public static IEnumerator SyncMeta(string host, int gameId, string token, Action<string> onError)
		{
			string listUrl = "http://" + host + "/map/patch/" + gameId + "/" + token + "/tileset";
			Debug.Log("Запрашиваю список тайлсетов " + listUrl);

			UnityWebRequest listReq = UnityWebRequest.Get(listUrl);
			listReq.downloadHandler = new DownloadHandlerBuffer();
			yield return listReq.SendWebRequest();

			if (listReq.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("TileCache tileset list: " + ExtractError(listReq));
				listReq.Dispose();
				yield break;
			}

			Dictionary<string, long> serverVersions;
			try { serverVersions = JsonConvert.DeserializeObject<Dictionary<string, long>>(listReq.downloadHandler.text); }
			catch (Exception ex) { onError?.Invoke("TileCache tileset list parse: " + ex.Message); listReq.Dispose(); yield break; }
			listReq.Dispose();

			if (serverVersions == null || serverVersions.Count == 0)
			{
				Debug.Log("TileCache: тайлсетов нет");
				yield break;
			}

			int updated = 0;
			foreach (var kv in serverVersions)
			{
				string tilesetId = kv.Key;
				long serverTs = kv.Value;

				if (_manifest.tileset_versions.TryGetValue(tilesetId, out long localTs) && localTs >= serverTs)
					continue;

				string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/tileset/" + tilesetId;
				UnityWebRequest req = UnityWebRequest.Get(url);
				req.downloadHandler = new DownloadHandlerBuffer();
				yield return req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					Debug.LogWarning("TileCache: ошибка загрузки тайлсета " + tilesetId + ": " + ExtractError(req));
					req.Dispose();
					continue;
				}

				string json = req.downloadHandler.text;
				req.Dispose();

				try
				{
					var ts = JsonConvert.DeserializeObject<TilesetMeta>(json);
					File.WriteAllText(TilesetFilePath(gameId, tilesetId), json);
					_manifest.tileset_versions[tilesetId] = serverTs;

					if (ts != null)
					{
						_tilesets[tilesetId] = ts;
						if (ts.tile != null)
							foreach (var tile in ts.tile)
								_meta[tile.Key] = tile.Value;
					}

					updated++;
				}
				catch (System.Exception ex)
				{
					string path = TilesetFilePath(gameId, tilesetId);
					if (File.Exists(path)) File.Delete(path);
					onError?.Invoke("TileCache: ошибка разбора тайлсета " + tilesetId + ": " + ex.Message);
					yield break;
				}
			}

			// Удалить локальные тайлсеты которых больше нет на сервере
			var toRemove = new List<string>();
			foreach (var id in _manifest.tileset_versions.Keys)
				if (!serverVersions.ContainsKey(id))
					toRemove.Add(id);
			foreach (var id in toRemove)
			{
				_manifest.tileset_versions.Remove(id);
				_tilesets.Remove(id);
				string fp = TilesetFilePath(gameId, id);
				if (File.Exists(fp)) File.Delete(fp);
			}

			if (updated > 0 || toRemove.Count > 0)
			{
				// Пересобрать плоский _meta из всех тайлсетов
				_meta = new Dictionary<string, Tile>();
				foreach (var ts in _tilesets.Values)
					if (ts.tile != null)
						foreach (var kv in ts.tile)
							_meta[kv.Key] = kv.Value;

				SaveManifest(gameId);
				Debug.Log("TileCache: обновлено " + updated + " тайлсетов, удалено " + toRemove.Count);
			}
			else
			{
				Debug.Log("TileCache: все тайлсеты актуальны");
			}

			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// terrain.json + tile meta карты: If-Modified-Since → 304 из кеша, иначе скачать и сохранить.
		// callback вызывается с JSON-строкой карты либо error-сообщением.
		public static IEnumerator GetMap(string host, int gameId, int mapId, string token, Action<string, string> callback)
		{
			EnsureLoaded(gameId);
			string mapFile = Path.Combine(MapsPath(gameId), mapId + ".json");
			_manifest.map_versions.TryGetValue(mapId, out string lastMod);

			string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/map/" + mapId;
			Debug.Log("Запрашиваю плитку карты "+url);
			
			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(lastMod)) req.SetRequestHeader("If-Modified-Since", lastMod);

			yield return req.SendWebRequest();

			if (req.responseCode == 304 && File.Exists(mapFile))
			{
				Debug.Log("TileCache: карта " + mapId + " из кеша");
				callback(File.ReadAllText(mapFile), null);
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				callback(null, "TileCache map " + mapId + ": " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string json = req.downloadHandler.text;
			string newLastMod = req.GetResponseHeader("Last-Modified");
			req.Dispose();

			File.WriteAllText(mapFile, json);
			if (!string.IsNullOrEmpty(newLastMod))
			{
				_manifest.map_versions[mapId] = newLastMod;
				SaveManifest(gameId);
			}
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif

			Debug.Log("TileCache: карта " + mapId + " скачана с сервера");
			callback(json, null);
		}

		// Sprite по sha256: грузится из PNG-файла локального кеша, кешируется в памяти.
		public static Sprite GetSprite(int gameId, string sha256)
		{
			// Unity-объект в словаре может быть уничтожен Resources.UnloadUnusedAssets при переходе сцен
			// (static-ссылка C# живёт, но нативный ресурс снесён). Проверяем через == null и пересоздаём.
			if (_spriteCache.TryGetValue(sha256, out Sprite cached) && cached != null) return cached;

			string path = Path.Combine(TilesPath(gameId), sha256 + ".png");
			if (!File.Exists(path))
			{
				throw new Exception("TileCache: отсутствует графика тайла " + sha256 + " (архив устарел?)");
			}

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			// Битые PNG — Unity вернёт false, текстура останется в fallback-состоянии и тайл тихо
			// отрисуется мусором. Сразу вызываем ConnectController.Error (UI-ошибка + отсоединение),
			// файл удаляем — следующий sync перекачает с сервера. Вызыватель получит null sprite,
			// клетка карты останется пустой вместо мусора.
			if (!tex.LoadImage(bytes))
			{
				try { File.Delete(path); } catch { /* нет прав / уже удалён — следующий заход всё равно перекачает */ }
				ConnectController.Error("TileCache: повреждённый PNG " + sha256 + " (" + bytes.Length + " байт), удалён из кеша");
				return null;
			}
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), tex.width, 0, SpriteMeshType.FullRect);
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_spriteCache[sha256] = s;
			Debug.Log("TileCache: спрайт " + sha256 + " загружен с диска");
			return s;
		}

		// Мета по sha256 (или null если её нет).
		public static Tile GetMeta(string sha256)
		{
			if (_meta == null) return null;
			_meta.TryGetValue(sha256, out Tile m);
			return m;
		}
	}
}
