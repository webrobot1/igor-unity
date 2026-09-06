using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

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

		// Графика тайлов: точка отсчёта в левом нижнем углу, пикселей на единицу — ширина самой текстуры.
		// Вместе это даёт «один тайл = одна клетка мира» при любом разрешении картинки, и на этот pivot
		// опирается раскладка карты (MapDecodeModel.BuildTileMatrix крутит тайл вокруг центра ячейки).
		// Ключ — голый отпечаток тайла, хвост имени файла кеш дописывает сам.
		private static readonly SpriteCache _sprites = new SpriteCache(
			"TileCache", TilesPath, ".png", new Vector2(0, 0), tex => tex.width, SpriteMeshType.FullRect,
			gameId =>
			{
				if (_manifest != null)
				{
					_manifest.archive_last_modified = null;
					SaveManifest(gameId);
				}
			});

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


		private static string TilesPath(int gameId)  => Path.Combine(GameCache.RootPath(gameId), TILES_DIR);
		private static string MapsPath(int gameId)   => Path.Combine(GameCache.RootPath(gameId), MAPS_DIR);
		private static string WorldMapPath(int gameId) => Path.Combine(GameCache.RootPath(gameId), WORLDMAP_DIR);

		private static string ManifestPath(int gameId) => Path.Combine(GameCache.RootPath(gameId), MANIFEST_FILE);
		private static string TilesetPath(int gameId) => Path.Combine(GameCache.RootPath(gameId), TILESET_DIR);
		private static string TilesetFilePath(int gameId, string tilesetId) => Path.Combine(TilesetPath(gameId), tilesetId + ".json");

		private static void EnsureLoaded(int gameId)
		{
			string mp = ManifestPath(gameId);
			GameCache.RequireManifestOnDisk("TileCache", _manifest, mp);
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
					_worldMaps = null;   // набор карт очищен — прежний отбор мира устарел (см. GetWorldMaps)
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

		private static void SaveManifest(int gameId) => GameCache.WriteJson(ManifestPath(gameId), _manifest);

		public static void ResetCache(int gameId)
		{
			Debug.LogWarning("TileCache: сброс кеша игры " + gameId);
			// null, а не пустые объекты: EnsureLoaded проверяет «_manifest != null && !File.Exists(mp)»
			// и бросает исключение. Если оставить здесь new SyncManifest() — следующий SyncAll в той же
			// сессии (повторный логин после Error) упадёт на этом guard'е.
			_manifest = null;
			_tilesets = null;
			_meta = null;
			_worldMaps = null;   // набор карт снесён — прежний отбор мира устарел (см. GetWorldMaps)
			_sprites.Clear();

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
			GameCache.Flush();
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
				onError?.Invoke("TileCache archive: " + GameCache.ExtractError(req));
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
			_sprites.Clear(); // новые PNG могли появиться — сбросим кеш спрайтов
			GameCache.Flush();
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
				onError?.Invoke("TileCache tileset list: " + GameCache.ExtractError(listReq));
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
					Debug.LogWarning("TileCache: ошибка загрузки тайлсета " + tilesetId + ": " + GameCache.ExtractError(req));
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

			GameCache.Flush();
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
				callback(null, "TileCache map " + mapId + ": " + GameCache.ExtractError(req));
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
			GameCache.Flush();

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

			_worldMaps = null;   // набор карт пополнился — прежний отбор мира устарел (см. GetWorldMaps)

			SaveManifest(gameId);
		}

		// Отбор карт одного мира, посчитанный в прошлый раз. Спрашивают его ПОКАДРОВО (радар держит фон по
		// нему каждый кадр), а меняется он только с приходом новой карты в кеш — потому отбор считается один
		// раз и держится до правки манифеста. Без этого каждый кадр стоил бы обхода всех скачанных карт с
		// новым словарём на выброс: канон клиента («Замер производительности клиента») мерит покадровый код
		// именно мусором за кадр. Снимают отбор все три точки правки набора карт: приход карты (RememberMap),
		// сброс кеша (ResetCache) и миграция схемы.
		private static int _worldMapsWorld = -1;
		private static Dictionary<int, CachedMap> _worldMaps;

		// Скачанные карты указанного мира — источник обзорной карты: показывается ровно то, что игрок уже
		// видел (кеш пополняют только загруженные карты — своя и смежные). Ключ — id карты.
		// Отдаётся ОБЩИЙ отбор, править его нельзя (оттого и тип только для чтения): нужен свой изменяемый
		// набор — снять с него копию.
		public static IReadOnlyDictionary<int, CachedMap> GetWorldMaps(int gameId, int worldId)
		{
			// Готовый отбор отдаётся БЕЗ обращения к диску: спрашивают его покадрово (радар держит по
			// нему фон каждый кадр), а EnsureLoaded на каждом вызове проверял бы наличие манифеста и
			// четырёх каталогов кеша — около десятка обращений к файловой системе и полтора десятка
			// строк-мусора за кадр. Диск нужен лишь тому, кто кеш ЧИТАЕТ либо ПИШЕТ, — сюда он попадает
			// только на пересчёте отбора, то есть при первом спросе и после смены набора карт.
			if (_worldMaps != null && _worldMapsWorld == worldId)
				return _worldMaps;

			EnsureLoaded(gameId);

			Dictionary<int, CachedMap> result = new Dictionary<int, CachedMap>();
			foreach (KeyValuePair<int, CachedMap> pair in _manifest.maps)
				if (pair.Value.world == worldId)
					result.Add(pair.Key, pair.Value);

			_worldMapsWorld = worldId;
			_worldMaps = result;

			return result;
		}

		/// <summary>
		/// Идёт ли карта в раскладку мира — мозаику, которую показывают обзорная карта и радар.
		///
		/// Карта БЕЗ места в открытом мире (интерьер, подземелье) идёт в неё лишь тогда, когда игрок
		/// стоит В НЕЙ: показать её надо — иначе, зайдя в банк, игрок увидит пустое окно, — но и только
		/// её одну. Таких карт у одного мира много (банк, кузница, тюрьма одного города), места в
		/// раскладке у них нет вовсе, а координаты записи подставлены нулями
		/// (<see cref="CachedMap.hasOpenworldPosition"/>): пусти их все — они лягут одной точкой друг на
		/// друга и на карту, которая стоит в нуле по-настоящему, а кто кого перекроет, решал бы порядок
		/// обхода словаря. Карты открытого мира правило не трогает.
		///
		/// Точка ОДНА на оба показа: разойдись их отборы — окно карты и радар показали бы разный состав
		/// мира, и расхождение это молчит. Что делать с отсеянным, каждый показ решает сам: обзорная
		/// карта не берёт запись в свою раскладку, радар гасит уже выложенную плитку.
		/// </summary>
		public static bool InWorldLayout(CachedMap map, int mapId, int currentMapId)
		{
			return map.hasOpenworldPosition || mapId == currentMapId;
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

		// Sprite тайла по его отпечатку: PNG локального кеша, разобранный по конвенции тайла (см. _sprites).
		// Битый тайл кеш снимает сам и бросает Exception с контекстом. Вызыватель оборачивает в try/catch и
		// сам решает что делать (обычно — ConnectController.Error + оставить sprite=null): клетка карты
		// останется пустой вместо мусора, а следующий sync перекачает тайл с сервера.
		public static Sprite TryGetSprite(int gameId, string sha256) => _sprites.Get(gameId, sha256);

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
