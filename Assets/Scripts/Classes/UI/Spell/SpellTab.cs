using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Вкладка книги заклинаний — стихия, к которой относятся её заклинания («Огонь», «Тело»). Название
    /// приходит с самим заклинанием и идёт подписью как есть: перечня стихий у книги нет, вкладки целиком
    /// следуют данным. Своих данных вкладка не хранит: показ и выбор ведёт <see cref="SpellBookController"/>.
    /// </summary>
    public class SpellTab : MonoBehaviour
    {
        /// <summary>Прозрачность невыбранной вкладки — та же, которой гасятся недоступные иконки.</summary>
        private const float UNSELECTED_ALPHA = 0.5f;

        /// <summary>Отступ подписи от края вкладки с каждой стороны.</summary>
        private const float PADDING = 18f;

        [SerializeField] private Text title;
        [SerializeField] private Image background;
        [SerializeField] private Button button;
        [SerializeField] private LayoutElement layout;

        /// <summary>Стихия, за которую отвечает вкладка.</summary>
        public string Element { get; private set; }

        /// <summary>
        /// Назначение вкладки стихии. Ссылки префаба проверяются здесь, а не в Awake: Awake отрабатывает
        /// и при сборке самого префаба редакторным скриптом, где поля ещё не назначены.
        /// </summary>
        public void Setup(string element, Action<SpellTab> click)
        {
            if (title == null || background == null || button == null || layout == null)
            {
                ConnectController.Error("в префабе вкладки книги заклинаний не назначены подпись, фон, кнопка либо размер");
                return;
            }

            Element = element;
            title.text = element;
            name = element;

            // Ширину вкладки задаёт её подпись: групп в книге сколько угодно, и длина имени у них разная.
            // Считаем явно — иначе раскладка возьмёт ширину у фоновой плашки (Image сам участвует в
            // раскладке и отдаёт нативный размер спрайта), и все вкладки станут одинаково широкими.
            layout.preferredWidth = title.preferredWidth + PADDING * 2f;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => click(this));
        }

        public void SetSelected(bool selected)
        {
            if (background == null)
                return;

            Color color = background.color;
            color.a = selected ? 1f : UNSELECTED_ALPHA;
            background.color = color;
        }
    }
}
