using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public abstract class MoveableObject : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField]
        protected Image image;

        // Видимая иконка (child корня). На неё ложится sprite + localScale = 1/serverSize.
        // Корневой image остаётся для layout preferred-size (LayoutGroup spellbook'а читает
        // image.sprite.rect) и color-cascade в ActionBar; делается невидимым через color.a=0
        // в prefab'е. Если icon==null — fallback на старое поведение (sprite/scale на корне).
        [SerializeField]
        protected Image icon;

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
        /// Видимая иконка (child), на которой применяется server size через localScale=1/size.
        /// Может быть null на старых instance'ах префабов — вызывающий код должен делать fallback на Image.
        /// </summary>
        public Image Icon
        {
            get { return icon; }
        }

        /// <summary>
        /// Унифицированная привязка sprite'а и server size к UI-элементу. Используется Spell.Magic и Item.SetData.
        /// Корневой image получает sprite (нужен для LayoutGroup preferred-size + ActionBar mirror),
        /// но остаётся невидимым (color.a=0 в prefab'е). Видимый icon-child получает тот же sprite
        /// и localScale = 1/serverSize (вариант 1 из TASK_ui_icon_size.md).
        /// </summary>
        protected void ApplyPrefabImage(string prefab)
        {
            Sprite sprite = AnimationCacheService.GetPrefabSprite(BaseController.GAME_ID, prefab)
                ?? Resources.Load<Sprite>("unknow");
            float? size = AnimationCacheService.GetPrefabSize(prefab);
            float k = (size.HasValue && size.Value > 0.0001f) ? 1f / size.Value : 1f;

            if (image != null)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                // image.color.a не трогаем — prefab держит 0 для невидимости
            }
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.transform.localScale = new Vector3(k, k, 1f);
            }
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

        /// <summary>
        /// На кулдауне ли объект (заклинание, предмет и т.д.)
        /// </summary>
        public virtual bool IsOnCooldown() { return false; }

        /// <summary>
        /// Прогресс кулдауна: fillAmount (0..1) и оставшееся время в секундах. Для UI overlay.
        /// </summary>
        public virtual (float fillAmount, float remainSeconds) GetCooldownProgress() { return (0f, 0f); }

        /// <summary>
        /// Стоимость маны объекта (заклинание и т.д.). 0 = нет стоимости.
        /// </summary>
        public virtual int ManaCost { get { return 0; } set { } }

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
