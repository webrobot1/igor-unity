using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	// Состояние добычи КОНКРЕТНОЙ сущности плюс её отображение на карте. Компонент висит на самой
	// сущности: добыча — её данные (публичные компоненты loot / loot_owner приходят world-дельтой
	// этой сущности), а не состояние окна. Окно (LootWindowController) читает состояние отсюда и
	// открывается локально, без запроса на сервер.
	//
	// Отдельного значка «тут есть добыча» нет и быть не должно: видимое тело САМО означает, что взять
	// есть что — труп с пустой добычей не рисуется вовсе (см. ниже). На карте показывается только
	// отсчёт оставшихся секунд, пока добыча закреплена за другим игроком; срок вышел — отсчёт
	// пропадает, брать может любой.
	//
	// Труп с ПУСТОЙ добычей не рисуется вовсе (обыскан либо ничего не выпало): тело гасится
	// forceRenderingOff, кликабельные коллайдеры выключаются — пустое тело не мешает целиться и не
	// притворяется контейнером. Гасим именно рендер, а не GameObject: выключенный объект перестал бы
	// находиться GameObject.Find, и следующая дельта создала бы сущность-дубль. Живая сущность и
	// воскресший моб восстанавливаются автоматически.
	//
	// Компонента НЕТ у сущностей без компонента добычи (игроки, животные) — их тела рисуются как раньше.
	public class CorpseLootMarker : MonoBehaviour
	{
		// --- Отсчёт чужого эксклюзива ---
		private const float TimerWorldOffsetY = 0.75f;  // подъём отсчёта над центром тела (в клетках)
		private const int TimerOrder = 71;              // поверх тела
		private const int TimerFontSize = 64;
		private const float TimerCharSize = 0.045f;
		private static readonly Color TimerColor = new Color(1f, 0.85f, 0.5f, 1f);

		// Пересканирование рендереров скрытого тела: визуал Spriter собирается асинхронно, часть
		// SpriteRenderer'ов появляется уже после того, как тело решено не рисовать.
		private const float HideRescanSec = 0.5f;

		private static Font _font;

		private EntityModel _model;

		// Накопленное содержимое добычи: позиция → предмет либо null (пусто). Дельта сервера ЧАСТИЧНА
		// (per-slot diff) — сливаем по ключам, не подменяем словарь целиком.
		private Dictionary<int, ItemSlotRecive> _loot;
		private LootOwnerRecive _owner;

		private Transform _timerRoot;
		private TextMesh _timer;
		private int _timerShown = -1;    // последняя показанная секунда отсчёта

		private bool _hidden;            // тело сейчас погашено
		private float _hideRescanAt;

		// Счётчик пришедших изменений добычи. Окно перерисовывает слоты только при его сдвиге: пакеты
		// от сервера идут каждый тик, а пересоздание Item'ов на каждом рвало бы перетаскивание.
		private int _version;

		/// <summary>Версия содержимого: меняется при каждой пришедшей дельте добычи.</summary>
		public int Version
		{
			get { return _version; }
		}

		/// <summary>Содержимое добычи (позиция → предмет либо null). null — компонент ни разу не приходил.</summary>
		public Dictionary<int, ItemSlotRecive> Loot
		{
			get { return _loot; }
		}

		/// <summary>В добыче есть хотя бы один предмет.</summary>
		public bool HasLoot
		{
			get
			{
				if (_loot == null) return false;
				foreach (var kv in _loot)
					if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.prefab))
						return true;
				return false;
			}
		}

		/// <summary>Позиции с предметами (1-based) — для «забрать всё».</summary>
		public List<int> OccupiedSlots()
		{
			List<int> list = new List<int>();
			if (_loot == null) return list;
			foreach (var kv in _loot)
				if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.prefab))
					list.Add(kv.Key);
			list.Sort();
			return list;
		}

		/// <summary>Разрешено ли игроку брать отсюда: владельца нет, владелец он, либо срок эксклюзива вышел.</summary>
		public bool CanTake(string playerKey)
		{
			return _owner == null || _owner.CanTake(playerKey);
		}

		/// <summary>
		/// Принять пришедшие компоненты добычи. Вызывает контроллер, разобравший пакет: критерий
		/// «этой сущности есть что показывать про добычу» — ответственность разбирающего, компонент
		/// лишь хранит и рисует уже принятое (тот же порядок, что у EquipableGroundMarker).
		/// </summary>
		public static CorpseLootMarker Apply(GameObject go, EnemyComponentsRecive components)
		{
			CorpseLootMarker marker = go.GetComponent<CorpseLootMarker>();
			if (marker == null) marker = go.AddComponent<CorpseLootMarker>();

			if (components.loot != null)
			{
				if (marker._loot == null) marker._loot = new Dictionary<int, ItemSlotRecive>();
				foreach (var kv in components.loot)
					marker._loot[kv.Key] = kv.Value;

				marker._version++;
			}

			if (components.loot_owner != null)
				marker._owner = components.loot_owner;

			return marker;
		}

		/// <summary>Маркер живой сущности по её ключу; null — сущности нет на сцене либо у неё нет добычи.</summary>
		public static CorpseLootMarker Find(string key)
		{
			if (string.IsNullOrEmpty(key)) return null;
			GameObject go = GameObject.Find(key);
			return go != null ? go.GetComponent<CorpseLootMarker>() : null;
		}

		private void Awake()
		{
			_model = GetComponent<EntityModel>();
		}

		private void LateUpdate()
		{
			if (_model == null) return;

			bool dead = _model.action == "dead";

			// Живая (или уже удаляемая) сущность про добычу ничего не показывает: у моба она гаснет
			// воскрешением, а до смерти пуста по определению.
			if (!dead)
			{
				ShowBody();
				HideTimer();
				return;
			}

			if (!HasLoot)
			{
				// Пустая добыча = труп обыскан либо не дал ничего: тела нет до воскрешения.
				HideBody();
				HideTimer();
				return;
			}

			ShowBody();
			ShowTimer();
		}

		private void OnDestroy()
		{
			if (_timerRoot != null) Destroy(_timerRoot.gameObject);
		}

		// --- Отсчёт чужого эксклюзива ---

		private void ShowTimer()
		{
			// Отсчёт до конца чужого эксклюзива — прямо на трупе, чтобы ожидание было видно без окна.
			// Своя (или уже освободившаяся) добыча ничего не показывает: тело само и есть признак.
			bool mine = CanTake(PlayerController.Player != null ? PlayerController.Player.key : null);
			int remain = (_owner != null && !mine) ? Mathf.CeilToInt((float)_owner.Remain) : 0;

			if (remain <= 0)
			{
				HideTimer();
				return;
			}

			EnsureTimer();
			if (_timerRoot == null) return;

			if (!_timerRoot.gameObject.activeSelf)
				_timerRoot.gameObject.SetActive(true);

			_timerRoot.position = transform.position + new Vector3(0f, TimerWorldOffsetY, 0f);

			if (remain != _timerShown)
			{
				_timerShown = remain;
				_timer.text = remain.ToString();
			}
		}

		private void HideTimer()
		{
			if (_timerRoot != null && _timerRoot.gameObject.activeSelf)
				_timerRoot.gameObject.SetActive(false);
		}

		private void EnsureTimer()
		{
			if (_timerRoot != null) return;

			var rootSr = GetComponent<SpriteRenderer>();

			// Отсчёт — СОСЕД тела, а не его ребёнок: EntityModel.TryGetVisualBounds считает границы
			// сущности по её дочерним SpriteRenderer'ам, и висящий над телом текст раздул бы и
			// кольцо-подсветку трупа, и его кликабельный коллайдер. Позицию сводим с телом покадрово
			// (труп не двигается — это дёшево).
			var timerGo = new GameObject("LootTimer");
			timerGo.transform.SetParent(transform.parent, false);
			timerGo.layer = gameObject.layer;
			_timerRoot = timerGo.transform;

			// SortingGroup обязателен: MeshRenderer от TextMesh в 2D-конвейере сортировки сам не участвует
			// и уходит под тайлы карты — сортируется именно группа, а не рендерер текста.
			var group = timerGo.AddComponent<UnityEngine.Rendering.SortingGroup>();
			group.sortingOrder = TimerOrder;
			if (rootSr != null) group.sortingLayerID = rootSr.sortingLayerID;

			_timer = timerGo.AddComponent<TextMesh>();
			_timer.font = GetFont();
			_timer.characterSize = TimerCharSize;
			_timer.fontSize = TimerFontSize;
			_timer.anchor = TextAnchor.MiddleCenter;
			_timer.alignment = TextAlignment.Center;
			_timer.color = TimerColor;

			var timerRenderer = timerGo.GetComponent<MeshRenderer>();
			timerRenderer.sharedMaterial = GetFont().material;
			timerRenderer.sortingOrder = TimerOrder;
			if (rootSr != null) timerRenderer.sortingLayerID = rootSr.sortingLayerID;
		}

		// --- Тело трупа ---

		private void HideBody()
		{
			if (_hidden && Time.time < _hideRescanAt) return;

			_hidden = true;
			_hideRescanAt = Time.time + HideRescanSec;

			// forceRenderingOff, а не enabled: флагом enabled управляет анимационный слой (Spriter и
			// Universal-оверлей гасят/зажигают рендереры сами) — перехват дрался бы с ним.
			// Отсчёт сюда не попадает: он сосед тела, а не его ребёнок (см. EnsureTimer).
			foreach (var r in GetComponentsInChildren<Renderer>(true))
				r.forceRenderingOff = true;

			foreach (var c in GetComponentsInChildren<Collider2D>(true))
				c.enabled = false;
		}

		private void ShowBody()
		{
			if (!_hidden) return;
			_hidden = false;

			foreach (var r in GetComponentsInChildren<Renderer>(true))
				r.forceRenderingOff = false;

			foreach (var c in GetComponentsInChildren<Collider2D>(true))
				c.enabled = true;
		}

		private static Font GetFont()
		{
			if (_font == null)
				_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			return _font;
		}
	}
}
