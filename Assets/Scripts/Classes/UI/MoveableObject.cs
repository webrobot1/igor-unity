using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public abstract class MoveableObject : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField]
        protected Image image;

        /// <summary>
        /// Назначается контроллером при создании объекта
        /// </summary>
        protected Tooltip tooltip;

        /// <summary>
        /// Нужен для курсора (перетаскивание картинки предмета)
        /// </summary>
        public Image Image
        {
            get { return image; }
            set { }
        }

        /// <summary>
        /// Это вызывается и при перетаскивании и при вызове (и из книги/инвентаря и из быстрого доступа)
        /// </summary>
        /// <param name="gameObject">
        /// На какой объект перетаскивается (из быстрого меню просто null)
        /// </param>
        public abstract void Use(Vector2 pos = new Vector2(), GameObject obj = null);

        protected virtual void Awake()
        {
            if (image == null)
                ConnectController.Error("не найден объект sprite в для элемента Заклинания в книге");
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            CursorController.TakeMoveable(this);
        }

        public virtual string GetTooltipText() { return null; }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip == null) return;
            string text = GetTooltipText();
            if (text != null)
                tooltip.Show(transform.position, text);
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null)
                tooltip.Hide();
        }
    }
}
