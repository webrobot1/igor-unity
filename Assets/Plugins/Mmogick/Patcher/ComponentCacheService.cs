using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	// Справочник компонентов игры — умолчание значения, состав видов, иконка и описание каждого компонента.
	// Endpoint (свой канал данных игры, не анимационный: компонент — элемент игры, к скелетам и картинкам
	// отношения не имеющий):
	//   GET /game/patch/{gameId}/{token}/component?since=  — дельта справочника:
	//       {items: slug→запись (изменившиеся), all: [все slug], version}
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/component/
	//   component.json — slug → ComponentEntry (дельта-мёрж по ?since, removal по списку all)
	//   sync.json      — отметка синхронизации (component_version, cache_schema_version)
	//
	// Кто чем пользуется: умолчание — последнее звено цепочки разрешения значения у префаба
	// (AnimationCacheService.GetComponentValue), состав видов — источник компонентов, префабу не заданных,
	// имя файла иконки адресует картинку в кеше графики (AnimationCacheService.GetComponentSprite), адрес
	// существа анимации — скелет значка в кеше скелетов (SpineCacheService.GetCached); пакет этой анимации
	// кладёт та же предзагрузка, что и скелеты тел (AnimationCacheService).
	public static class ComponentCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE  = "sync.json";
		private const string DIRECTORY_FILE = "component.json";

		// Версия формата локального кеша (ComponentEntry/component.json). Бамп при смене состава записи либо
		// ФОРМЫ значения внутри неё: дельта везёт только изменившееся, у давно не правленного компонента дата
		// прежняя, и его лежалая запись осталась бы в старой форме — разбор упал бы на ней.
		private const int CACHE_SCHEMA_VERSION = 3;

		private static SyncManifest _manifest;
		private static Dictionary<string, ComponentEntry> _components;

		[Serializable]
		public class SyncManifest
		{
			// Версия последней дельта-синхронизации справочника (unix-сек, max updated отданных записей).
			// Шлётся как ?since в следующий заход. 0 (дефолт) → холодный старт: весь справочник.
			public long component_version;

			// Версия формата локального кеша на диске. При несовпадении с CACHE_SCHEMA_VERSION EnsureLoaded
			// сбрасывает component_version→0 — разовый полный refetch уже в новом формате.
			public int cache_schema_version;
		}

		// Конверт дельта-ответа (см. серверный Game\Controller\Api\PatchController::component).
		[Serializable]
		private class DirectoryResponse
		{
			public Dictionary<string, ComponentEntry> items;  // только изменившиеся с since (slug → запись)
			public List<string> all;                          // все живые slug игры (для детекции удалений)
			public long version;                              // max updated отданных items — клиент шлёт как since далее
		}

		/// <summary>
		/// Компонент игры в справочнике: умолчание значения и виды, которым компонент положен. Умолчание —
		/// последнее звено цепочки «своё у экземпляра → заданное префабу → умолчание компонента»; в записях
		/// префабов его нет, иначе одно значение размножилось бы по всем записям вида.
		/// </summary>
		[Serializable]
		public class ComponentEntry
		{
			public JToken @default;
			public List<string> kind;

			/// <summary>
			/// Иконка компонента: имя файла в общем архиве картинок игры (том же, откуда берутся спрайты
			/// предметов) — готовым, склеивать из частей тут нечего. null — иконки у компонента нет.
			/// </summary>
			public string image;

			/// <summary>
			/// Значок компонента, заданный АНИМАЦИЕЙ: адрес существа внутри записи анимации. Сама анимация
			/// едет своим каналом (кеш анимаций), тут только адрес. null — значок задан картинкой либо его
			/// нет вовсе: формы взаимоисключающие, сервер шлёт ровно одну.
			/// </summary>
			public IconAnimation animation;

			/// <summary>
			/// Описание компонента, заданное у элемента игры. null — описание не задано.
			/// </summary>
			public string description;
		}

		/// <summary>
		/// Адрес существа анимации: чем и адресуется скелет значка. Имена полей — как приходят с сервера;
		/// разбор в редакторе строгий, и лишнее поле уронило бы весь справочник.
		/// </summary>
		[Serializable]
		public class IconAnimation
		{
			/// <summary>Идентификатор записи анимации — им же адресован пакет скелета в кеше анимаций.</summary>
			public int animation;

			/// <summary>Имя существа внутри записи: вариантов скелета в одной записи бывает несколько.</summary>
			public string entity;

			/// <summary>
			/// Клип, которым значку и быть: движение существа, отобранное игрой. null — отобрать было не из
			/// чего, и клип выбирает сам сборщик скелета.
			/// </summary>
			public string clip;
		}

		// Корень кеша справочника для игры
		private static string DirectoryPath(int gameId)
		{
			string folder;
			#if UNITY_WEBGL && !UNITY_EDITOR
				folder = "idbfs";
			#else
				folder = Application.persistentDataPath;
			#endif
			string path = Path.Combine(folder, "games", gameId.ToString(), "component");
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			return path;
		}

		private static string ManifestPath(int gameId)  => Path.Combine(DirectoryPath(gameId), MANIFEST_FILE);
		private static string DirectoryFile(int gameId) => Path.Combine(DirectoryPath(gameId), DIRECTORY_FILE);

		// Загружает отметку синхронизации и сам справочник с диска. Идемпотентно.
		private static void EnsureLoaded(int gameId)
		{
			string mp = ManifestPath(gameId);
			// Рассинхрон disk↔RAM (sync.json удалён внешним кодом, а отметка держится в памяти) — нарушение
			// контракта: этими файлами владеет только ComponentCacheService. Падаем громко, чтобы виновный
			// код чинили у источника, а не маскировали тихим сбросом.
			if (_manifest != null && !File.Exists(mp))
				throw new InvalidOperationException("ComponentCache: sync.json отсутствует на диске, но отметка загружена в память. Кто-то очистил кэш мимо ResetCache() — почините источник.");

			if (_manifest == null)
			{
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();

				if (_manifest.cache_schema_version != CACHE_SCHEMA_VERSION)
				{
					_manifest.cache_schema_version = CACHE_SCHEMA_VERSION;
					_manifest.component_version = 0;
					SaveManifest(gameId);
				}
			}

			if (_components == null)
			{
				string dp = DirectoryFile(gameId);
				_components = new Dictionary<string, ComponentEntry>();
				if (File.Exists(dp))
				{
					// Битый либо недописанный файл не разбирается — начнём с пустого, Sync перезальёт целиком
					// (since=0 при пустом справочнике, см. ниже).
					try { _components = JsonConvert.DeserializeObject<Dictionary<string, ComponentEntry>>(File.ReadAllText(dp)) ?? new Dictionary<string, ComponentEntry>(); }
					catch { _components = new Dictionary<string, ComponentEntry>(); }
				}
			}
		}

		private static void SaveManifest(int gameId)
		{
			File.WriteAllText(ManifestPath(gameId), JsonConvert.SerializeObject(_manifest));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		private static void SaveDirectory(int gameId)
		{
			File.WriteAllText(DirectoryFile(gameId), JsonConvert.SerializeObject(_components));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полный сброс кеша справочника: отметка и сам справочник. Следующий Sync соберёт его с нуля.
		public static void ResetCache(int gameId)
		{
			Debug.LogWarning("ComponentCache: сброс кеша справочника игры " + gameId);
			// null, а не пустые объекты: EnsureLoaded бросает на «отметка в памяти есть, файла нет», и
			// повторный вход в той же сессии упал бы на этом guard'е.
			_manifest = null;
			_components = null;

			try
			{
				if (File.Exists(ManifestPath(gameId)))  File.Delete(ManifestPath(gameId));
				if (File.Exists(DirectoryFile(gameId))) File.Delete(DirectoryFile(gameId));
			}
			catch (Exception ex) { Debug.LogWarning("ComponentCache: ошибка при сбросе кеша: " + ex.Message); }

			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Дельта-синхронизация справочника перед входом в игру. Мёржит изменившиеся записи и удаляет slug'и,
		// которых больше нет в all. since = отметка прошлой синхронизации, НО только если справочник не пуст:
		// при потере файла (удалён мимо ResetCache, отметка уцелела) since=0 форсит полный ресинк — иначе
		// дельта прислала бы лишь изменившееся, а неизменные компоненты остались бы потеряны.
		// Вызывать ДО AnimationCacheService.SyncAll: цепочка разрешения значений префаба опирается на умолчания
		// отсюда.
		public static IEnumerator Sync(string host, int gameId, string token, Action<string> onError = null)
		{
			EnsureLoaded(gameId);

			long since = _components.Count > 0 ? _manifest.component_version : 0;
			string url = "http://" + host + "/game/patch/" + gameId + "/" + token + "/component?since=" + since;
			Debug.Log("Запрашиваю справочник компонентов " + url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("ComponentCache: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			DirectoryResponse parsed;
			try { parsed = JsonConvert.DeserializeObject<DirectoryResponse>(text); }
			catch (Exception ex) { onError?.Invoke("ComponentCache parse: " + ex.Message); yield break; }

			// null — сервер вернул не конверт: контракт нарушен, дальше цепочка разрешения значений молча
			// отдавала бы «умолчания нет» на каждом компоненте. Сигналим, вызывающий уводит на экран входа.
			if (parsed == null) { onError?.Invoke("ComponentCache: пустой ответ /component"); yield break; }

			int changed = 0;
			if (parsed.items != null)
				foreach (var kv in parsed.items) { _components[kv.Key] = kv.Value; changed++; }

			// Удаление: всё, чего нет в all (снятый компонент исчезает у клиента независимо от version).
			if (parsed.all != null)
			{
				var keep = new HashSet<string>(parsed.all);
				var drop = new List<string>();
				foreach (var slug in _components.Keys)
					if (!keep.Contains(slug)) drop.Add(slug);
				foreach (var slug in drop) _components.Remove(slug);
			}

			_manifest.component_version = parsed.version;
			Debug.Log("ComponentCache: справочник синхронизирован (since=" + since + "), изменено " + changed + ", всего " + _components.Count);
			SaveDirectory(gameId);
			SaveManifest(gameId);
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

		// Умолчание компонента — значение, одинаковое для всех, кому компонент положен. Точечный
		// AnimationCacheService.GetComponentValue сюда не годится: он резолвит значение ДЛЯ префаба и умолчание
		// отдаёт лишь тому, чьему виду компонент положен, — а справочное значение бывает нужно из компонента,
		// которого у самого префаба нет вовсе (книга заклинаний, схема настроек).
		// Контракт: вызывать только после Sync, иначе exception — справочник грузится до входа в мир
		// (SigninController.LoadMain), и тихий null от «не загружен» неотличим от «умолчания нет».
		// null — компонента нет в справочнике либо умолчания у него нет: легитимное отсутствие, вызывающий
		// решает сам.
		public static JToken GetDefault(string component)
		{
			EnsureSynced(component);
			if (string.IsNullOrEmpty(component))
				return null;

			return _components.TryGetValue(component, out ComponentEntry entry) && entry != null ? entry.@default : null;
		}

		// Имя файла иконки компонента в общем архиве картинок игры — том же, откуда берутся спрайты предметов.
		// Справочник отдаёт его готовым (у entry префаба клиент склеивает имя сам из sha256+extension; тут
		// склеивать нечего). Готовый спрайт по этому имени даёт AnimationCacheService.GetComponentSprite.
		// Контракт по справочнику тот же, что у GetDefault. null — иконки у компонента нет либо самого
		// компонента в справочнике нет: показ рисует компонент как рисовал.
		public static string GetImage(string component)
		{
			EnsureSynced(component);
			if (string.IsNullOrEmpty(component))
				return null;

			return _components.TryGetValue(component, out ComponentEntry entry) && entry != null && !string.IsNullOrEmpty(entry.image)
				? entry.image : null;
		}

		// Значок компонента, заданный анимацией: адрес существа, чей скелет и рисуется вместо картинки.
		// Формы значка взаимоисключающи — сервер шлёт либо имя файла картинки (GetImage), либо этот адрес.
		// Сам скелет собирает SpineCacheService по тому же адресу, пакет анимации кладёт предзагрузка перед
		// входом в игру (AnimationCacheService).
		// Контракт по справочнику тот же, что у GetDefault. null — значок компонента задан картинкой либо
		// его нет вовсе либо самого компонента в справочнике нет: показ рисует компонент как рисовал.
		public static IconAnimation GetAnimation(string component)
		{
			EnsureSynced(component);
			if (string.IsNullOrEmpty(component))
				return null;

			return _components.TryGetValue(component, out ComponentEntry entry) && entry != null
				&& entry.animation != null && entry.animation.animation != 0
				&& !string.IsNullOrEmpty(entry.animation.entity)
				? entry.animation : null;
		}

		// Описание компонента, заданное у элемента игры. Им подсказка объясняет игроку, что значит
		// характеристика: подпись строки зашита в клиент и говорит лишь имя, а смысл живёт у самого
		// элемента и правится в админке.
		// Контракт по справочнику тот же, что у GetDefault. null — описание не задано либо компонента
		// нет в справочнике: показ остаётся на одной подписи.
		public static string GetDescription(string component)
		{
			EnsureSynced(component);
			if (string.IsNullOrEmpty(component))
				return null;

			return _components.TryGetValue(component, out ComponentEntry entry) && entry != null && !string.IsNullOrEmpty(entry.description)
				? entry.description : null;
		}

		// Виды, которым компонент положен (ComponentEntry.kind). По нему умолчание отдаётся только своему виду:
		// у чужого значения быть не должно, иначе предмет получил бы свойство существа.
		// Контракт по справочнику тот же, что у GetDefault. null — компонента нет в справочнике либо видов
		// у него нет.
		public static List<string> GetKinds(string component)
		{
			EnsureSynced(component);
			if (string.IsNullOrEmpty(component))
				return null;

			return _components.TryGetValue(component, out ComponentEntry entry) && entry != null ? entry.kind : null;
		}

		// Все компоненты справочника. Нужны сборке состава префаба: компонент, префабу не заданный, но
		// положенный его виду, действует умолчанием — и в составе он есть.
		// Контракт по справочнику тот же, что у GetDefault.
		public static IEnumerable<string> GetSlugs()
		{
			EnsureSynced(null);
			return _components.Keys;
		}

		// Справочник грузится до входа в мир, поэтому его отсутствие — не «пусто», а вызов до загрузки:
		// баг тайминга у вызывающего. Падаем громко — тихий дефолт увёл бы в «умолчания нет» на каждом
		// компоненте, и характеристики существ молча разъехались бы со значениями сервера.
		private static void EnsureSynced(string component)
		{
			if (_components == null)
				throw new InvalidOperationException("ComponentCacheService вызван до Sync (справочник компонентов не загружен)"
					+ (string.IsNullOrEmpty(component) ? "" : ", component=" + component)
					+ ". Вызывайте только после завершения SigninController.LoadMain.");
		}
	}
}
