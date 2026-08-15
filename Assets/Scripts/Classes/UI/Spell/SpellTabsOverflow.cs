using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Указатели, что вкладок в ленте больше, чем помещается: стрелка горит с той стороны, где вкладки
    /// ещё есть. Полосы прокрутки у ленты нет — она тянется пальцем, и без указателя скрытые вкладки ничем
    /// себя не выдают: игрок видит ровно тот край, на котором стоит, и считает его последним.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class SpellTabsOverflow : MonoBehaviour
    {
        /// <summary>Запас в точках, ниже которого сдвиг считается упором в край.</summary>
        private const float EDGE = 1f;

        [SerializeField] private GameObject left;
        [SerializeField] private GameObject right;

        private ScrollRect _scroll;
        private RectTransform _viewport;

        private void Awake()
        {
            _scroll = GetComponent<ScrollRect>();
            _viewport = transform as RectTransform;

            if (left == null || right == null)
                ConnectController.Error("у ленты вкладок книги заклинаний не назначены указатели скрытых вкладок");
        }

        private void LateUpdate()
        {
            if (left == null || right == null || _scroll == null || _scroll.content == null)
                return;

            // считаем от фактической ширины: вкладки создаются по книге игрока и меняются в рантайме,
            // а ширина каждой зависит от длины названия стихии
            float hidden = _scroll.content.rect.width - _viewport.rect.width;
            float shift = -_scroll.content.anchoredPosition.x;

            left.SetActive(hidden > EDGE && shift > EDGE);
            right.SetActive(hidden > EDGE && shift < hidden - EDGE);
        }
    }
}
