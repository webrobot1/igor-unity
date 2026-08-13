using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	// Состояние добычи КОНКРЕТНОЙ сущности-контейнера (труп существа, объект-сундук либо лавка торговца)
	// плюс её отображение на карте. Компонент висит на самой сущности: добыча — её данные, а не состояние
	// окна. Окно (LootWindowController) читает состояние отсюда.
	//
	// Состав добычи ПРИВАТЕН: сервер шлёт его адресно тому, кто стоит на клетке контейнера, — по команде
	// открытия и дальше при каждом изменении состава. Пока состав ни разу не приходил, у контейнера
	// известны только право на добычу (оно публично и приходит данными команды GROUP_LOOTFREE) и ценник
	// торговца (он тоже публичен и приходит компонентом trade).
	//
	// Сколько тело лежит на карте, решает сервер: по истечении срока он убирает сущность обычным
	// удалением. Клиент видимостью тела не управляет — есть сущность, значит рисуем.
	//
	// На карте под телом висит полоска ожидания — она показывается ТОЛЬКО тому, кому добыча пока закрыта:
	// перечёркнутый мешок отвечает «сейчас нельзя», а сама полоска тает до расширения круга допущенных.
	// Допущенному показывать нечего: полоска исчезает, а сколько тело ещё пролежит, говорит полоска срока.
	//
	// Компонента НЕТ у сущностей без компонента добычи (игроки, животные).
	public class CorpseLootMarker : MonoBehaviour
	{
		// --- Очередь на добычу ---

		/// <summary>
		/// Группа команды, ведущей ступени очереди. Она же НЕСЁТ само право: список допущенных приходит её
		/// данными, длина ступени — её длительностью, остаток — сколько до расширения круга. Допускать
		/// больше некого — сервер команду не перевешивает, право гаснет вместе с ней.
		/// </summary>
		public const string GROUP_LOOTFREE = "status/lootfree";

		// Вторая полоска под телом: первой идёт срок самого тела (DeathTimer), очередь ложится под ней —
		// над телом место занято подписью имени и полоской здоровья.
		private const int BarOrder = 71;                // поверх тела

		// Ожидание — оранжевым: тот же цвет, каким игра метит «пока нельзя».
		private static readonly Color WaitColor = new Color(1f, 0.7f, 0.3f, 1f);

		private EntityModel _model;

		// Накопленное содержимое добычи: позиция → предмет либо null (пусто). Дельта сервера ЧАСТИЧНА
		// (per-slot diff) — сливаем по ключам, не подменяем словарь целиком.
		private Dictionary<int, ItemSlotRecive> _loot;

		// Разобранное право и сырые данные команды, из которых оно разобрано. Состояние окна и полоски
		// пересчитывается каждым кадром, а разбор JSON на кадр слишком дорог — пересобираем только когда
		// сервер прислал новый пакет команды (сравнение по ссылке на её данные).
		private LootOwnerRecive _owner;
		private object _ownerOf;

		private WorldBar _bar;

		// Ценник контейнера, как его прислал сервер (компонент trade) — включая ПУСТОЙ, который приходит
		// каждому объекту: торгующий он или нет, решает TradeRecive.Trades. Храним пришедшее как есть,
		// иначе опустевший ценник не погасил бы прежнюю лавку.
		private TradeRecive _trade;

		// Счётчик пришедших изменений состояния — и состава добычи, и ценника: по обоим считаются цены на
		// ячейках, и запоздавший ценник обязан перерисовать уже показанное окно. Окно перерисовывает слоты
		// только при сдвиге счётчика: пакеты от сервера идут каждый тик, а пересоздание Item'ов на каждом
		// рвало бы перетаскивание.
		private int _version;

		/// <summary>Версия состояния: меняется при каждой пришедшей дельте добычи либо ценника.</summary>
		public int Version
		{
			get { return _version; }
		}

		/// <summary>
		/// Ценник контейнера; null — он не торгует (обычный сундук, труп, портал). Единственная точка,
		/// где пустой ценник отсеивается: отдельного признака «это торговец» сервер не шлёт, а сам
		/// компонент приходит каждому объекту — лавку выдают только заполненные множители.
		/// </summary>
		public TradeRecive Trade
		{
			get { return _trade != null && _trade.Trades ? _trade : null; }
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
			LootOwnerRecive owner = Owner();
			return owner == null || owner.CanTake(playerKey);
		}

		/// <summary>
		/// Право на добычу, как его прислал сервер данными команды <see cref="GROUP_LOOTFREE"/>;
		/// null — команды нет, добыча свободна любому.
		/// </summary>
		private LootOwnerRecive Owner()
		{
			if (_model == null) return null;

			object raw = _model.getEvent(GROUP_LOOTFREE).data;

			if (!ReferenceEquals(raw, _ownerOf))
			{
				_ownerOf = raw;
				_owner = raw != null ? _model.getEventData<LootOwnerRecive>(GROUP_LOOTFREE) : null;
			}

			return _owner;
		}

		/// <summary>
		/// Принять пришедшие компоненты добычи. Вызывает контроллер, разобравший пакет: критерий
		/// «этой сущности есть что показывать про добычу» — ответственность разбирающего, компонент
		/// лишь хранит и рисует уже принятое (тот же порядок, что у EquipableGroundMarker).
		/// Состава в пакете может не быть вовсе (он приватен): маркер заводится и под одно право на
		/// добычу либо один ценник — их видят и те, кто к контейнеру ещё не подошёл.
		/// </summary>
		public static CorpseLootMarker Apply(GameObject go, CreatureComponentsRecive components)
		{
			CorpseLootMarker marker = go.GetComponent<CorpseLootMarker>();
			if (marker == null) marker = go.AddComponent<CorpseLootMarker>();

			if (components != null && components.loot != null)
			{
				if (marker._loot == null) marker._loot = new Dictionary<int, ItemSlotRecive>();
				foreach (var kv in components.loot)
					marker._loot[kv.Key] = kv.Value;

				marker._version++;
			}

			// Ценник приходит целиком — подменяем, а не сливаем: частичной дельты у него на сервере нет.
			// Правка цены самого предмета сюда и не приходит: базовая цена живёт у предмета и приезжает
			// манифестом префабов, ценник несёт только множители торговца.
			if (components != null && components.trade != null)
			{
				marker._trade = components.trade;
				marker._version++;
			}

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

			// Гейт — сама очередь, а не состав добычи: состав приватен и приходит, лишь когда стоишь на теле,
			// а очередь публична и нужна ИЗДАЛЕКА — по ней решают, идти к телу или оно чужое.
			ShowBar();
		}

		private void OnDestroy()
		{
			if (_bar != null) Destroy(_bar.gameObject);
		}

		// --- Очередь на добычу ---

		private void ShowBar()
		{
			// Полоска отвечает на один вопрос: «когда станет можно мне». Закреплённых нет либо я уже среди
			// допущенных — вопроса нет, и полоски нет: сколько тело ещё пролежит, показывает полоска срока.
			LootOwnerRecive owner = Owner();

			if (owner == null || !owner.HasOwner || CanTake(PlayerController.Player != null ? PlayerController.Player.key : null))
			{
				HideBar();
				return;
			}

			double remain = _model.GetEventRemain(GROUP_LOOTFREE);

			// Полная длина шкалы — длительность ступени: сервер шлёт её всем видящим сущность рядом с
			// остатком. Ещё не пришла (первый кадр после закрепления) — считаем от текущего остатка,
			// полоска стартует полной.
			double? length = _model.getEvent(GROUP_LOOTFREE).timeout;
			double step = length.HasValue && length.Value > 0 ? length.Value : remain;

			if (remain <= 0 || step <= 0)
			{
				HideBar();
				return;
			}

			if (_bar == null)
				_bar = WorldBar.Create(transform, "LootBar", BarOrder);

			if (_bar == null)
				return;

			_bar.Show(
				WorldBar.PlaceUnder(_model, transform, WorldBar.StackStep),
				(float)(remain / step),
				WaitColor,
				null,
				GameIcons.LootLocked
			);
		}

		private void HideBar()
		{
			if (_bar != null) _bar.Hide();
		}
	}
}
