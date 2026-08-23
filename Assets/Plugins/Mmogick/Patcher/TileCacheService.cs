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
		// Нарисованные миниатюры карт для обзорной карты мира: рисуются из тайлов один раз на версию данных.
		private const string WORLDMAP_DIR = "worldmap";

		// Версия формата локального кеша — И меты тайлсетов (TilesetMeta/Tile/TileObjectGroup/TileObject),
		// И разбора карт (Map). Бамп при смене формы любой из этих структур → EnsureLoaded форсит полный
		// refetch: версии наборов и карт очищаются, скачанные карты удаляются. Состав полей — не единственная
		// ось формы: значение внутри прежней структуры сменило тип (словарь стал скаляром либо наоборот) —
		// бамп тот же, разбор падает на лежалой записи так же. Отметка свежести с сервера
		// строится по датам данных и смену формата не выражает: без бампа набор либо карта с прежней датой
		// не перекачается, а на диске останется кеш прежней формы. Карту при этом мало пометить устаревшей —
		// пере-скачивается она лишь при заходе игрока на неё, а читают кеш и те, кто карту сейчас не грузит.
		// v2: TileObjectGroup.class и TileObject.visible — до них разбор меты падал, кеш затирался пустым.
		// v3: Map.world — по нему из кеша отбираются карты текущего мира.
		// v4: CachedMap.hasOpenworldPosition — новое bool-поле; у записей, лежавших в sync.json ДО этой
		// версии, его нет в JSON вовсе, и десериализатор молча кладёт туда false (C#-дефолт bool) —
		// ранее закешированные карты ОТКРЫТОГО мира читались бы как интерьеры и выпадали из мозаики.
		private const int CACHE_SCHEMA_VERSION = 4;

		private static SyncManifest _manifest;
		private static Dictionary<string, TilesetMeta> _tilesets;
		private static Dictionary<string, Tile> _meta;
		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		// Шапка скачанной карты: мир, имя и место в открытом мире. Держится в манифесте, а не читается из
		// самих карт: обзорной карте мира нужны шапки ВСЕХ скачанных карт, а разбор их файлов целиком —
		// сотни килобайт тайлов ради шести полей. Пишется при скачивании карты из её же файла, потому
		// расходиться с ним не может; при смене формата кеша уходит вместе с картами (cache_schema_version).
		[System.Serializable]
		public class CachedMap
		{
			public int world;
			public string name;

			// true — карта стоит в раскладке открытого мира, x/y её место там. false — интерьер/подземелье
			// без раскладки: x=0, y=0 условны (соседей по определению нет), запись существует лишь чтобы
			// миникарта и обзорная карта могли показать карту, пока игрок в НЕЙ (см. фильтр у потребителей
			// в MinimapController.UpdateMinimapMaps / WorldMapController.BuildWorldMap) — несколько таких
			// карт делят один world (банк, кузница, тюрьма одного города), и без фильтра по mapId==player.map
			// их x=0,y=0 накладывались бы друг на друга либо подменяли друг друга по порядку обхода Dictionary.
			public bool hasOpenworldPosition;
			public int x;
			public int y;
			public int width;
			public int height;

			// Отпечаток данных, по которым нарисована миниатюра карты (см. WorldMapStamp): версия самой карты
			// плюс версия архива графики. Разошёлся с текущим — картинка устарела, её перерисовывают: карту
			// могли перерисовать в редакторе, а тайлы — перезалить, и второе миниатюру меняет так же.
			public string render;
		}

		[System.Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public Dictionary<string, long> tileset_versions = new Dictionary<string, long>();
			public Dictionary<int, string> map_versions = new Dictionary<int, string>();
			public Dictionary<int, CachedMap> maps = new Dictionary<int, CachedMap>();

			// Версия формата кеша меты на диске. При несовпадении с CACHE_SCHEMA_VERSION EnsureLoaded чистит
			// tileset_versions (разовый полный refetch меты уже в новом формате).
			public int cache_schema_version;
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
		private static string WorldMapPath(int gameId) => Path.Combine(GamePath(gameId), WORLDMAP_DIR);

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
			string mp = ManifestPath(gameId);
			// Рассинхрон disk↔RAM (sync.json удалён внешним кодом / ручной очисткой кэша, но _manifest
			// в RAM держит timestamp прошлого архива) — нарушение контракта: TileCacheService —
			// единственный владелец этих файлов. По CLAUDE.md политике падаем громко, чтобы виновный
			// код был починен у источника, а не маскировался силент-ресетом.
			if (_manifest != null && !File.Exists(mp))
				throw new InvalidOperationException("TileCache: sync.json отсутствует на диске, но _manifest загружен в RAM. Кто-то очистил кэш мимо ResetCache() — почините источник.");
			if (_manifest == null)
			{
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();

				// Миграция схемы кеша: разбираемые структуры расширились, а сервер отдаёт версию по датам
				// ДАННЫХ — набор либо карта с прежней датой не перекачались бы, и на диске остался бы кеш
				// прежней формы (в т.ч. пустой, записанный когда разбор падал). Разово форсим полный refetch.
				// Карты сносим ФАЙЛАМИ, не одними версиями: файл прежней формы иначе доживает до захода игрока
				// на эту карту, а читают кеш и те, кому карта сейчас не грузится (обзорная карта мира).
				if (_manifest.cache_schema_version != CACHE_SCHEMA_VERSION)
				{
					_manifest.cache_schema_version = CACHE_SCHEMA_VERSION;
					_manifest.tileset_versions.Clear();
					_manifest.map_versions.Clear();
					_manifest.maps.Clear();
					if (Directory.Exists(MapsPath(gameId)))
						foreach (string file in Directory.GetFiles(MapsPath(gameId), "*.json"))
							File.Delete(file);
					// Миниатюры уходят вместе с картами: рисуются они из тех же файлов, а их отпечаток
					// (WorldMapStamp) остался бы без записи карты и годность картинки было бы нечем мерить.
					if (Directory.Exists(WorldMapPath(gameId)))
						foreach (string file in Directory.GetFiles(WorldMapPath(gameId), "*.png"))
							File.Delete(file);
					SaveManifest(gameId);
				}
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
							// Канон сервера: sandbox-скаляры приходят всегда, включая null (null ≡ дефолт).
							// Ignore не даёт Newtonsoft писать null в не-nullable поля (напр. LayerObject.ellipse) —
							// тот же контракт, что у MapDecodeModel.generate.
							var ts = JsonConvert.DeserializeObject<TilesetMeta>(File.ReadAllText(file), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
							// Версию снимаем вместе с файлом: SyncMeta сверяет ТОЛЬКО её, и оставшаяся запись
							// выдала бы удалённый кеш за актуальный — мета набора не вернулась бы никогда.
							if (_manifest.tileset_versions.Remove(id))
								SaveManifest(gameId);
						}
					}
				}
			}
			if (!Directory.Exists(TilesPath(gameId))) Directory.CreateDirectory(TilesPath(gameId));
			if (!Directory.Exists(TilesetPath(gameId))) Directory.CreateDirectory(TilesetPath(gameId));
			if (!Directory.Exists(MapsPath(gameId))) Directory.CreateDirectory(MapsPath(gameId));
			if (!Directory.Exists(WorldMapPath(gameId))) Directory.CreateDirectory(WorldMapPath(gameId));
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
			// null, а не пустые объекты: EnsureLoaded проверяет «_manifest != null && !File.Exists(mp)»
			// и бросает исключение. Если оставить здесь new SyncManifest() — следующий SyncAll в той же
			// сессии (повторный логин после Error) упадёт на этом guard'е.
			_manifest = null;
			_tilesets = null;
			_meta = null;
			_spriteCache.Clear();

			try
			{
				if (File.Exists(ManifestPath(gameId))) File.Delete(ManifestPath(gameId));
				if (Directory.Exists(TilesPath(gameId)))   Directory.Delete(TilesPath(gameId), true);
				if (Directory.Exists(TilesetPath(gameId))) Directory.Delete(TilesetPath(gameId), true);
				if (Directory.Exists(MapsPath(gameId)))    Directory.Delete(MapsPath(gameId), true);
				if (Directory.Exists(WorldMapPath(gameId))) Directory.Delete(WorldMapPath(gameId), true);
			}
			catch (Exception ex) { Debug.LogWarning("TileCache: ошибка при сбросе кеша: " + ex.Message); }

			Directory.CreateDirectory(TilesPath(gameId));
			Directory.CreateDirectory(TilesetPath(gameId));
			Directory.CreateDirectory(MapsPath(gameId));
			Directory.CreateDirectory(WorldMapPath(gameId));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полная синхронизация перед входом в игру: архив PNG + мета. Вызывать ДО Connect.
		// onProgress — доля принятого архива (0..1) для полосы загрузки; мету не покрывает, она мала.
		public static IEnumerator SyncAll(string host, int gameId, string token, Action<string> onError = null, Action<float> onProgress = null)
		{
			EnsureLoaded(gameId);
			yield return SyncArchive(host, gameId, token, onError, onProgress);
			yield return SyncMeta(host, gameId, token, onError);
		}

		// Архив: GET с If-Modified-Since. 304 → ничего. 200 → unzip в tiles/.
		public static IEnumerator SyncArchive(string host, int gameId, string token, Action<string> onError, Action<float> onProgress = null)
		{
			string url = "http://" + host + "/map/patch/" + gameId + "/" + token + "/archive";
			Debug.Log("Запрашиваю архив изображения карт "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.archive_last_modified))
				req.SetRequestHeader("If-Modified-Since", _manifest.archive_last_modified);
			req.downloadHandler = new DownloadHandlerBuffer();

			// Ждём по кадрам, а не одним yield: только так видно долю принятого, которой живёт полоса загрузки.
			// Актуальный кеш отвечает 304 в первом же кадре — доля до полосы просто не успевает дойти.
			var request = req.SendWebRequest();
			while (!request.isDone)
			{
				onProgress?.Invoke(req.downloadProgress);
				yield return null;
			}

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
					// Канон сервера: sandbox-скаляры приходят всегда, включая null (null ≡ дефолт).
					// Ignore не даёт Newtonsoft писать null в не-nullable поля (напр. LayerObject.ellipse) —
					// тот же контракт, что у MapDecodeModel.generate.
					var ts = JsonConvert.DeserializeObject<TilesetMeta>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
				string cached = File.ReadAllText(mapFile);
				// Шапку из кеш-файла перечитываем, только если её нет: запись пишется при скачивании, а
				// разбор карты ради уже известного стоил бы полного парса файла на каждый заход на карту.
				if (!_manifest.maps.ContainsKey(mapId))
					RememberMap(gameId, mapId, cached);
				callback(cached, null);
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
				_manifest.map_versions[mapId] = newLastMod;
			RememberMap(gameId, mapId, json);   // сам сохраняет манифест — вместе с версией выше
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif

			Debug.Log("TileCache: карта " + mapId + " скачана с сервера");
			callback(json, null);
		}

		// Запоминает шапку карты (мир, имя, место в открытом мире) в манифесте — см. CachedMap. Карта без
		// координат открытого мира (интерьер, подземелье) тоже получает запись, но с hasOpenworldPosition
		// = false: миникарта и обзорная карта мира обязаны показать саму эту комнату, пока игрок в ней, а
		// не оставаться пустыми — но не мозаику из всех интерьеров того же world, что игрок посещал раньше.
		private static void RememberMap(int gameId, int mapId, string json)
		{
			Map map = JsonConvert.DeserializeObject<Map>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

			if (map == null)
				throw new InvalidOperationException("TileCache: карта " + mapId + " не разобрана");

			_manifest.maps[mapId] = new CachedMap {
				world  = map.world,
				name   = map.name,
				hasOpenworldPosition = map.openworldX.HasValue && map.openworldY.HasValue,
				x      = map.openworldX ?? 0,
				y      = map.openworldY ?? 0,
				width  = map.width,
				height = map.height,
			};

			SaveManifest(gameId);
		}

		// Скачанные карты указанного мира — источник обзорной карты: показывается ровно то, что игрок уже
		// видел (кеш пополняют только загруженные карты — своя и смежные). Ключ — id карты.
		public static Dictionary<int, CachedMap> GetWorldMaps(int gameId, int worldId)
		{
			EnsureLoaded(gameId);

			Dictionary<int, CachedMap> result = new Dictionary<int, CachedMap>();
			foreach (KeyValuePair<int, CachedMap> pair in _manifest.maps)
				if (pair.Value.world == worldId)
					result.Add(pair.Key, pair.Value);

			return result;
		}

		// Скачанная карта из кеша (тот же JSON, что отдаёт GetMap) — для отрисовки миниатюры карты, которую
		// игрок сейчас не грузит. Сети не трогает: обзорная карта показывает уже скачанное, а докачивать
		// непосещённое ей нечего. Карты нет в кеше — null.
		public static string ReadCachedMap(int gameId, int mapId)
		{
			EnsureLoaded(gameId);

			string path = Path.Combine(MapsPath(gameId), mapId + ".json");
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}

		// Отпечаток миниатюры карты: версия самой карты, версия архива графики и версия правил отрисовки.
		// Первые две двигает сервер по датам данных, и обе меняют картинку — перерисованный тайл виден на
		// миниатюре так же, как правка самой карты. Третья — наша: смену правил рисования даты данных не
		// выражают, без неё уже нарисованное осталось бы навсегда (php «Свежесть производного артефакта»).
		private static string WorldMapStamp(int mapId)
		{
			_manifest.map_versions.TryGetValue(mapId, out string mapVersion);
			return mapVersion + "|" + _manifest.archive_last_modified + "|v" + WorldMapRenderer.RENDER_VERSION;
		}

		private static string WorldMapImagePath(int gameId, int mapId) => Path.Combine(WorldMapPath(gameId), mapId + ".png");

		// Готовая миниатюра карты либо null — её нет или она устарела (карту или графику перерисовали).
		// Устаревший файл здесь и удаляется: оставленный, он дожил бы до следующей отрисовки и был бы отдан
		// как годный тем, кто отпечаток не сверяет.
		public static byte[] GetWorldMapImage(int gameId, int mapId)
		{
			EnsureLoaded(gameId);

			string path = WorldMapImagePath(gameId, mapId);
			if (!File.Exists(path))
				return null;

			if (!_manifest.maps.TryGetValue(mapId, out CachedMap cached) || cached.render != WorldMapStamp(mapId))
			{
				File.Delete(path);
				return null;
			}

			return File.ReadAllBytes(path);
		}

		// Кладёт нарисованную миниатюру карты в кеш вместе с отпечатком данных, по которым она нарисована.
		public static void SaveWorldMapImage(int gameId, int mapId, byte[] png)
		{
			EnsureLoaded(gameId);

			if (!_manifest.maps.TryGetValue(mapId, out CachedMap cached))
				throw new InvalidOperationException("TileCache: миниатюра карты " + mapId + ", которой нет в кеше");

			File.WriteAllBytes(WorldMapImagePath(gameId, mapId), png);
			cached.render = WorldMapStamp(mapId);
			SaveManifest(gameId);
		}

		// Wrapper над GetSprite: на любой сбой (LoadImage / отсутствие файла) инвалидирует битый кеш
		// (удаляет PNG и сбрасывает archive_last_modified — иначе следующий sync получит 304 и файл
		// не перекачается) и бросает Exception с контекстом. Вызыватель оборачивает в try/catch и
		// сам решает что делать (обычно — ConnectController.Error + оставить sprite=null).
		public static Sprite TryGetSprite(int gameId, string sha256)
		{
			try { return GetSprite(gameId, sha256); }
			catch (Exception ex)
			{
				if (!string.IsNullOrEmpty(sha256))
				{
					string path = Path.Combine(TilesPath(gameId), sha256 + ".png");
					try { if (File.Exists(path)) File.Delete(path); } catch { /* нет прав / уже удалён */ }
					_spriteCache.Remove(sha256);
					if (_manifest != null)
					{
						_manifest.archive_last_modified = null;
						SaveManifest(gameId);
					}
				}
				throw new Exception("TileCache: битый тайл '" + sha256 + "' удалён из кеша, перекачается на следующем sync — " + ex.Message, ex);
			}
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
				throw new Exception("TileCache: Unity.Texture2D.LoadImage не справился с " + sha256 + " (" + bytes.Length + " байт)");
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), tex.width, 0, SpriteMeshType.FullRect);
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_spriteCache[sha256] = s;
			Debug.Log("TileCache: спрайт " + sha256 + " загружен с диска");
			return s;
		}

		// Мета по sha256. Контракт по _meta тот же что у _library в AnimationCacheService — вызывать
		// только после EnsureLoaded/sync (applySprite зовётся при декодировании карты, когда тайлсеты
		// уже загружены). _meta==null — это вызов до загрузки тайлсетов (баг), а не «у тайла нет меты»;
		// тихий null замаскировал бы его (карта молча отрисовалась бы статикой без frame-анимаций).
		// null — только для «у тайла нет frame-меты» (TryGetValue==false).
		public static Tile GetMeta(string sha256)
		{
			if (_meta == null)
				throw new InvalidOperationException("TileCache.GetMeta вызван до загрузки тайлсетов (_meta == null). sha256=" + sha256);
			_meta.TryGetValue(sha256, out Tile m);
			return m;
		}
	}
}
