using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Полоска у сущности: доля оставшегося срока, иконка смысла и подпись под ней. Заменяет собой
	/// числовые отсчёты — доля видна с одного взгляда, а число заставляет читать и сравнивать.
	/// Куда её ставить, решает владелец — готовая точка под телом у <see cref="PlaceUnder"/>.
	///
	/// Полоска лежит в СВОЁМ контейнере сцены — ни ребёнком существа, ни в зоне его карты:
	///  - у существа корень переворачивается по X на смене ракурса (EntityModel.SetData) и меняет масштаб
	///    при подгонке тела под серверный размер (UpdateController), а ребёнок наследует и то и другое:
	///    подпись со значком встали бы зеркально и поплыли бы в размере. Детям существа (LifeBar) этот
	///    масштаб приходится гасить обратной величиной — полоске такой привязки не нужно;
	///  - в зоне карты живут ТОЛЬКО существа: обработка мирового пакета разбирает детей зоны как существа,
	///    и посторонний объект там ломает её.
	/// От контейнера полоска не берёт ничего: мировую точку ей каждый кадр задаёт владелец
	/// (<see cref="Show"/>), слой сортировки она ставит себе сама, а уничтожает её владелец вместе со
	/// своим существом.
	///
	/// Слой сортировки берётся у корневого спрайта сущности: свой Canvas в World Space по умолчанию уходит
	/// в дефолтный слой и ныряет под тайлы карты.
	/// </summary>
	public class WorldBar : MonoBehaviour
	{
		private const string PrefabPath = "Prefabs/UI/WorldBar";

		// Общий контейнер полосок. Заводится с первой полоской: сцены без единого отсчёта (экран входа)
		// пустого узла не получают.
		private const string RootName = "WorldBars";
		private static Transform _root;

		/// <summary>
		/// Контейнер полосок текущей сцены. Прежний уходит вместе со своей сценой, и ссылка на него
		/// сравнивается с null — тогда заводим новый.
		/// </summary>
		private static Transform Root()
		{
			if (_root == null)
				_root = new GameObject(RootName).transform;

			return _root;
		}

		[SerializeField] private Canvas canvas;
		[SerializeField] private Image fill;
		[SerializeField] private Image icon;
		[SerializeField] private Text label;

		private string _shownText;

		/// <summary>
		/// Создать полоску рядом с сущностью: name — имя объекта в иерархии (искать глазами в сцене),
		/// order — порядок отрисовки внутри слоя сущности.
		/// </summary>
		public static WorldBar Create(Transform owner, string name, int order)
		{
			GameObject prefab = Resources.Load<GameObject>(PrefabPath);
			if (prefab == null)
			{
				ConnectController.Error("Не найден префаб полоски " + PrefabPath);
				return null;
			}

			GameObject go = Instantiate(prefab, Root(), false);
			go.name = name;
			go.layer = owner.gameObject.layer;

			WorldBar bar = go.GetComponent<WorldBar>();
			if (bar == null)
			{
				ConnectController.Error("На префабе полоски " + PrefabPath + " нет компонента WorldBar");
				Destroy(go);
				return null;
			}

			// Состав проверяем ЗДЕСЬ, а не в Awake: Awake срабатывает и в редакторе — в момент сборки самого
			// префаба, когда поля ещё не назначены, — и жалоба оттуда рвала бы игроку сессию на ровном месте.
			if (bar.canvas == null || bar.fill == null || bar.icon == null || bar.label == null)
			{
				ConnectController.Error("У префаба полоски " + PrefabPath + " не назначены Canvas/Fill/Icon/Label");
				Destroy(go);
				return null;
			}

			bar.canvas.overrideSorting = true;
			bar.canvas.sortingOrder = order;

			SpriteRenderer ownerRenderer = owner.GetComponent<SpriteRenderer>();
			if (ownerRenderer != null)
				bar.canvas.sortingLayerID = ownerRenderer.sortingLayerID;

			return bar;
		}

		/// <summary>
		/// Шаг стопки полосок в мировых единицах. Считается по ЗНАЧКУ, а не по самой полоске: значок выше
		/// её планки и торчит по обе стороны, потому шаг в высоту планки давал бы наплыв значков друг на друга.
		/// </summary>
		public const float StackStep = 0.21f;

		// Зазор между телом и первой полоской: вплотную к спрайту полоска читается как часть тела.
		private const float GapY = 0.08f;

		/// <summary>
		/// Точка под телом сущности для полоски: drop — сколько ещё опустить (0 — первая полоска,
		/// <see cref="StackStep"/> — следующая под ней).
		///
		/// Отступ считается от НИЖНЕЙ границы видимого тела, а не от позиции сущности: у сущности своя
		/// точка привязки в ногах, и фиксированный отступ от неё у мелкого тела выглядит далёким, у
		/// крупного — налезающим. Тела ещё нет (визуал грузится) — держим полоску у самой точки сущности.
		/// Над телом место занято подписью имени и полоской здоровья, потому сроки собраны снизу.
		/// </summary>
		public static Vector3 PlaceUnder(EntityModel model, Transform owner, float drop)
		{
			Bounds bounds;
			if (model != null && model.TryGetVisualBounds(out bounds))
				return new Vector3(bounds.center.x, bounds.min.y - GapY - drop, owner.position.z);

			return new Vector3(owner.position.x, owner.position.y - GapY - drop, owner.position.z);
		}

		/// <summary>
		/// Показать полоску в мировой точке: part — доля заполнения 0..1, color — цвет заливки,
		/// text — подпись под полоской (пусто — подписи нет), iconSprite — значок слева (null — без значка).
		/// </summary>
		public void Show(Vector3 position, float part, Color color, string text, Sprite iconSprite)
		{
			if (!gameObject.activeSelf)
				gameObject.SetActive(true);

			transform.position = position;

			fill.fillAmount = Mathf.Clamp01(part);
			fill.color = color;

			// Значок красится в цвет заливки: на мелкой полоске он и есть главный признак смысла, и белый
			// значок рядом с красной полоской читался бы как чужой элемент.
			icon.enabled = iconSprite != null;
			if (iconSprite != null)
			{
				if (icon.sprite != iconSprite)
					icon.sprite = iconSprite;

				icon.color = color;
			}

			// Тот же текст повторно не пишем — пересборка меша дорога, а зовут нас каждый кадр.
			if (text != _shownText)
			{
				_shownText = text;
				label.text = text ?? "";
			}

			label.enabled = !string.IsNullOrEmpty(text);
		}

		public void Hide()
		{
			if (gameObject.activeSelf)
				gameObject.SetActive(false);
		}
	}
}
