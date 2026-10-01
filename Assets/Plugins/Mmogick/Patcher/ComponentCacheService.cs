using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	// Справочник компонентов игры — умолчание значения, состав видов, иконка и описание каждого компонента.
	// Endpoint (свой канал данных игры, не анимационный: компонент — элемент игры, к скелетам и картинкам
	// отношения не имеющий):
	//   GET /game/patch/{gameId}/{token}/component  — справочник целиком: {items: slug→запись}
	//
	// Справочник приходит целиком на каждый вход в игру и живёт только в памяти: до входа его не читает
	// никто, а на входе он приходит заново — копия на диске не пригодилась бы ни разу.
	//
	// Кто чем пользуется: умолчание — последнее звено цепочки разрешения значения у префаба
	// (AnimationCacheService.GetComponentValue), состав видов — источник компонентов, префабу не заданных,
	// имя файла иконки адресует картинку в кеше графики (AnimationCacheService.GetComponentSprite), адрес
	// существа анимации — скелет значка в кеше скелетов (SpineCacheService.GetCached); пакет этой анимации
	// кладёт та же предзагрузка, что и скелеты тел (AnimationCacheService).
	public static class ComponentCacheService
	{
		private static Dictionary<string, ComponentEntry> _components;

		// Конверт ответа (см. серверный Game\Controller\Api\PatchController::component).
		[Serializable]
		private class DirectoryResponse
		{
			public Dictionary<string, ComponentEntry> items;  // slug → запись каждого живого компонента игры
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

		// Справочник перед входом в игру: ответ заменяет прежний целиком. Вызывать ДО AnimationCacheService.SyncAll:
		// его предзагрузка скелетов берёт отсюда значки компонентов, заданные анимацией.
		public static IEnumerator Sync(string host, int gameId, string token, Action<string> onError = null)
		{
			// Прежний справочник снимаем до запроса: сорвись запрос — в памяти остался бы справочник прошлого
			// входа, возможно другой игры, и отвечал бы как текущий.
			_components = null;

			string url = "http://" + host + "/game/patch/" + gameId + "/" + token + "/component";
			Debug.Log("Запрашиваю справочник компонентов " + url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("ComponentCache: " + GameCache.ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			DirectoryResponse parsed;
			try { parsed = JsonConvert.DeserializeObject<DirectoryResponse>(text); }
			catch (Exception ex) { onError?.Invoke("ComponentCache parse: " + ex.Message); yield break; }

			// Ответ не конверт либо конверт без справочника — контракт нарушен. Сигналим, вызывающий уводит
			// на экран входа.
			if (parsed == null || parsed.items == null)
			{
				onError?.Invoke("ComponentCache: ответ /component без справочника (items)");
				yield break;
			}

			_components = parsed.items;
			Debug.Log("ComponentCache: справочник получен, компонентов " + _components.Count);
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
