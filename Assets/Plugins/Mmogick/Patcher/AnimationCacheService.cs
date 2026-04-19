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
	// Content-addressable кеш анимаций игры. Работает с 4 endpoint'ами сервера:
	//   GET /animation/patch/{gameId}/{token}/structure/{animationId} — SCML XML
	//   GET /animation/patch/{gameId}/{token}/images                  — ZIP (sha256.ext + _files.json) (If-Modified-Since)
	//   GET /animation/patch/{gameId}/{token}/prefabs?since=<ts>      — map prefab.name → animation_id
	//   GET /animation/patch/{gameId}/{token}/animations?since=<ts>   — map animation_id → updated_ts (delta)
	// Маппинг action→clip (entity_actions) приходит инлайном в ответе /api/game/{gameId}/auth
	// и хранится в ConnectController.entity_actions (session state) — сюда передаётся параметром GetClipName.
	//
	// Дедупликация: несколько Prefab одной игры могут ссылаться на одну Animation — качаем её один раз.
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/animations/
	//   images/{sha256}.{ext}         — распакованные из ZIP /images
	//   structures/{animationId}.xml  — кеш SCML XML от /structure
	//   files.json                    — animationId → idx → sha256.ext (берётся из _files.json в ZIP)
	//   library.json                  — prefab.name → animation_id (мержится по диффам ?since)
	//   sync.json                     — manifest (archive_last_modified, library_since, structures_since)
	public static class AnimationCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE        = "sync.json";
		private const string LIBRARY_FILE         = "library.json";
		private const string FILES_FILE           = "files.json";
		private const string ARCHIVE_FILES_ENTRY  = "_files.json";
		private const string IMAGES_DIR           = "images";
		private const string STRUCT_DIR           = "structures";

		private static SyncManifest _manifest;
		private static Dictionary<string, PrefabEntry> _library;                     // prefab.name → {animation, entity}
		private static Dictionary<int, Dictionary<int, string>> _files;              // animationId → idx → sha256.ext
		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		[Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public int    library_since;
			public int    structures_since;
		}

		[Serializable]
		public class PrefabEntry
		{
			public int animation;
			public int entity;
		}

		[Serializable]
		private class LibraryResponse
		{
			public Dictionary<string, PrefabEntry> items;  // prefab.name → {animation, entity} (диффы по ?since)
			public int                             now;
		}

		[Serializable]
		private class AnimationsResponse
		{
			public List<int> items;  // список animation_id, обновлённых с ?since
			public int       now;
		}

		[Serializable]
		private class StructureResponse
		{
			public string data;  // base64(gzip(xml))
		}

		// Извлекает текст серверной ошибки из body ({"error":"..."} — exceptionHandler и явные 4xx)
		// при неуспешном HTTP-запросе. Fallback — код+generic error от UnityWebRequest.
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

		private static string ImagesPath(int gameId)          => Path.Combine(AnimationsPath(gameId), IMAGES_DIR);
		private static string StructPath(int gameId)          => Path.Combine(AnimationsPath(gameId), STRUCT_DIR);
		private static string ManifestPath(int gameId)        => Path.Combine(AnimationsPath(gameId), MANIFEST_FILE);
		private static string LibraryPath(int gameId)         => Path.Combine(AnimationsPath(gameId), LIBRARY_FILE);
		private static string FilesPath(int gameId)           => Path.Combine(AnimationsPath(gameId), FILES_FILE);

		// Загружает manifest + library + files с диска. Идемпотентно.
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
				_library = new Dictionary<string, PrefabEntry>();
				if (File.Exists(lp))
				{
					// Старый формат (string→int) → на новый не парсится — catch, reset since=0 для полной пересинхронизации.
					try { _library = JsonConvert.DeserializeObject<Dictionary<string, PrefabEntry>>(File.ReadAllText(lp)) ?? new Dictionary<string, PrefabEntry>(); }
					catch { _library = new Dictionary<string, PrefabEntry>(); _manifest.library_since = 0; }
				}
			}
			if (_files == null)
			{
				string fp = FilesPath(gameId);
				_files = new Dictionary<int, Dictionary<int, string>>();
				if (File.Exists(fp))
				{
					try { _files = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<int, string>>>(File.ReadAllText(fp)) ?? new Dictionary<int, Dictionary<int, string>>(); }
					catch { _files = new Dictionary<int, Dictionary<int, string>>(); }
				}
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

		private static void SaveFiles(int gameId)
		{
			File.WriteAllText(FilesPath(gameId), JsonConvert.SerializeObject(_files));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полный сброс локального кеша анимаций игры: manifest, library, structures/, images/.
		// Вызывается при обнаружении рассинхронизации (например, сервер отвечает 404 на animation_id из library).
		// После сброса следующий SyncAll пересобирает всё с нуля.
		public static void ResetCache(int gameId)
		{
			Debug.LogWarning("AnimationCache: сброс кеша игры " + gameId);
			_manifest = new SyncManifest();
			_library = new Dictionary<string, PrefabEntry>();
			_files = new Dictionary<int, Dictionary<int, string>>();
			_spriteCache.Clear();

			string root = AnimationsPath(gameId);
			try
			{
				if (File.Exists(ManifestPath(gameId)))       File.Delete(ManifestPath(gameId));
				if (File.Exists(LibraryPath(gameId)))        File.Delete(LibraryPath(gameId));
				if (File.Exists(FilesPath(gameId)))          File.Delete(FilesPath(gameId));
				if (Directory.Exists(StructPath(gameId))) Directory.Delete(StructPath(gameId), true);
				if (Directory.Exists(ImagesPath(gameId))) Directory.Delete(ImagesPath(gameId), true);
			}
			catch (Exception ex) { Debug.LogWarning("AnimationCache: ошибка при сбросе кеша: " + ex.Message); }

			Directory.CreateDirectory(StructPath(gameId));
			Directory.CreateDirectory(ImagesPath(gameId));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полная синхронизация перед входом в игру: архив картинок + library + delta анимаций + предзагрузка структур. Вызывать ДО Connect.
		// entity_actions в SyncAll не качается — приходит инлайном в ответе /auth и хранится в ConnectController.entity_actions.
		public static IEnumerator SyncAll(string host, int gameId, string token, Action<string> onError = null)
		{
			EnsureLoaded(gameId);
			yield return SyncImagesArchive(host, gameId, token, onError);
			yield return SyncLibrary(host, gameId, token, onError);
			var delta = new HashSet<int>();
			yield return SyncAnimations(host, gameId, token, delta, onError);
			yield return PreFetchStructures(host, gameId, token, delta, onError);
		}

		// Список анимаций игры, обновлённых с прошлого захода (Animation.updated >= structures_since).
		// Не качает сами структуры — лишь сигнал для PreFetchStructures, какие из library надо
		// принудительно обновить (даже если локальный structures/{id}.json уже есть).
		private static IEnumerator SyncAnimations(string host, int gameId, string token, HashSet<int> delta, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/animations?since=" + _manifest.structures_since;
			Debug.Log("Запрашиваю список изменившихся анимаций "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache animations: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			AnimationsResponse resp;
			try { resp = JsonConvert.DeserializeObject<AnimationsResponse>(text); }
			catch (Exception ex) { onError?.Invoke("AnimationCache animations parse: " + ex.Message); yield break; }

			if (resp.items != null)
				foreach (var id in resp.items)
					delta.Add(id);

			Debug.Log("AnimationCache: delta анимаций — " + delta.Count);
			_manifest.structures_since = resp.now;
			SaveManifest(gameId);
		}

		// Предзагрузка SCML-структур: качаем, если animation в delta (сервер сообщил об обновлении)
		// ИЛИ если локального кеша ещё нет (первый раз / новый prefab). Для delta — удаляем старый кеш-файл,
		// чтобы GetStructure пошёл в сеть (иначе отдаст устаревшее из кеша). Дедупликация по animation_id.
		private static IEnumerator PreFetchStructures(string host, int gameId, string token, HashSet<int> delta, Action<string> onError)
		{
			var seen = new HashSet<int>();
			foreach (var kv in _library)
			{
				int animationId = kv.Value.animation;
				if (!seen.Add(animationId)) continue;
				string structFile = Path.Combine(StructPath(gameId), animationId + ".json");
				bool inDelta = delta.Contains(animationId);
				if (!inDelta && File.Exists(structFile)) continue;
				if (inDelta && File.Exists(structFile)) File.Delete(structFile);
				yield return GetStructure(host, gameId, kv.Key, token, (xml, files, err) =>
				{
					if (err != null) onError?.Invoke(err);
				});
			}
		}

		// Архив: GET с If-Modified-Since. 304 → ничего. 200 → unzip в images/.
		private static IEnumerator SyncImagesArchive(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/images";
			Debug.Log("Запрашиваю архив картинок анимаций "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.archive_last_modified))
				req.SetRequestHeader("If-Modified-Since", _manifest.archive_last_modified);
			req.downloadHandler = new DownloadHandlerBuffer();

			yield return req.SendWebRequest();

			if (req.responseCode == 304)
			{
				Debug.Log("AnimationCache: архив картинок актуален (кеш)");
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
				onError?.Invoke("AnimationCache archive: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string lastMod = req.GetResponseHeader("Last-Modified");
			int extractedCount = 0;

			if(req.downloadedBytes>0)
			{
				byte[] zipBytes = req.downloadHandler.data;
				bool filesExtracted = false;
				try
				{
					string imagesDir = ImagesPath(gameId);
					using (var ms = new MemoryStream(zipBytes))
					using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
					{
						foreach (var entry in zip.Entries)
						{
							if (string.IsNullOrEmpty(entry.Name)) continue;
							// _files.json — не картинка, а маппинг animationId → idx → sha256.ext. Кладём в память + на диск отдельно.
							if (entry.Name == ARCHIVE_FILES_ENTRY)
							{
								using (var src = entry.Open())
								using (var sr  = new StreamReader(src))
								{
									string json = sr.ReadToEnd();
									_files = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<int, string>>>(json)
									         ?? new Dictionary<int, Dictionary<int, string>>();
								}
								filesExtracted = true;
								continue;
							}
							string dest = Path.Combine(imagesDir, entry.Name);
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
					onError?.Invoke("AnimationCache archive unzip: " + ex.Message);
					yield break;
				}
				if (filesExtracted) SaveFiles(gameId);
			}
			req.Dispose();
			
			Debug.Log("AnimationCache: архив картинок обновлён, распаковано " + extractedCount + " файлов");
			_manifest.archive_last_modified = lastMod;
			SaveManifest(gameId);
			_spriteCache.Clear(); // новые картинки могли появиться
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Library: GET ?since=<library_since>. Ответ мержится в _library по animation_id.
		private static IEnumerator SyncLibrary(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/prefabs?since=" + _manifest.library_since;
			Debug.Log("Запрашиваю список префабов "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache library: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			LibraryResponse resp;
			try { resp = JsonConvert.DeserializeObject<LibraryResponse>(text); }
			catch (Exception ex) { onError?.Invoke("AnimationCache library parse: " + ex.Message); yield break; }

			int libCount = 0;
			if (resp.items != null)
			{
				foreach (var kv in resp.items)
				{
					_library[kv.Key] = kv.Value;
					libCount++;
				}
			}
			Debug.Log("AnimationCache: библиотека обновлена, получено " + libCount + " префабов");
			_manifest.library_since = resp.now;
			SaveManifest(gameId);
			SaveLibrary(gameId);
		}

		// Резолв action → имя SCML-клипа для данного prefab.
		// null если: library/entityActions ещё не загружены, prefab неизвестен, маппинга на action нет.
		// Вызывающий (EntityModel.SetData) делает fallback на action как имя клипа при null.
		// entityActions — session-словарь из ConnectController.entity_actions (приходит в /auth).
		public static string GetClipName(string prefab, string action, Dictionary<int, Dictionary<string, string>> entityActions)
		{
			if (_library == null || string.IsNullOrEmpty(prefab)) return null;
			if (!_library.TryGetValue(prefab, out PrefabEntry p)) return null;
			if (entityActions == null) return null;
			if (!entityActions.TryGetValue(p.entity, out var map) || map == null) return null;
			return map.TryGetValue(action, out var clip) ? clip : null;
		}

		// SCML XML анимации. Ключ кеша/URL — Animation.id (шерится между Prefab-ами).
		// Если локальный кеш есть — читаем с диска (свежесть проверяется через /animations до вызова).
		// Иначе — GET /structure/{id} → распаковываем base64+gzip, сохраняем, возвращаем.
		// Маппинг SCML-file-idx → sha256.ext подтягивается из _files (прилетает в /images как _files.json).
		public static IEnumerator GetStructure(string host, int gameId, string prefab, string token,
			Action<string, Dictionary<int, string>, string> callback)
		{
			if (!_library.TryGetValue(prefab, out PrefabEntry entry))
			{
				callback(null, null, "AnimationCache structure: Prefab '" + prefab + "' отсутствует в library");
				yield break;
			}
			int animationId = entry.animation;
			string structFile = Path.Combine(StructPath(gameId), animationId + ".xml");

			if (!_files.TryGetValue(animationId, out var filesForAnim))
			{
				// Должен прийти из /images вместе с архивом. Если нет — сигналим ошибку, вызывающий сделает ResetCache.
				callback(null, null, "AnimationCache structure: отсутствует files для animation " + animationId + " (архив устарел?)");
				yield break;
			}

			if (File.Exists(structFile))
			{
				Debug.Log("AnimationCache: структура " + animationId + " из кеша");
				try
				{
					string cachedXml = File.ReadAllText(structFile);
					callback(cachedXml, filesForAnim, null);
				}
				catch (Exception ex) { callback(null, null, "AnimationCache cache read: " + ex.Message); }
				yield break;
			}

			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/structure/" + animationId;
			Debug.Log("Запрашиваю анимацию "+animationId+" (prefab "+prefab+") "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				callback(null, null, "AnimationCache structure " + animationId + ": " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string body = req.downloadHandler.text;
			req.Dispose();

			StructureResponse wrapper;
			try { wrapper = JsonConvert.DeserializeObject<StructureResponse>(body); }
			catch (Exception ex) { callback(null, null, "AnimationCache structure wrapper: " + ex.Message); yield break; }

			string xml;
			try
			{
				byte[] gzipped = Convert.FromBase64String(wrapper.data);
				using (var src = new MemoryStream(gzipped))
				using (var gz  = new GZipStream(src, CompressionMode.Decompress))
				using (var dst = new MemoryStream())
				{
					gz.CopyTo(dst);
					xml = Encoding.UTF8.GetString(dst.ToArray());
				}
			}
			catch (Exception ex) { callback(null, null, "AnimationCache structure decode: " + ex.Message); yield break; }

			File.WriteAllText(structFile, xml);
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif

			Debug.Log("AnimationCache: структура " + animationId + " скачана с сервера");
			callback(xml, filesForAnim, null);
		}

		// Sprite по имени файла ("sha256.ext"): грузится из локального кеша, кешируется в памяти.
		public static Sprite GetSprite(int gameId, string fileName)
		{
			if (string.IsNullOrEmpty(fileName)) return null;
			// Unity-объект в словаре может быть уничтожен Resources.UnloadUnusedAssets при переходе сцен
			// (static-ссылка C# живёт, но нативный ресурс снесён). Проверяем через == null и пересоздаём.
			if (_spriteCache.TryGetValue(fileName, out Sprite cached) && cached != null) return cached;

			string path = Path.Combine(ImagesPath(gameId), fileName);
			if (!File.Exists(path))
			{
				throw new Exception("AnimationCache: отсутствует картинка " + fileName + " (архив устарел?)");
			}

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			tex.LoadImage(bytes);
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			// PixelsPerUnit должен совпадать с SpriterDotNetBehaviour.Ppu (=100), иначе UnityAnimator.ApplySpriteTransform
			// считает info.X/info.Y в разных масштабах для разных спрайтов — и части персонажа разлетаются.
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), 100f, 0, SpriteMeshType.FullRect);
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_spriteCache[fileName] = s;
			Debug.Log("AnimationCache: спрайт " + fileName + " загружен с диска");
			return s;
		}

		// Все имена префабов игры (из /prefabs). Используется для создания GameObject'ов по префабам.
		public static IEnumerable<string> GetPrefabs()
			=> _library != null ? _library.Keys : System.Linq.Enumerable.Empty<string>();

		// true если Prefab с таким именем существует в серверном списке.
		public static bool HasPrefab(string name)
			=> _library != null && _library.ContainsKey(name);
	}
}
