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
    public class WorldMapDrag : MonoBehaviour, IDragHandler
    {
        /// <summary>Контейнер раскладки — тот же, что двигает увеличение.</summary>
        [SerializeField]
        private RectTransform content;

        private void Awake()
        {
            if (content == null)
                BaseController.Error("Карта мира: перетаскиванию не присвоен контейнер раскладки content");
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
