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
    public class WorldMapDrag : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler,
                                               IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Контейнер раскладки — тот же, что двигает увеличение.</summary>
        [SerializeField]
        private RectTransform content;

        /// <summary>
        /// Указатель над областью — раскрытая ладонь: единственный признак того, что карту вообще можно тащить,
        /// само окно об этом не говорит, а стрелка над ним читается как «тут нечего делать». Тащим — ладонь
        /// сжата: принятая пара состояний, по ней видно, что карта именно схвачена. Лежат в Resources, а не
        /// ссылкой в сцене: это ассеты курсора, а не объекты интерфейса, и назначать их руками в каждом
        /// окне-потребителе незачем.
        /// </summary>
        private static Texture2D handOpen;

        private static Texture2D handClosed;

        /// <summary>
        /// Точка картинки, которой указатель «попадает» в экран. Значения — из самих курсоров системной темы,
        /// откуда взяты картинки: у раскрытой и сжатой ладони они разные, и при подмене картинки без подмены
        /// точки рука прыгала бы в момент захвата.
        /// </summary>
        private static readonly Vector2 HOTSPOT_OPEN = new Vector2(11, 2);

        private static readonly Vector2 HOTSPOT_CLOSED = new Vector2(9, 5);

        /// <summary>
        /// Идёт ли перетаскивание. Указатель во время тяги уходит за границы области (карту тащат «наотмашь»),
        /// и приходящий выход не должен снимать сжатую ладонь — иначе она мигала бы на каждом заходе за край.
        /// </summary>
        private bool dragging;

        private void Awake()
        {
            if (content == null)
                BaseController.Error("Карта мира: перетаскиванию не присвоен контейнер раскладки content");

            if (handOpen == null)
            {
                handOpen = Resources.Load<Texture2D>("Cursors/hand");

                if (handOpen == null)
                    BaseController.Error("Карта мира: не найдена картинка указателя Resources/Cursors/hand");
            }

            if (handClosed == null)
            {
                handClosed = Resources.Load<Texture2D>("Cursors/hand_drag");

                if (handClosed == null)
                    BaseController.Error("Карта мира: не найдена картинка указателя Resources/Cursors/hand_drag");
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!dragging)
                Cursor.SetCursor(handOpen, HOTSPOT_OPEN, CursorMode.Auto);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!dragging)
                ResetCursor();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = true;
            Cursor.SetCursor(handClosed, HOTSPOT_CLOSED, CursorMode.Auto);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;

            // Отпустили над областью — рука снова раскрыта, тащить можно дальше; отпустили за её пределами —
            // возвращаем обычный указатель: выход, пришедший во время тяги, мы намеренно пропустили.
            RectTransform area = (RectTransform)transform;
            if (RectTransformUtility.RectangleContainsScreenPoint(area, eventData.position, eventData.pressEventCamera))
                Cursor.SetCursor(handOpen, HOTSPOT_OPEN, CursorMode.Auto);
            else
                ResetCursor();
        }

        /// <summary>
        /// Окно карты гасится прозрачностью группы, а не выключением объекта, поэтому уход указателя за
        /// закрытое окно приходит обычным OnPointerExit. Сброс тут — на случай, когда область всё же
        /// выключают: события выхода тогда не будет вовсе, и ладонь осталась бы висеть на всём экране.
        /// </summary>
        private void OnDisable()
        {
            dragging = false;
            ResetCursor();
        }

        private void ResetCursor()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
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
