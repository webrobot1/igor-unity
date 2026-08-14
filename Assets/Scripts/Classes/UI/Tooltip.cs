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

        // Ширину держим через LayoutElement самого текста: ContentSizeFitter панели считает её по нему.
        private LayoutElement width;
        private RectTransform rect;

        void Awake()
        {
            if (canvasGroup == null)
                ConnectController.Error("не указан CanvasGroup для Tooltip");

            if (tooltipText == null)
                ConnectController.Error("не указан Text для Tooltip");

            rect = GetComponent<RectTransform>();

            width = tooltipText.GetComponent<LayoutElement>();
            if (width == null)
                ConnectController.Error("не назначен LayoutElement на тексте подсказки — нечем ограничить её ширину");

            canvasGroup.alpha = 0;
            canvasGroup.blocksRaycasts = false;
        }

        public void Show(Vector3 position, string text)
        {
            tooltipText.text = text;

            // Ширина по тексту, но не больше потолка: длинное описание иначе растягивает подсказку в
            // полосу через весь экран (панель тянет ContentSizeFitter по длине строки без переносов).
            // Зажимаем ширину — перенос по словам и рост в высоту дальше делают сам Text и Fitter.
            // Короткому тексту потолок не мешает: у него своя ширина меньше, пустой панели не будет.
            width.preferredWidth = Mathf.Min(tooltipText.preferredWidth, MAX_WIDTH);

            transform.position = position;
            canvasGroup.alpha = 1;

            // Размеры после переноса известны только пересчитанной раскладке, а зажать позицию нужно уже
            // по ним: подсказка выросла в высоту и у нижних ячеек ушла бы за край экрана.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            ClampToScreen();
        }

        /// <summary>
        /// Удержать подсказку целиком в пределах экрана: точку показа задаёт ячейка, и у краёв панель
        /// вылезала бы за границу — там её не прочитать. Двигаем ровно на величину выхода, чтобы
        /// подсказка оставалась у своей ячейки.
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
