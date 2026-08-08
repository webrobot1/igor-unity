using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	// Состояние добычи КОНКРЕТНОЙ сущности-контейнера (труп существа либо объект-сундук) плюс её
	// отображение на карте. Компонент висит на самой сущности: добыча — её данные, а не состояние окна.
	// Окно (LootWindowController) читает состояние отсюда.
	//
	// Состав добычи ПРИВАТЕН: сервер шлёт его адресно тому, кто стоит на клетке контейнера, — по команде
	// открытия и дальше при каждом изменении состава. Пока состав ни разу не приходил, у контейнера
	// известно только право на добычу (оно публично).
	//
	// Сколько тело лежит на карте, решает сервер: по истечении срока он убирает сущность обычным
	// удалением. Клиент видимостью тела не управляет — есть сущность, значит рисуем.
	//
	// На карте над телом висит полоска очереди на добычу с именами тех, кому она уже открыта: она тает
	// до расширения круга и пропадает вместе с самой очередью, когда добыча становится общей.
	//
	// Компонента НЕТ у сущностей без компонента добычи (игроки, животные).
	public class CorpseLootMarker : MonoBehaviour
	{
		// --- Очередь на добычу ---

		/// <summary>
		/// Группа события, ведущего ступени очереди: сервер шлёт остаток секунд до расширения круга
		/// допущенных, а пройдя очередь целиком — гасит право в loot_owner.
		/// </summary>
		public const string GROUP_LOOTFREE = "status/lootfree";

		private const float BarWorldOffsetY = 1f;       // над телом: срок самого тела показан ПОД ним
		private const int BarOrder = 71;                // поверх тела

		// Моя очередь подошла — зелёным, жду — оранжевым: цвет отвечает на «мне-то уже можно?» раньше,
		// чем игрок прочтёт имена.
		private static readonly Color MineColor = new Color(0.45f, 0.85f, 0.45f, 1f);
		private static readonly Color WaitColor = new Color(1f, 0.7f, 0.3f, 1f);

		private EntityModel _model;

		// Накопленное содержимое добычи: позиция → предмет либо null (пусто). Дельта сервера ЧАСТИЧНА
		// (per-slot diff) — сливаем по ключам, не подменяем словарь целиком.
		private Dictionary<int, ItemSlotRecive> _loot;
		private LootOwnerRecive _owner;

		private WorldBar _bar;

		// Готовая подпись и состав, из которого она собрана: имена ищутся по сущностям на сцене, а зовут
		// нас каждый кадр — пересобираем только со сменой круга допущенных.
		private string _names;
		private string _namesOf;

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
			// Очередь висит прямо на теле, чтобы ожидание было видно без окна. Закреплённых нет — добыча
			// общая, показывать нечего: тело само и есть признак.
			if (_owner == null || !_owner.HasOwner)
			{
				HideBar();
				return;
			}

			double remain = _model.GetEventRemain(GROUP_LOOTFREE);
			double step = _owner.step > 0 ? _owner.step : remain;

			if (remain <= 0 || step <= 0)
			{
				HideBar();
				return;
			}

			if (_bar == null)
				_bar = WorldBar.Create(transform, "LootBar", BarOrder);

			if (_bar == null)
				return;

			bool mine = CanTake(PlayerController.Player != null ? PlayerController.Player.key : null);

			_bar.Show(
				transform.position + new Vector3(0f, BarWorldOffsetY, 0f),
				(float)(remain / step),
				mine ? MineColor : WaitColor,
				AllowedNames(),
				GameIcons.Loot
			);
		}

		private void HideBar()
		{
			if (_bar != null) _bar.Hide();
		}

		/// <summary>Имена допущенных к добыче через запятую — подпись под полоской очереди.</summary>
		private string AllowedNames()
		{
			string[] allowed = _owner.Allowed;
			string of = string.Join("", allowed);

			if (of == _namesOf)
				return _names;

			_namesOf = of;

			string[] names = new string[allowed.Length];
			for (int i = 0; i < allowed.Length; i++)
				names[i] = NameOf(allowed[i]);

			_names = string.Join(", ", names);
			return _names;
		}

		/// <summary>
		/// Отображаемое имя по ключу сущности: у стоящего рядом игрока берём его собственное, ушедшего с
		/// глаз — разбираем ключ (он собран как вид_логин; логин и есть отображаемое имя).
		/// </summary>
		private static string NameOf(string key)
		{
			GameObject go = GameObject.Find(key);
			EntityModel model = go != null ? go.GetComponent<EntityModel>() : null;

			if (model != null && !string.IsNullOrEmpty(model.slug))
				return model.slug;

			int cut = key.IndexOf('_');
			return cut >= 0 && cut + 1 < key.Length ? key.Substring(cut + 1) : key;
		}
	}
}
