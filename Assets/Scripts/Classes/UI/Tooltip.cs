using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    public class Tooltip : MonoBehaviour
    {
        [SerializeField]
        private CanvasGroup canvasGroup;

        [SerializeField]
        private Text tooltipText;

        /// <summary>Предел ширины подсказки в пикселях интерфейса — дальше текст переносится по словам.</summary>
        private const float MAX_WIDTH = 420f;

        /// <summary>Зазор между подсказкой и тем, о чём она: вплотную панель читается как часть окна.</summary>
        private const float GAP = 8f;

        // Ширину держим через LayoutElement самого текста: ContentSizeFitter панели считает её по нему.
        private LayoutElement width;
        private RectTransform rect;

        // углы того, о чём подсказка: по ним она и встаёт сбоку
        private readonly Vector3[] corners = new Vector3[4];

        void Awake()
        {
            // Error() не бросает: без return строки ниже разыменовали бы те же null'ы
            // (tooltipText — сразу, canvasGroup — в конце метода).
            if (canvasGroup == null)
            {
                ConnectController.Error("не указан CanvasGroup для Tooltip");
                return;
            }

            if (tooltipText == null)
            {
                ConnectController.Error("не указан Text для Tooltip");
                return;
            }

            rect = GetComponent<RectTransform>();

            width = tooltipText.GetComponent<LayoutElement>();
            if (width == null)
            {
                ConnectController.Error("не назначен LayoutElement на тексте подсказки — нечем ограничить её ширину");
                return;
            }

            canvasGroup.alpha = 0;
            canvasGroup.blocksRaycasts = false;
        }

        /// <summary>
        /// Показать подсказку о том, что занимает <paramref name="source"/>. Спрашивают её у ячейки, у
        /// значка, у строки списка — панель шире любого из них, поэтому встаёт она СБОКУ (см. Place):
        /// выросшая от точки внутри источника, она закрывала бы соседей, о которых речи не было.
        /// </summary>
        public void Show(RectTransform source, string text)
        {
            tooltipText.text = text;

            // Ширина по тексту, но не больше потолка: длинное описание иначе растягивает подсказку в
            // полосу через весь экран (панель тянет ContentSizeFitter по длине строки без переносов).
            // Зажимаем ширину — перенос по словам и рост в высоту дальше делают сам Text и Fitter.
            // Короткому тексту потолок не мешает: у него своя ширина меньше, пустой панели не будет.
            width.preferredWidth = Mathf.Min(tooltipText.preferredWidth, MAX_WIDTH);

            // Размер после переноса известен только пересчитанной раскладке, а он и решает, с какой
            // стороны панель помещается и не уходит ли за край экрана.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Place(source);

            canvasGroup.alpha = 1;
        }

        /// <summary>
        /// Поставить подсказку рядом с источником, ничего им не закрывая: слева от него, а не хватило
        /// места до края экрана — справа. Верх панели равняем по верху источника — так видно, о чём она.
        /// </summary>
        private void Place(RectTransform source)
        {
            source.GetWorldCorners(corners);

            Vector2 size = Vector2.Scale(rect.rect.size, rect.lossyScale);

            float left = corners[0].x - GAP - size.x;

            if (left < 0)
            {
                float right = corners[2].x + GAP;

                if (right + size.x <= Screen.width)
                    left = right;
            }

            float bottom = corners[1].y - size.y;

            // Позиция — точка pivot'а панели, а считали мы её края: переводим одно в другое, чтобы
            // сам pivot можно было держать в префабе любым.
            rect.position = new Vector3(left + rect.pivot.x * size.x, bottom + rect.pivot.y * size.y, rect.position.z);

            ClampToScreen();
        }

        /// <summary>
        /// Удержать подсказку целиком в пределах экрана: у краёв панель вылезала бы за границу — там её
        /// не прочитать. Двигаем ровно на величину выхода, чтобы подсказка оставалась у своего места.
        /// Место сбоку не всегда есть вовсе (узкий экран) — тогда сдвиг кладёт панель поверх источника,
        /// и это лучше, чем нечитаемая подсказка за краем.
        /// </summary>
        private void ClampToScreen()
        {
            Vector2 size = Vector2.Scale(rect.rect.size, rect.lossyScale);
            Vector3 pos = rect.position;

            float left = pos.x - rect.pivot.x * size.x;
            float bottom = pos.y - rect.pivot.y * size.y;

            if (left + size.x > Screen.width) pos.x -= left + size.x - Screen.width;
            if (left < 0) pos.x -= left;
            if (bottom + size.y > Screen.height) pos.y -= bottom + size.y - Screen.height;
            if (bottom < 0) pos.y -= bottom;

            rect.position = pos;
        }

        public void Hide()
        {
            canvasGroup.alpha = 0;
        }
    }
}
