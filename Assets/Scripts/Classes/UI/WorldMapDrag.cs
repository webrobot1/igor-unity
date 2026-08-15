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
    public class WorldMapDrag : MonoBehaviour, IDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Контейнер раскладки — тот же, что двигает увеличение.</summary>
        [SerializeField]
        private RectTransform content;

        /// <summary>
        /// Картинка указателя-ладони: единственный признак того, что карту вообще можно тащить — само окно
        /// об этом не говорит, а стрелка над ним читается как «тут нечего делать». Лежит в Resources, а не
        /// ссылкой в сцене: это ассет курсора, а не объект интерфейса, и назначать его руками в каждом
        /// окне-потребителе незачем.
        /// </summary>
        private static Texture2D handCursor;

        /// <summary>
        /// Точка картинки, которой указатель «попадает» в экран — середина ладони.
        /// </summary>
        private static readonly Vector2 HAND_HOTSPOT = new Vector2(15, 18);

        private void Awake()
        {
            if (content == null)
                BaseController.Error("Карта мира: перетаскиванию не присвоен контейнер раскладки content");

            if (handCursor == null)
            {
                handCursor = Resources.Load<Texture2D>("Cursors/hand");

                if (handCursor == null)
                    BaseController.Error("Карта мира: не найдена картинка указателя Resources/Cursors/hand");
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Cursor.SetCursor(handCursor, HAND_HOTSPOT, CursorMode.Auto);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ResetCursor();
        }

        /// <summary>
        /// Окно карты гасится прозрачностью группы, а не выключением объекта, поэтому уход указателя за
        /// закрытое окно приходит обычным OnPointerExit. Сброс тут — на случай, когда область всё же
        /// выключают: события выхода тогда не будет вовсе, и ладонь осталась бы висеть на всём экране.
        /// </summary>
        private void OnDisable()
        {
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
