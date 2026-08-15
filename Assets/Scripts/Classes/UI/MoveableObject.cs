using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    public abstract class MoveableObject : MonoBehaviour
    {
        [SerializeField]
        protected Image image;

        // Видимая иконка (child корня). На неё ложится sprite; localScale=1 (вариант 2 — size в UI не влияет).
        // Корневой image остаётся для layout preferred-size (LayoutGroup spellbook'а читает
        // image.sprite.rect) и color-cascade в ActionBar; делается невидимым через color.a=0
        // в prefab'е. Если icon==null — fallback на старое поведение (sprite на корне).
        [SerializeField]
        protected Image icon;

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
        /// Унифицированная привязка sprite'а к UI-элементу. Используется Spell.Magic и Item.SetData.
        /// Корневой image получает sprite (нужен для LayoutGroup preferred-size + ActionBar mirror),
        /// но остаётся невидимым (color.a=0 в prefab'е). Видимый icon-child получает тот же sprite.
        ///
        /// Вариант 2 из TASK_ui_icon_size.md: server size в UI НЕ применяется — иконка всегда занимает
        /// слот целиком (preserveAspect для вытянутых картинок), размер не зависит от мирового size.
        /// Причина: в инвентаре/спеллбуке/курсоре предметы должны быть читаемыми и одинаковыми, а size
        /// (например меч size=5) делил бы иконку в 1/size → микроскопический предмет. Мировой/рука-размер
        /// (он зависит от size) живут отдельно (UpdateController image-path, WeaponMount). icon.localScale=1.
        /// </summary>
        protected void ApplyPrefabImage(string prefab)
        {
            Sprite sprite = AnimationCacheService.GetPrefabSprite(BaseController.GAME_ID, prefab)
                ?? Resources.Load<Sprite>("unknow");

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
                icon.transform.localScale = Vector3.one;   // вариант 2: фикс размер слота, size игнорируется
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

        /// <summary>
        /// Нельзя применить прямо сейчас (нет маны, мёртв, применять не к чему) — иконка гасится.
        /// Считается тут, а не в отрисовке карточки: тот же признак читает слот панели быстрых
        /// действий, а карточка книги живёт лишь пока открыта её вкладка.
        /// </summary>
        public virtual bool IsUnavailable() { return false; }
    }
}
