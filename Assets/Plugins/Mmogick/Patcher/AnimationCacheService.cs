using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	// Content-addressable кеш анимаций игры. Работает с 3 endpoint'ами сервера:
	//   GET /animations2d/patch/structure/{prefab}/{token} — SCML XML + sha256-маппинг (If-None-Match/ETag)
	//   GET /animations2d/patch/images/{gameId}/{token}    — ZIP всех картинок игры (If-Modified-Since)
	//   GET /animations2d/patch/library/{gameId}/{token}?since=<ts> — diff per-game overrides
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/animations/
	//   images/{sha256}.{ext}     — распакованные из ZIP /images
	//   structures/{prefab}.json  — кеш ответа /structure (xml + files), payload base64+gzip
	//   library.json              — последний снимок overrides (animation_id → LibraryItem)
	//   sync.json                 — manifest (archive_last_modified, prefab_etags{}, library_since)
	public static class AnimationCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE = "sync.json";
		private const string LIBRARY_FILE  = "library.json";
		private const string IMAGES_DIR    = "images";
		private const string STRUCT_DIR    = "structures";

		private static SyncManifest _manifest;
		private static Dictionary<int, LibraryItem> _library;
		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		[Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public int    library_since;
			public Dictionary<string, string> prefab_etags = new Dictionary<string, string>();
		}

		[Serializable]
		public class LibraryItem
		{
			public int    animation_id;
			public string name;
			public int?   interval_override;
			public int    updated;
		}

		[Serializable]
		private class LibraryResponse
		{
			public LibraryItem[] items;
			public int           now;
		}

		[Serializable]
		private class StructurePayload
		{
			public string       xml;
			public SpriterFile[] files;
		}

		[Serializable]
		private class StructureResponse
		{
			public string data;  // base64(gzip(json))
			public string error;
		}

		// Корень кеша анимаций для игры
		private static string AnimationsPath(int gameId)
		{
			string folder;
			#if UNITY_WEBGL && !UNITY_EDITOR
				folder = "idbfs";
			#else
				folder = Application.persistentDataPath;
			#endif
			string path = Path.Combine(folder, "games", gameId.ToString(), "animations");
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			return path;
		}

		private static string ImagesPath(int gameId)    => Path.Combine(AnimationsPath(gameId), IMAGES_DIR);
		private static string StructPath(int gameId)    => Path.Combine(AnimationsPath(gameId), STRUCT_DIR);
		private static string ManifestPath(int gameId)  => Path.Combine(AnimationsPath(gameId), MANIFEST_FILE);
		private static string LibraryPath(int gameId)   => Path.Combine(AnimationsPath(gameId), LIBRARY_FILE);

		// Загружает manifest + library с диска. Идемпотентно.
		private static void EnsureLoaded(int gameId)
		{
			if (_manifest == null)
			{
				string mp = ManifestPath(gameId);
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();
			}
			if (_library == null)
			{
				string lp = LibraryPath(gameId);
				_library = File.Exists(lp)
					? JsonConvert.DeserializeObject<Dictionary<int, LibraryItem>>(File.ReadAllText(lp))
					: new Dictionary<int, LibraryItem>();
			}
			if (!Directory.Exists(ImagesPath(gameId))) Directory.CreateDirectory(ImagesPath(gameId));
			if (!Directory.Exists(StructPath(gameId))) Directory.CreateDirectory(StructPath(gameId));
		}

		private static void SaveManifest(int gameId)
		{
			File.WriteAllText(ManifestPath(gameId), JsonConvert.SerializeObject(_manifest));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		private static void SaveLibrary(int gameId)
		{
			File.WriteAllText(LibraryPath(gameId), JsonConvert.SerializeObject(_library));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полная синхронизация перед входом в игру: архив картинок + library overrides. Вызывать ДО Connect.
		public static IEnumerator SyncAll(string host, int gameId, string token, Action<string> onError = null)
		{
			EnsureLoaded(gameId);
			yield return SyncImagesArchive(host, gameId, token, onError);
			yield return SyncLibrary(host, gameId, token, onError);
		}

		// Архив: GET с If-Modified-Since. 304 → ничего. 200 → unzip в images/.
		public static IEnumerator SyncImagesArchive(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animations2d/patch/images/" + gameId + "/" + token;
			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.archive_last_modified))
				req.SetRequestHeader("If-Modified-Since", _manifest.archive_last_modified);
			req.downloadHandler = new DownloadHandlerBuffer();

			yield return req.SendWebRequest();

			if (req.responseCode == 304)
			{
				req.Dispose();
				yield break;
			}
			// 202 = building, попробуем в следующий заход
			if (req.responseCode == 202)
			{
				Debug.Log("AnimationCache: архив пересобирается на сервере, повторим позже");
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache archive: " + req.responseCode + " " + req.error);
				req.Dispose();
				yield break;
			}

			string lastMod = req.GetResponseHeader("Last-Modified");
			byte[] zipBytes = req.downloadHandler.data;
			req.Dispose();

			try
			{
				string imagesDir = ImagesPath(gameId);
				using (var ms = new MemoryStream(zipBytes))
				using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
				{
					foreach (var entry in zip.Entries)
					{
						if (string.IsNullOrEmpty(entry.Name)) continue;
						string dest = Path.Combine(imagesDir, entry.Name);
						using (var src = entry.Open())
						using (var dst = File.Create(dest))
						{
							src.CopyTo(dst);
						}
					}
				}
			}
			catch (Exception ex)
			{
				onError?.Invoke("AnimationCache archive unzip: " + ex.Message);
				yield break;
			}

			_manifest.archive_last_modified = lastMod;
			SaveManifest(gameId);
			_spriteCache.Clear(); // новые картинки могли появиться
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Library: GET ?since=<library_since>. Ответ мержится в _library по animation_id.
		public static IEnumerator SyncLibrary(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animations2d/patch/library/" + gameId + "/" + token + "?since=" + _manifest.library_since;
			UnityWebRequest req = UnityWebRequest.Get(url);

			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache library: " + req.responseCode + " " + req.error);
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			LibraryResponse resp;
			try { resp = JsonConvert.DeserializeObject<LibraryResponse>(text); }
			catch (Exception ex) { onError?.Invoke("AnimationCache library parse: " + ex.Message); yield break; }

			if (resp.items != null)
			{
				foreach (var item in resp.items)
					_library[item.animation_id] = item;
			}
			_manifest.library_since = resp.now;
			SaveManifest(gameId);
			SaveLibrary(gameId);
		}

		// Структура анимации: SCML XML + sha256-маппинг.
		// If-None-Match → 304 = читаем из кеша. 200 → распаковываем base64+gzip, сохраняем, возвращаем.
		public static IEnumerator GetStructure(string host, int gameId, string prefab, string token,
			Action<string, SpriterFile[], string> callback)
		{
			EnsureLoaded(gameId);
			string structFile = Path.Combine(StructPath(gameId), prefab + ".json");
			_manifest.prefab_etags.TryGetValue(prefab, out string etag);

			string url = "http://" + host + "/animations2d/patch/structure/" + prefab + "/" + token;
			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(etag))
				req.SetRequestHeader("If-None-Match", etag);

			yield return req.SendWebRequest();

			if (req.responseCode == 304 && File.Exists(structFile))
			{
				try
				{
					var cached = JsonConvert.DeserializeObject<StructurePayload>(File.ReadAllText(structFile));
					callback(cached.xml, cached.files, null);
				}
				catch (Exception ex) { callback(null, null, "AnimationCache cache parse: " + ex.Message); }
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				callback(null, null, "AnimationCache structure " + prefab + ": " + req.responseCode + " " + req.error);
				req.Dispose();
				yield break;
			}

			string body = req.downloadHandler.text;
			string newEtag = req.GetResponseHeader("ETag");
			req.Dispose();

			StructureResponse wrapper;
			try { wrapper = JsonConvert.DeserializeObject<StructureResponse>(body); }
			catch (Exception ex) { callback(null, null, "AnimationCache structure wrapper: " + ex.Message); yield break; }

			if (!string.IsNullOrEmpty(wrapper.error))
			{
				callback(null, null, "AnimationCache structure server error: " + wrapper.error);
				yield break;
			}

			StructurePayload payload;
			try
			{
				byte[] gzipped = Convert.FromBase64String(wrapper.data);
				using (var src = new MemoryStream(gzipped))
				using (var gz  = new GZipStream(src, CompressionMode.Decompress))
				using (var dst = new MemoryStream())
				{
					gz.CopyTo(dst);
					string json = Encoding.UTF8.GetString(dst.ToArray());
					payload = JsonConvert.DeserializeObject<StructurePayload>(json);
				}
			}
			catch (Exception ex) { callback(null, null, "AnimationCache structure decode: " + ex.Message); yield break; }

			File.WriteAllText(structFile, JsonConvert.SerializeObject(payload));
			if (!string.IsNullOrEmpty(newEtag))
			{
				_manifest.prefab_etags[prefab] = newEtag;
				SaveManifest(gameId);
			}
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif

			callback(payload.xml, payload.files, null);
		}

		// Sprite по sha256: грузится из локального кеша картинок, кешируется в памяти.
		public static Sprite GetSprite(int gameId, string sha256, string ext)
		{
			string key = sha256 + "." + ext;
			if (_spriteCache.TryGetValue(key, out Sprite s)) return s;

			string path = Path.Combine(ImagesPath(gameId), key);
			if (!File.Exists(path))
			{
				Debug.LogError("AnimationCache: отсутствует картинка " + key + " (архив устарел?)");
				return null;
			}

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			tex.LoadImage(bytes);
			tex.filterMode = FilterMode.Point;
			s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), tex.width, 0, SpriteMeshType.FullRect);
			_spriteCache[key] = s;
			return s;
		}

		// Per-game override по animation_id (если клиент знает) или null.
		public static LibraryItem GetLibraryOverride(int animationId)
		{
			if (_library == null) return null;
			_library.TryGetValue(animationId, out LibraryItem item);
			return item;
		}

		// Per-game override по имени prefab (admin-задаваемое имя в библиотеке игры) или null.
		public static LibraryItem GetLibraryOverrideByName(string name)
		{
			if (_library == null) return null;
			foreach (var kv in _library)
				if (kv.Value.name == name) return kv.Value;
			return null;
		}
	}
}
