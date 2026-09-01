using Spine.Unity;
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
        /// Стрелка роста поверх значка: ставится строке о ПРИБЫЛИ запаса — значок там заимствован у
        /// самого запаса и сказал бы лишь «здоровье», а не «здоровье прибывает». Не указана — у этого
        /// рода пункта стрелки не бывает вовсе (маркер списка, строка без значка).
        /// </summary>
        [SerializeField]
        private Image gain;

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
        /// Компонент, чей скелет стоит в значке сейчас. null — значок стоит картинкой либо его нет вовсе.
        /// Пункт наполняется заново на каждую смену значения строки (в бою — то и дело), а сборка скелета
        /// — работа под смену компонента, не под неё.
        /// </summary>
        private string _skeleton;

        /// <summary>
        /// Наполнить пункт готовой строкой. Значок кладём, только когда он встал: архив картинок бывает
        /// устаревшим, скелет — ещё не скачанным, и без значка строка обязана вернуться к тексту с
        /// подписью — одно значение без неё не сказало бы, чего оно.
        /// </summary>
        public void SetRow(InfoRow row, Tooltip tooltip)
        {
            if (text == null && icon == null)
            {
                ConnectController.Error("в префабе пункта списка не указаны ни текст, ни значок");
                return;
            }

            _tooltip = tooltip;

            bool shown = ApplyIcon(row.Icon);

            // Стрелка живёт вместе со значком: значок не встал — строка вернулась к полной фразе, и
            // помечать стрелкой стало нечего, о росте там сказано словами.
            if (gain != null)
                gain.gameObject.SetActive(row.Gain && shown);

            if (text != null)
                text.text = shown ? row.Value : row.Text;

            // Подсказка несёт ровно то, чего пункт не показал сам. Пункт из одного значка не показал
            // ничего — ему достаётся фраза целиком; пункт со значком и значением не показал подписи —
            // ему достаётся она. Под тем и другим идёт описание свойства, если оно есть.
            // Значок не встал (кеш устарел): строка вернулась к полной фразе, и подсказке нечего
            // добавить — кроме случая, когда текста у пункта нет вовсе.
            string head = text == null ? row.Text : shown ? row.Caption : null;

            _hint = string.IsNullOrEmpty(head) ? null
                : string.IsNullOrEmpty(row.Description) ? head : head + "\n" + row.Description;
        }

        /// <summary>
        /// Поставить значок компонента. Форм у него две, и компонент несёт ровно одну: картинку из архива
        /// игры либо скелет анимации — тогда значок живой и клип идёт по кругу. Скелет рисуется дочерним
        /// объектом самого значка, а картинка значка при этом пустеет: без неё поверх скелета лёг бы белый
        /// прямоугольник. Гасим её прозрачностью, а не выключением: нажатия и наведение ловит она, и
        /// выключенная не открыла бы подсказку — у пункта из одного значка это весь его рассказ.
        ///
        /// Каким движением быть значку, решает игра: клип приходит вместе с адресом существа. Не пришёл —
        /// отбирать было не из чего, и клип берёт сам сборщик скелета.
        ///
        /// false — значка нет: у пункта его не бывает вовсе (маркер списка), у компонента он не задан,
        /// картинка битая либо пакета скелета ещё нет в кеше. Качать пакет тут нечем — его кладёт
        /// предзагрузка перед входом в игру.
        /// </summary>
        private bool ApplyIcon(string component)
        {
            if (icon == null)
                return false;

            Sprite sprite = component != null
                ? AnimationCacheService.GetComponentSprite(BaseController.GAME_ID, component)
                : null;

            ComponentCacheService.IconAnimation animation = sprite == null && component != null
                ? ComponentCacheService.GetAnimation(component)
                : null;

            if (animation == null)
            {
                if (_skeleton != null)
                {
                    VisualBuilder.Clear(icon.gameObject);
                    _skeleton = null;
                }
            }
            else if (_skeleton != component)
            {
                SkeletonDataAsset asset = SpineCacheService.GetCached(
                    BaseController.GAME_ID, animation.animation, animation.entity, out string failure);

                if (failure != null)
                    Debug.LogWarning("Значок компонента " + component + ": " + failure);

                _skeleton = asset != null
                    && VisualBuilder.CreateGraphic(icon.gameObject, asset, animation.clip) != null
                    ? component : null;

                if (_skeleton == null)
                    VisualBuilder.Clear(icon.gameObject);
            }

            bool shown = sprite != null || _skeleton != null;

            icon.sprite = sprite;
            icon.preserveAspect = true;

            Color color = icon.color;
            color.a = sprite != null ? 1f : 0f;
            icon.color = color;

            icon.gameObject.SetActive(shown);
            return shown;
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
