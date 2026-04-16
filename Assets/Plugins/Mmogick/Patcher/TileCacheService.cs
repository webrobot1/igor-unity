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
	// Content-addressable кеш тайлов игры. Работает с 3 endpoint'ами сервера:
	//   GET /map/patch/{game}/{token}/archive     — ZIP со всеми PNG графики (If-Modified-Since)
	//   GET /map/patch/{game}/{token}/map/{mapId} — terrain.json карты (If-Modified-Since)
	//   GET /map/patch/{game}/{token}/tile        — tile meta игры (If-Modified-Since)
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/
	//   tiles/{sha256}.png
	//   meta.json    — { sha256: TileMeta } (только для тайлов у которых реально есть мета)
	//   sync.json    — { archive_last_modified, last_meta_updated, map_versions: {mapId: ts} }
	public static class TileCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE = "sync.json";
		private const string META_FILE = "meta.json";
		private const string TILES_DIR = "tiles";
		private const string MAPS_DIR = "maps";

		private static SyncManifest _manifest;
		private static Dictionary<string, TileMeta> _meta;
		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		[System.Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public string last_meta_updated;
			public Dictionary<int, string> map_versions = new Dictionary<int, string>();
		}

		[System.Serializable]
		public class TileMeta
		{
			public TileAnimation[] frame;
			public TileObjectGroup[] objectgroup;
			public TileProperty[] property;
		}

		[System.Serializable]
		public class TileAnimation
		{
			public string tileid;
			public int duration;
		}

		[System.Serializable]
		public class TileObjectGroup
		{
			public string name;
			public TileObject[] objects;
			public TileProperty[] property;
		}

		[System.Serializable]
		public class TileObject
		{
			public string name;
			public string type;
			public float x;
			public float y;
			public float width;
			public float height;
			public float rotation;
			public bool ellipse;
			public bool point;
			public Point[] polygon;
			public Point[] polyline;
			public string sha256;
		}

		[System.Serializable]
		public class TileProperty
		{
			public string name;
			public string value;
			public string type;
			public string propertytype;
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
		private static string MetaPath(int gameId)   => Path.Combine(GamePath(gameId), META_FILE);

		// Загружает manifest + meta с диска. Идемпотентно.
		private static void EnsureLoaded(int gameId)
		{
			if (_manifest == null)
			{
				string mp = ManifestPath(gameId);
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();
			}
			if (_meta == null)
			{
				string mp = MetaPath(gameId);
				_meta = File.Exists(mp)
					? JsonConvert.DeserializeObject<Dictionary<string, TileMeta>>(File.ReadAllText(mp))
					: new Dictionary<string, TileMeta>();
			}
			if (!Directory.Exists(TilesPath(gameId))) Directory.CreateDirectory(TilesPath(gameId));
			if (!Directory.Exists(MapsPath(gameId))) Directory.CreateDirectory(MapsPath(gameId));
		}

		private static void SaveManifest(int gameId)
		{
			File.WriteAllText(ManifestPath(gameId), JsonConvert.SerializeObject(_manifest));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		private static void SaveMeta(int gameId)
		{
			File.WriteAllText(MetaPath(gameId), JsonConvert.SerializeObject(_meta));
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
				onError?.Invoke("TileCache archive: " + req.responseCode + " " + req.error);
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

		// Tile meta игры: GET с If-Modified-Since. 304 → ничего. 200 → полный дамп заменяет _meta.
		public static IEnumerator SyncMeta(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/tile";
			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.last_meta_updated))
				req.SetRequestHeader("If-Modified-Since", _manifest.last_meta_updated);
			req.downloadHandler = new DownloadHandlerBuffer();

			yield return req.SendWebRequest();

			if (req.responseCode == 304)
			{
				Debug.Log("TileCache: мета тайлов актуальна (кеш)");
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("TileCache meta: " + req.responseCode + " " + req.error);
				req.Dispose();
				yield break;
			}

			string lastMod = req.GetResponseHeader("Last-Modified");
			string text = req.downloadHandler.text;
			req.Dispose();

			Dictionary<string, TileMeta> parsed;
			try { parsed = JsonConvert.DeserializeObject<Dictionary<string, TileMeta>>(text); }
			catch (Exception ex) { onError?.Invoke("TileCache meta parse: " + ex.Message); yield break; }

			_meta = parsed ?? new Dictionary<string, TileMeta>();
			Debug.Log("TileCache: мета обновлена, получено " + _meta.Count + " записей");
			_manifest.last_meta_updated = lastMod;
			SaveManifest(gameId);
			SaveMeta(gameId);
		}

		// terrain.json + tile meta карты: If-Modified-Since → 304 из кеша, иначе скачать и сохранить.
		// callback вызывается с JSON-строкой карты либо error-сообщением.
		public static IEnumerator GetMap(string host, int gameId, int mapId, string token, Action<string, string> callback)
		{
			EnsureLoaded(gameId);
			string mapFile = Path.Combine(MapsPath(gameId), mapId + ".json");
			_manifest.map_versions.TryGetValue(mapId, out string lastMod);

			string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/map/" + mapId;
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
				callback(null, "TileCache map " + mapId + ": " + req.responseCode + " " + req.error);
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
			if (_spriteCache.TryGetValue(sha256, out Sprite s)) return s;

			string path = Path.Combine(TilesPath(gameId), sha256 + ".png");
			if (!File.Exists(path))
			{
				throw new Exception("TileCache: отсутствует графика тайла " + sha256 + " (архив устарел?)");
			}

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			tex.LoadImage(bytes);
			tex.filterMode = FilterMode.Point;
			s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), tex.width, 0, SpriteMeshType.FullRect);
			_spriteCache[sha256] = s;
			Debug.Log("TileCache: спрайт " + sha256 + " загружен с диска");
			return s;
		}

		// Мета по sha256 (или null если её нет).
		public static TileMeta GetMeta(string sha256)
		{
			if (_meta == null) return null;
			_meta.TryGetValue(sha256, out TileMeta m);
			return m;
		}
	}
}
