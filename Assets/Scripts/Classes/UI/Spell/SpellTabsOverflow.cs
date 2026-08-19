using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Указатели, что вкладок в ленте больше, чем помещается: стрелка горит с той стороны, где вкладки
    /// ещё есть. Полосы прокрутки у ленты нет — она тянется пальцем, и без указателя скрытые вкладки ничем
    /// себя не выдают: игрок видит ровно тот край, на котором стоит, и считает его последним. Нажатие на
    /// саму стрелку двигает ленту на шаг: мышью тянуть узкую полосу неудобно, а стрелка уже указывает,
    /// куда двигаться.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class SpellTabsOverflow : MonoBehaviour
    {
        /// <summary>Запас в точках, ниже которого сдвиг считается упором в край.</summary>
        private const float EDGE = 1f;

        /// <summary>Доля видимой ширины ленты, на которую двигает одно нажатие стрелки.</summary>
        private const float STEP = 0.75f;

        [SerializeField] private Button left;
        [SerializeField] private Button right;

        private ScrollRect _scroll;
        private RectTransform _viewport;

        /// <summary>Сколько ленты не помещается в видимую часть. Меньше запаса — прокручивать нечего.</summary>
        private float Hidden
        {
            get { return _scroll.content.rect.width - _viewport.rect.width; }
        }

        /// <summary>На сколько лента уже сдвинута от левого края.</summary>
        private float Shift
        {
            get { return -_scroll.content.anchoredPosition.x; }
        }

        private void Awake()
        {
            _scroll = GetComponent<ScrollRect>();
            _viewport = transform as RectTransform;

            if (left == null || right == null)
            {
                ConnectController.Error("у ленты вкладок книги заклинаний не назначены указатели скрытых вкладок");
                return;
            }

            left.onClick.AddListener(() => Scroll(-1f));
            right.onClick.AddListener(() => Scroll(1f));
        }

        private void LateUpdate()
        {
            if (left == null || right == null || _scroll == null || _scroll.content == null)
                return;

            // считаем от фактической ширины: вкладки создаются по книге игрока и меняются в рантайме,
            // а ширина каждой зависит от длины названия стихии
            float hidden = Hidden;

            left.gameObject.SetActive(hidden > EDGE && Shift > EDGE);
            right.gameObject.SetActive(hidden > EDGE && Shift < hidden - EDGE);
        }

        /// <summary>
        /// Сдвиг ленты на шаг: -1 — к первым вкладкам, +1 — к последним. Дальше края не уходим —
        /// иначе лента отскакивает назад упругостью ScrollRect'а.
        /// </summary>
        private void Scroll(float direction)
        {
            float hidden = Hidden;

            if (hidden <= EDGE)
                return;

            Vector2 position = _scroll.content.anchoredPosition;
            position.x = -Mathf.Clamp(Shift + direction * _viewport.rect.width * STEP, 0f, hidden);

            // Инерцию гасим: она несёт ленту дальше нажатого шага, и стрелка перестаёт быть шагом.
            _scroll.velocity = Vector2.zero;
            _scroll.content.anchoredPosition = position;
        }
    }
}
