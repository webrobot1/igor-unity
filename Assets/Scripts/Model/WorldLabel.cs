using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Надпись над сущностью в мире: имя предмета, существа либо игрока. Вид — шрифт, кегль, цвета текста
	/// и плашки, отступы — целиком в префабе: одному и тому же коду разные подписи (вещь на земле, моб,
	/// игрок) задаются РАЗНЫМИ префабами, а не ветвлением здесь.
	///
	/// Ширина плашки идёт за длиной имени сама (раскладка группы + подгонка размера содержимым в префабе);
	/// код лишь просит пересобрать раскладку сразу после смены текста, чтобы клик и подъём считались по
	/// уже готовому размеру, а не по прошлому кадру.
	///
	/// Надпись кликабельна: у неё свой коллайдер, и клик по нему считается кликом по самой сущности
	/// (CursorController поднимается к сущности-родителю). Скрытая надпись не рисуется и кликов не ловит —
	/// иначе над сущностью висела бы невидимая кликабельная зона.
	///
	/// Позицию и масштаб держит ВЛАДЕЛЕЦ (HoverLabel): подпись знает свой вид, но не то, над чем висит.
	/// </summary>
	public class WorldLabel : MonoBehaviour
	{
		[SerializeField] private Canvas canvas;
		[SerializeField] private CanvasGroup group;
		[SerializeField] private Text label;
		[SerializeField] private BoxCollider2D clickArea;

		private RectTransform _rect;

		/// <summary>
		/// Создать надпись ребёнком сущности: prefabPath — префаб вида (путь внутри Resources),
		/// name — имя объекта в иерархии (искать глазами в сцене), order — порядок отрисовки внутри
		/// слоя сущности. Ребёнком, а не соседом: клик по надписи должен доставаться сущности, а её
		/// ищут вверх по иерархии.
		/// </summary>
		public static WorldLabel Create(Transform owner, string prefabPath, string name, int order)
		{
			GameObject prefab = Resources.Load<GameObject>(prefabPath);
			if (prefab == null)
			{
				ConnectController.Error("Не найден префаб надписи " + prefabPath);
				return null;
			}

			GameObject go = Instantiate(prefab, owner, false);
			go.name = name;
			go.layer = owner.gameObject.layer;

			WorldLabel worldLabel = go.GetComponent<WorldLabel>();
			if (worldLabel == null)
			{
				ConnectController.Error("На префабе надписи " + prefabPath + " нет компонента WorldLabel");
				Destroy(go);
				return null;
			}

			// Состав проверяем ЗДЕСЬ, а не в Awake: Awake срабатывает и в редакторе — в момент сборки самого
			// префаба, когда поля ещё не назначены, — и жалоба оттуда рвала бы игроку сессию на ровном месте.
			if (worldLabel.canvas == null || worldLabel.group == null || worldLabel.label == null || worldLabel.clickArea == null)
			{
				ConnectController.Error("У префаба надписи " + prefabPath + " не назначены Canvas/CanvasGroup/Text/BoxCollider2D");
				Destroy(go);
				return null;
			}

			worldLabel._rect = go.GetComponent<RectTransform>();

			worldLabel.canvas.overrideSorting = true;
			worldLabel.canvas.sortingOrder = order;

			// Свой Canvas в World Space иначе уходит в слой по умолчанию и ныряет под тайлы карты.
			SpriteRenderer ownerRenderer = owner.GetComponent<SpriteRenderer>();
			if (ownerRenderer != null)
				worldLabel.canvas.sortingLayerID = ownerRenderer.sortingLayerID;

			worldLabel.SetAlpha(0f);
			return worldLabel;
		}

		/// <summary>Текст надписи. Тот же повторно не пишем — пересборка раскладки дорога, а зовут нас каждый кадр.</summary>
		public void SetText(string text)
		{
			string value = text ?? "";
			if (label.text == value) return;

			label.text = value;

			// Раскладка пересобирается СРАЗУ: до конца кадра размер плашки остаётся прошлым, а по нему тут же
			// считаются подъём надписи и её кликабельная зона.
			LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
			clickArea.size = _rect.rect.size;
			clickArea.offset = Vector2.zero;
		}

		/// <summary>
		/// Видимость надписи: 0 — скрыта совсем (не рисуется и не ловит кликов), 1 — показана.
		/// Промежуточные значения даёт плавное появление у владельца.
		/// </summary>
		public void SetAlpha(float alpha)
		{
			bool shown = alpha > 0.001f;

			group.alpha = alpha;
			canvas.enabled = shown;
			clickArea.enabled = shown;
		}

		/// <summary>Кликабельная зона надписи: по ней владелец решает, держит ли надпись курсор.</summary>
		public Collider2D ClickArea
		{
			get { return clickArea; }
		}

		/// <summary>Высота плашки в клетках мира — по ней владелец поднимает надпись над телом.</summary>
		public float WorldHeight
		{
			get { return _rect.rect.height * Mathf.Abs(transform.lossyScale.y); }
		}
	}
}
