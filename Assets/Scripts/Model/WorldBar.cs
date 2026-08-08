using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Полоска над сущностью: доля оставшегося срока, иконка смысла и подпись под ней. Заменяет собой
	/// числовые отсчёты — доля видна с одного взгляда, а число заставляет читать и сравнивать.
	///
	/// Полоска — СОСЕД сущности, а не её ребёнок: EntityModel.TryGetVisualBounds считает границы по дочерним
	/// SpriteRenderer'ам, и висящая над телом полоска раздула бы и кольцо-подсветку, и кликабельный коллайдер.
	/// Позиция сводится с сущностью покадрово владельцем полоски.
	///
	/// Слой сортировки берётся у корневого спрайта сущности: свой Canvas в World Space по умолчанию уходит
	/// в дефолтный слой и ныряет под тайлы карты.
	/// </summary>
	public class WorldBar : MonoBehaviour
	{
		private const string PrefabPath = "Prefabs/UI/WorldBar";

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

			GameObject go = Instantiate(prefab, owner.parent, false);
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
