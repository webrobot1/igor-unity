using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    /// <summary>
    /// Перетаскивание обзорной карты пальцем либо мышью: тащит контейнер раскладки внутри области показа.
    /// Висит на самой области — тащить можно за любое её место, а не только за картинку карты, и на телефоне
    /// попадать в узкую полосу между картами не нужно.
    ///
    /// Пределы сдвига считает контроллер карты (<see cref="WorldMapController.ClampWorldMapShift"/>): они
    /// зависят от текущего увеличения, а оно там же и живёт. Своей копии правила компонент не держит.
    /// </summary>
    public class WorldMapDrag : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, ITakeable
    {
        /// <summary>Контейнер раскладки — тот же, что двигает увеличение.</summary>
        [SerializeField]
        private RectTransform content;

        /// <summary>
        /// Идёт ли перетаскивание карты. Пока тянут, форму указателя держит эта область, а не общий
        /// разбор наведения: карту тащат «наотмашь», указатель при этом уходит за её края, и сжатая
        /// ладонь иначе мигала бы на каждом заходе за край.
        /// </summary>
        public static bool Dragging { get; private set; }

        /// <summary>
        /// Карту можно потянуть в любой момент — само окно об этом не говорит, а стрелка над ним читается
        /// как «тут нечего делать». Признак тот же, по которому руку получают карточки заклинаний и ячейки:
        /// форму указателя на всю игру решает <see cref="CursorController"/>.
        /// </summary>
        public bool CanTake
        {
            get { return true; }
        }

        private void Awake()
        {
            if (content == null)
                BaseController.Error("Карта мира: перетаскиванию не присвоен контейнер раскладки content");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Dragging = true;
            HandCursor.Set(HandCursor.Shape.Closed);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Dragging = false;
        }

        /// <summary>
        /// Окно карты гасится прозрачностью группы, а не выключением объекта, поэтому обычно тяга
        /// заканчивается сама. Сброс тут — на случай, когда область всё же выключают прямо во время
        /// тяги: события конца не будет вовсе, и указатель остался бы сжатым на всём экране.
        /// </summary>
        private void OnDisable()
        {
            Dragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // delta приходит в пикселях экрана, а холст масштабируется под разрешение — переводим через
            // масштаб холста, иначе на телефоне карта уезжала бы быстрее пальца.
            float canvasScale = GetComponentInParent<Canvas>().scaleFactor;

            MainController.Instance.MoveWorldMap(content.anchoredPosition + eventData.delta / canvasScale);
        }
    }
}
