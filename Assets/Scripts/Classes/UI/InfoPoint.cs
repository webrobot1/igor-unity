using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Пункт списка в окне сведений: значок слева, текст справа. Нужен ради переноса — пункт бывает
    /// длиннее строки, и его продолжение должно вставать под текстом, а не под значком, иначе соседние
    /// пункты сливаются в сплошной абзац. Держит раскладку сам префаб, класс лишь наполняет его.
    ///
    /// Родов у пункта три, и это разные префабы одного класса. Маркер списка — неизменный значок, до
    /// которого классу дела нет вовсе. Иконка компонента рядом с текстом подменяет подпись строки:
    /// подпись тогда читается подсказкой. Одна иконка, без текста вовсе, — так встаёт особенность в
    /// ряду значков: подсказкой читается весь рассказ о ней. Подсказку открывает и наведение, и
    /// нажатие: игра мобильная, наводить там нечем, и палец обязан давать тот же исход.
    /// </summary>
    public class InfoPoint : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>
        /// Текст пункта. Не указан — пункт весь состоит из значка (ряд значков особенностей), и
        /// показывать текстом нечего: рассказ о свойстве уходит в подсказку.
        /// </summary>
        [SerializeField]
        private Text text;

        /// <summary>
        /// Иконка компонента. Не указана — у этого пункта её не бывает вовсе (пункт-маркер), и строка
        /// всегда встаёт текстом.
        /// </summary>
        [SerializeField]
        private Image icon;

        /// <summary>
        /// Подсказка окна: своей ссылки на неё у пункта нет — он рождается в рантайме, и поле
        /// Inspector'а заполнить некому (так же наполняются иконки заклинаний и добычи).
        /// </summary>
        private Tooltip _tooltip;

        /// <summary>
        /// Что показать подсказкой. null — показывать нечего: подписи у строки нет либо она осталась
        /// в самом тексте.
        /// </summary>
        private string _hint;

        /// <summary>
        /// Наполнить пункт готовой строкой. Иконку кладём, только когда картинка нашлась: архив
        /// картинок бывает устаревшим, и на битой строка обязана вернуться к тексту с подписью —
        /// одно значение без неё не сказало бы, чего оно.
        /// </summary>
        public void SetRow(InfoRow row, Tooltip tooltip)
        {
            if (text == null && icon == null)
            {
                ConnectController.Error("в префабе пункта списка не указаны ни текст, ни значок");
                return;
            }

            _tooltip = tooltip;

            Sprite sprite = icon != null && row.Icon != null
                ? AnimationCacheService.GetComponentSprite(BaseController.GAME_ID, row.Icon)
                : null;

            if (icon != null)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.gameObject.SetActive(sprite != null);
            }

            if (text != null)
                text.text = sprite != null ? row.Value : row.Text;

            // Картинка не встала (кеш картинок устарел): у пункта с текстом строка вернулась к полной
            // фразе, и подсказке нечего добавить; у пункта из одного значка показывать больше нечем —
            // подсказку оставляем, иначе о свойстве не узнать вовсе.
            _hint = sprite != null ? row.Hint : text == null ? row.Text : null;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            ShowHint();
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            ShowHint();
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        /// <summary>
        /// Показать подпись строки. Зовут её и наведение, и нажатие — исход у них один: на телефоне
        /// наводить нечем, и подсказка обязана открываться пальцем.
        ///
        /// Подсказке отдаём весь пункт, а не значок: она встаёт сбоку от того, что ей передали, а
        /// строка занимает окно почти целиком — от значка панель легла бы поверх соседних строк.
        /// </summary>
        private void ShowHint()
        {
            if (_tooltip == null || string.IsNullOrEmpty(_hint))
                return;

            _tooltip.Show((RectTransform)transform, _hint);
        }
    }
}
