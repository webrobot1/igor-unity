using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Имя сущности всплывает над ней, пока она под курсором: у предмета — что за вещь лежит, у существа —
	/// кто это (у игрока его логин, у моба название из library). Показ по наведению, а не постоянный:
	/// плашка шире клетки, и у стоящих рядом сущностей постоянные имена налезали бы друг на друга.
	/// Наведение выбрано и потому, что клик занят игровыми действиями — выбором цели, добычей, подходом
	/// к предмету; вопрос «кто это» отвечается тем же жестом у любой сущности.
	///
	/// КОГО подписывать и КАКИМ префабом решает ВЫЗЫВАЮЩИЙ (MainController.UpdateObject —
	/// EquipableGroundMarker у вещей, существа напрямую), сюда сущность приходит уже отобранной.
	///
	/// Текст берётся из EntityModel.DisplayName при каждом показе: имя меняется вместе с prefab'ом
	/// сущности (смена визуала на лету), а храня копию, надпись бы врала.
	/// </summary>
	public class HoverLabel : MonoBehaviour
	{
		/// <summary>Префаб вида подписи для существ. У вещей на земле свой — см. EquipableGroundMarker.</summary>
		public const string PrefabCreature = "Prefabs/UI/WorldLabelCreature";

		private const float GapY = 0.06f;      // зазор между верхним краем тела и низом плашки (в клетках)
		private const int Order = 61;          // поверх тела и обводки
		private const float FadeSpeed = 10f;   // скорость появления/угасания плашки под курсором

		private static Camera _cam;

		private string _prefabPath;
		private EntityModel _model;
		private WorldLabel _label;
		private Vector3 _labelScale;    // масштаб надписи, как он задан в её префабе
		private Collider2D _ownerCollider;
		private Graphic[] _markers;     // мировая разметка на сущности (полоска жизней и прочее)
		private readonly Vector3[] _corners = new Vector3[4];
		private float _hover;           // 0..1, сглаженная видимость плашки (0 — скрыта)

		/// <summary>
		/// Навесить/обновить подпись сущности. prefabPath — префаб вида: им и различаются подписи
		/// вещи, моба и игрока, кода на каждый вид не заводится.
		/// </summary>
		public static HoverLabel Apply(GameObject go, string prefabPath)
		{
			HoverLabel hover = go.GetComponent<HoverLabel>();
			if (hover == null)
			{
				hover = go.AddComponent<HoverLabel>();
				hover._prefabPath = prefabPath;
			}

			return hover;
		}

		private void Awake()
		{
			_model = GetComponent<EntityModel>();
		}

		private void OnDestroy()
		{
			if (_label != null) Destroy(_label.gameObject);
		}

		private void LateUpdate()
		{
			if (_model == null) return;

			bool hovered = Hovered();
			_hover = Mathf.MoveTowards(_hover, hovered ? 1f : 0f, Time.deltaTime * FadeSpeed);

			// Пока сущности не касались курсором, надписи у неё нет вовсе: сущностей на карте десятки,
			// а спрошены за сессию будут единицы.
			if (_hover <= 0.001f)
			{
				if (_label != null) _label.SetAlpha(0f);
				return;
			}

			if (_label == null)
			{
				_label = WorldLabel.Create(transform, _prefabPath, "HoverLabel", Order);
				if (_label == null)
				{
					enabled = false;   // префаб не найден — жалоба уже выдана, попыток не повторяем
					return;
				}

				_labelScale = _label.transform.localScale;
			}

			_label.SetText(_model.DisplayName);
			Place();
			_label.SetAlpha(_hover);
		}

		/// <summary>
		/// Сущность под курсором. Наведение держится и когда курсор ушёл с тела на саму надпись: она
		/// кликабельна, и исчезновение из-под курсора отняло бы клик по ней. Скрытая надпись наведения
		/// не держит — иначе над сущностью осталась бы невидимая зона, ловящая курсор.
		/// </summary>
		private bool Hovered()
		{
			if (_cam == null) _cam = Camera.main;
			if (_cam == null) return false;

			Vector3 world = _cam.ScreenToWorldPoint(Input.mousePosition);
			Vector2 point = new Vector2(world.x, world.y);

			if (_ownerCollider == null) _ownerCollider = GetComponent<Collider2D>();
			if (_ownerCollider != null && _ownerCollider.OverlapPoint(point)) return true;

			return _hover > 0.001f && _label != null && _label.ClickArea.OverlapPoint(point);
		}

		/// <summary>
		/// Поднять надпись над сущностью и вернуть ей собственный масштаб. Подъём отсчитывается от
		/// ВЕРХНЕГО края занятого места, а не от центра: высота сущностей разная (мышь, дерево, игрок),
		/// и единый подъём от центра одним оставлял бы пустоту, на других ложился бы.
		///
		/// Масштаб корня сущности задан нормализацией её размера под серверный size, и надпись, будучи
		/// ребёнком, унаследовала бы его — имя моба выходило бы крупнее имени мыши. Компенсируем корневой
		/// масштаб, оставляя надписи её префабный: у всех сущностей подпись одного кегля. Знак X делим со
		/// знаком, чтобы у отзеркаленной влево сущности текст не читался задом наперёд.
		/// </summary>
		private void Place()
		{
			Vector3 root = transform.localScale;
			float sx = Mathf.Abs(root.x) > 1e-4f ? root.x : 1f;
			float sy = Mathf.Abs(root.y) > 1e-4f ? Mathf.Abs(root.y) : 1f;
			_label.transform.localScale = new Vector3(_labelScale.x / sx, _labelScale.y / sy, _labelScale.z);

			float half = _label.WorldHeight * 0.5f;

			// Границы видимого тела: у сущности со скелетной анимацией корневой спрайт выключен, а тело
			// собрано из детей — мерить надо их. Тела нет вовсе (визуал ещё грузится) — держим надпись
			// над точкой сущности, пока не появится.
			bool hasBody = _model.TryGetVisualBounds(out Bounds bounds);
			float x = hasBody ? bounds.center.x : transform.position.x;
			float top = Mathf.Max(hasBody ? bounds.max.y : transform.position.y, MarkersTop());

			_label.transform.position = new Vector3(x, top + GapY + half, transform.position.z);
		}

		/// <summary>
		/// Верх того, что уже нарисовано над сущностью помимо тела: полоска жизней у выделенного существа,
		/// любая другая мировая разметка на ней. Тело меряется спрайтами, а эти элементы рисуются интерфейсом
		/// и в границы тела не попадают — без их учёта надпись ложится прямо на полоску.
		/// Собственная надпись из счёта исключена: иначе она отталкивала бы сама себя всё выше с каждым кадром.
		/// </summary>
		private float MarkersTop()
		{
			// Состав детей сущности постоянен, меняется только их включённость — ищем однажды.
			if (_markers == null) _markers = GetComponentsInChildren<Graphic>(true);

			float top = float.NegativeInfinity;
			foreach (var marker in _markers)
			{
				if (marker == null || !marker.isActiveAndEnabled) continue;
				if (_label != null && marker.transform.IsChildOf(_label.transform)) continue;

				var rect = marker.rectTransform;
				rect.GetWorldCorners(_corners);
				for (int i = 0; i < 4; i++)
					if (_corners[i].y > top) top = _corners[i].y;
			}

			return top;
		}
	}
}
