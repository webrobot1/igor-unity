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
	// Content-addressable кеш анимаций игры. Endpoint'ы:
	//   GET /animation/patch/{gameId}/{token}/prefabs        — полный список prefab.name → {animation, entity, size, h_mirror}
	//   GET /animation/patch/{gameId}/{token}/animations     — полный список animation_id → updated_timestamp
	//   GET /animation/patch/{gameId}/{token}/animations/{id} — SCML XML (клиент качает только если updated отличается)
	//   GET /animation/patch/{gameId}/{token}/images          — ZIP (sha256.ext + _files.json) (If-Modified-Since)
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/animations/
	//   images/{sha256}.{ext}         — распакованные из ZIP /images
	//   structures/{animationId}.xml  — кеш SCML XML
	//   files.json                    — animationId → idx → sha256.ext (берётся из _files.json в ZIP)
	//   library.json                  — prefab.name → PrefabEntry (полная замена при каждом sync)
	//   sync.json                     — manifest (archive_last_modified, animation_versions: {id: ts})
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

		/// <summary>
		/// Tight-rect спрайта в sprite-local мировых единицах (относительно pivot=(0,0), т.е. левый-нижний угол rect).
		/// Берётся из <see cref="Sprite.vertices"/> — при Tight-меше Unity туда кладёт вершины полигона вокруг
		/// непрозрачных пикселей. <see cref="Sprite.bounds"/> не подходит: он считает всю sprite.rect целиком,
		/// и PNG с прозрачными полями искажают измерения SpriterPostImportAdjuster / fallback-normalize.
		/// Требует Tight-меша — у Spriter-спрайтов это задаётся в <see cref="Sprite.Create"/> ниже,
		/// у ассетов Unity — через TextureImporter.spriteMeshType=Tight (см. README / raw .meta files).
		/// </summary>
		public static bool TryGetTightRect(Sprite s, out Rect rect)
		{
			if (s == null) { rect = default; return false; }
			var verts = s.vertices;
			if (verts == null || verts.Length == 0) { rect = default; return false; }
			float minX = verts[0].x, maxX = verts[0].x, minY = verts[0].y, maxY = verts[0].y;
			for (int i = 1; i < verts.Length; i++)
			{
				if (verts[i].x < minX) minX = verts[i].x;
				if (verts[i].x > maxX) maxX = verts[i].x;
				if (verts[i].y < minY) minY = verts[i].y;
				if (verts[i].y > maxY) maxY = verts[i].y;
			}
			rect = new Rect(minX, minY, maxX - minX, maxY - minY);
			return true;
		}

		[Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public Dictionary<int, long> animation_versions = new Dictionary<int, long>();
		}

		[Serializable]
		public class PrefabEntry
		{
			public string prefab;
			public int animation;
			public int entity;
			/// <summary>
			/// Высота «тела» персонажа в scml-единицах (per-prefab константа, приходит с /prefabs
			/// вместе с animation и entity, задаётся в админке при конфигурации prefab'а).
			/// Клиент нормализует Spriter-сущность так, чтобы size * final_scale = 1 клетка.
			/// Если null (поле не задано на сервере) — fallback на автозамер bounds за N кадров.
			/// </summary>
			public float? size;
			public bool h_mirror;
			public string image;

			public bool IsImage => !string.IsNullOrEmpty(image);
		}

		// Возвращает per-prefab "size" (max scml-размер body) из library, если задан, иначе null.
		// Используется SpriterPostImportAdjuster для точной нормализации размера без замера bounds.
		// Контракт: вызывать ТОЛЬКО после того как SyncAll отработал (что гарантировано
		// SigninController.LoadMain — он awaitит SyncAll до ConnectController.Connect, поэтому
		// любой WS-спавн приходит уже с загруженным _library). Вызов до SyncAll — это баг вызывающей стороны,
		// поэтому бросаем exception вместо тихого null: null-возврат от «prefab без size» и null от «library
		// не загружена» — разные вещи, и глотать второе опасно (SpriterPostImportAdjuster уйдёт в fallback
		// median-sampling, замаскировав проблему timing'а).
		public static float? GetPrefabSize(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabSize вызван до SyncAll (_library == null). prefab=" + prefab + ". Вызывайте только после завершения SigninController.LoadMain.");
			return _library.TryGetValue(prefab, out PrefabEntry e) ? e.size : (float?)null;
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
			string mp = ManifestPath(gameId);
			// Рассинхрон disk↔RAM (sync.json удалён внешним кодом / ручной очисткой кэша, но _manifest
			// в RAM держит timestamp прошлого архива) — нарушение контракта: AnimationCacheService —
			// единственный владелец этих файлов. По CLAUDE.md политике падаем громко, чтобы виновный
			// код был починен у источника, а не маскировался силент-ресетом.
			if (_manifest != null && !File.Exists(mp))
				throw new InvalidOperationException("AnimationCache: sync.json отсутствует на диске, но _manifest загружен в RAM. Кто-то очистил кэш мимо ResetCache() — почините источник.");
			if (_manifest == null)
			{
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
					// Старый формат (string→int) не парсится на новый — catch, начнём с пустого; SyncLibrary всё равно перезальёт целиком.
					try { _library = JsonConvert.DeserializeObject<Dictionary<string, PrefabEntry>>(File.ReadAllText(lp)) ?? new Dictionary<string, PrefabEntry>(); }
					catch { _library = new Dictionary<string, PrefabEntry>(); }
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
			// null, а не пустые объекты: EnsureLoaded проверяет «_manifest != null && !File.Exists(mp)»
			// и бросает исключение. Если оставить здесь new SyncManifest() — следующий SyncAll в той же
			// сессии (повторный логин после Error) упадёт на этом guard'е.
			_manifest = null;
			_library = null;
			_files = null;
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

		// Полный список animation_id → updated. Сравниваем с локальным, определяем delta, удаляем лишние.
		private static IEnumerator SyncAnimations(string host, int gameId, string token, HashSet<int> delta, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/animations";
			Debug.Log("Запрашиваю список анимаций " + url);

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

			Dictionary<int, long> serverVersions;
			try { serverVersions = JsonConvert.DeserializeObject<Dictionary<int, long>>(text); }
			catch (Exception ex) { onError?.Invoke("AnimationCache animations parse: " + ex.Message); yield break; }

			if (serverVersions == null) serverVersions = new Dictionary<int, long>();

			foreach (var kv in serverVersions)
			{
				if (!_manifest.animation_versions.TryGetValue(kv.Key, out long localTs) || localTs < kv.Value)
					delta.Add(kv.Key);
			}

			// Удалить локальные анимации которых больше нет на сервере
			var toRemove = new List<int>();
			foreach (var id in _manifest.animation_versions.Keys)
				if (!serverVersions.ContainsKey(id))
					toRemove.Add(id);
			foreach (var id in toRemove)
			{
				_manifest.animation_versions.Remove(id);
				string structFile = Path.Combine(StructPath(gameId), id + ".xml");
				if (File.Exists(structFile)) File.Delete(structFile);
			}

			Debug.Log("AnimationCache: delta " + delta.Count + " анимаций, удалено " + toRemove.Count);
			SaveManifest(gameId);
		}

		// Предзагрузка SCML-структур: качаем если в delta (updated отличается) или нет локального файла.
		private static IEnumerator PreFetchStructures(string host, int gameId, string token, HashSet<int> delta, Action<string> onError)
		{
			var seen = new HashSet<int>();
			foreach (var kv in _library)
			{
				int animationId = kv.Value.animation;
				if (!seen.Add(animationId)) continue;
				string structFile = Path.Combine(StructPath(gameId), animationId + ".xml");
				bool inDelta = delta.Contains(animationId);
				if (!inDelta && File.Exists(structFile)) continue;
				if (inDelta && File.Exists(structFile)) File.Delete(structFile);
				yield return GetStructure(host, gameId, kv.Key, token, (xml, files, err) =>
				{
					if (err != null) onError?.Invoke(err);
				});
			}
			SaveManifest(gameId);
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

		// Полный список prefab'ов — заменяет _library целиком. Клиент видит удалённые prefab'ы.
		private static IEnumerator SyncLibrary(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/prefabs";
			Debug.Log("Запрашиваю список префабов " + url);

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

			Dictionary<string, PrefabEntry> parsed;
			try { parsed = JsonConvert.DeserializeObject<Dictionary<string, PrefabEntry>>(text); }
			catch (Exception ex) { onError?.Invoke("AnimationCache library parse: " + ex.Message); yield break; }

			_library = parsed ?? new Dictionary<string, PrefabEntry>();
			Debug.Log("AnimationCache: библиотека загружена, " + _library.Count + " префабов");
			SaveLibrary(gameId);
		}

		// Резолв action → имя SCML-клипа для данного prefab с учётом направления (angle).
		// Возвращает (clipName, flipX). flipX=true если clip получен через h_mirror (горизонтальное зеркало).
		// null clipName если: library/entityActions ещё не загружены, prefab неизвестен, маппинга на action нет.
		// Вызывающий (EntityModel.SetData) делает fallback на action как имя клипа при null.
		public static (string clipName, bool flipX) GetClipName(
			string prefab, string action, float forwardX, float forwardY,
			Dictionary<int, Dictionary<string, Dictionary<string, string>>> entityActions)
		{
			if (_library == null || string.IsNullOrEmpty(prefab)) return (null, false);
			if (!_library.TryGetValue(prefab, out PrefabEntry p)) return (null, false);
			if (p.IsImage) return (null, false);
			if (entityActions == null) return (null, false);
			if (!entityActions.TryGetValue(p.entity, out var actionMap) || actionMap == null) return (null, false);
			if (!actionMap.TryGetValue(action, out var angleMap) || angleMap == null) return (null, false);

			// Единственный ключ "" = clip без направления (backward compat)
			if (angleMap.Count == 1 && angleMap.ContainsKey(""))
				return (angleMap[""], false);

			float targetAngle = Mathf.Atan2(forwardY, forwardX) * Mathf.Rad2Deg;
			if (targetAngle < 0) targetAngle += 360f;

			string bestClip = null;
			float bestDist = 360f;
			bool bestFlip = false;

			foreach (var kv in angleMap)
			{
				if (kv.Key == "") continue;
				if (!int.TryParse(kv.Key, out int clipAngle)) continue;

				float dist = Mathf.Abs(Mathf.DeltaAngle(targetAngle, clipAngle));
				if (dist < bestDist)
				{
					bestDist = dist;
					bestClip = kv.Value;
					bestFlip = false;
				}

				if (p.h_mirror)
				{
					int mirrorAngle = (360 - clipAngle) % 360;
					float mirrorDist = Mathf.Abs(Mathf.DeltaAngle(targetAngle, mirrorAngle));
					if (mirrorDist < bestDist)
					{
						bestDist = mirrorDist;
						bestClip = kv.Value;
						bestFlip = true;
					}
				}
			}

			// Fallback на без-направления если ничего не нашли
			if (bestClip == null && angleMap.TryGetValue("", out var fallback))
				return (fallback, false);

			return (bestClip, bestFlip);
		}

		// Простой резолв без направления (backward compat для вызовов где forward неважен, например idle в importer)
		public static string GetClipNameSimple(
			string prefab, string action,
			Dictionary<int, Dictionary<string, Dictionary<string, string>>> entityActions)
		{
			if (_library == null || string.IsNullOrEmpty(prefab)) return null;
			if (!_library.TryGetValue(prefab, out PrefabEntry p)) return null;
			if (p.IsImage) return null;
			if (entityActions == null) return null;
			if (!entityActions.TryGetValue(p.entity, out var actionMap) || actionMap == null) return null;
			if (!actionMap.TryGetValue(action, out var angleMap) || angleMap == null) return null;
			// Берём первый попавшийся clip (предпочитая без-направления)
			if (angleMap.TryGetValue("", out var clip)) return clip;
			foreach (var kv in angleMap) return kv.Value;
			return null;
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
			if (entry.IsImage)
			{
				callback(null, null, null);
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

			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/animations/" + animationId;
			Debug.Log("Запрашиваю анимацию " + animationId + " (prefab " + prefab + ") " + url);

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
			_manifest.animation_versions[animationId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif

			Debug.Log("AnimationCache: структура " + animationId + " скачана с сервера");
			callback(xml, filesForAnim, null);
		}

		// Wrapper над GetSprite: на любой сбой (LoadImage / отсутствие файла) инвалидирует битый кеш
		// (удаляет PNG и сбрасывает archive_last_modified — иначе следующий sync получит 304 и файл
		// не перекачается) и бросает Exception с контекстом. Вызыватель оборачивает в try/catch и
		// сам решает что делать (обычно — ConnectController.Error + оставить sprite=null).
		public static Sprite TryGetSprite(int gameId, string fileName)
		{
			try { return GetSprite(gameId, fileName); }
			catch (Exception ex)
			{
				if (!string.IsNullOrEmpty(fileName))
				{
					string path = Path.Combine(ImagesPath(gameId), fileName);
					try { if (File.Exists(path)) File.Delete(path); } catch { /* нет прав / уже удалён */ }
					_spriteCache.Remove(fileName);
					if (_manifest != null)
					{
						_manifest.archive_last_modified = null;
						SaveManifest(gameId);
					}
				}
				throw new Exception("AnimationCache: битый image '" + fileName + "' удалён из кеша, перекачается на следующем sync — " + ex.Message, ex);
			}
		}

		// Sprite по имени файла ("sha256.ext"): грузится из локального кеша, кешируется в памяти.
		// pivot=(0.5, 0.5) — центр. Image-only items так центруются на клетке entity, а Spriter-части
		// корректны: UnityAnimator.ApplySpriteTransform компенсирует sprite.pivot формулой
		// (spritePivotX - info.PivotX) * size — то есть работает с любым pivot спрайта.
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
			// LoadImage возвращает false на битых PNG или PNG, которые Unity не умеет парсить
			// (наблюдалось на валидных файлах с большими iTXt-чанками XMP-метаданных от Photoshop).
			// Кидаем — вызыватель решит, удалить файл из кеша / сбросить manifest / показать Error.
			if (!tex.LoadImage(bytes))
				throw new Exception("AnimationCache: Unity.Texture2D.LoadImage не справился с " + fileName + " (" + bytes.Length + " байт)");
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			// PixelsPerUnit должен совпадать с SpriterDotNetBehaviour.Ppu (=100), иначе UnityAnimator.ApplySpriteTransform
			// считает info.X/info.Y в разных масштабах для разных спрайтов — и части персонажа разлетаются.
			// SpriteMeshType.Tight — чтобы Sprite.bounds (и SpriteRenderer.bounds) отсекали прозрачные поля PNG.
			// Критично для SpriterPostImportAdjuster: без этого персонажи с «воздухом» вокруг контента в своих
			// PNG-ах измерялись бы завышенными bounds и нормализовались бы в клетке мельче остальных.
			// Рендеринг FullRect vs Tight отличается только числом треугольников меша — визуально идентично.
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.Tight);
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

		// Имя файла картинки (sha256.ext) если prefab — image-only, иначе null.
		public static string GetPrefabImage(string name)
			=> _library != null && _library.TryGetValue(name, out PrefabEntry e) && e.IsImage ? e.image : null;

		// Готовый Sprite иконки для image-prefab. null — если prefab не image (animation
		// или отсутствует в library) или картинка битая (битый кеш чистится TryGetSprite,
		// перекачается на следующем sync). Используется UI-кодом (Spell, Item) — они передают
		// BaseController.GAME_ID (public static, глобальный конфиг проекта).
		// Контракт: вызывать только после SigninController.LoadMain (т.е. _library != null).
		public static Sprite GetPrefabSprite(int gameId, string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabSprite вызван до SyncAll (_library == null). prefab=" + prefab);
			string imageFile = GetPrefabImage(prefab);
			if (imageFile == null) return null;
			try { return TryGetSprite(gameId, imageFile); }
			catch (Exception ex) { Debug.LogWarning("GetPrefabSprite '" + prefab + "': " + ex.Message); return null; }
		}
	}
}
