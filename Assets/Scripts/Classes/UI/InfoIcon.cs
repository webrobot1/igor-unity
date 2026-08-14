using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    /// <summary>
    /// Иконка префаба в окне сведений о существе: картинка плюс подсказка по наведению. Сама по себе
    /// не показывается — от неё наследуются иконки того, что окно перечисляет сеткой: умения существа
    /// (<see cref="InfoSpell"/>) и его добыча (<see cref="InfoLoot"/>). Общее у них — картинка из
    /// каталога, показ подсказки и запрет применения; расходятся они лишь текстом подсказки.
    /// </summary>
    public abstract class InfoIcon : MoveableObject, IPointerEnterHandler, IPointerExitHandler
    {
        protected string _prefab;
        private Tooltip _tooltip;

        /// <summary>
        /// Чей это префаб в каталоге. Показ сверяет по нему собранную сетку с нужным составом, чтобы
        /// не пересобирать её каждый кадр.
        /// </summary>
        public string Prefab { get { return _prefab; } }

        /// <summary>
        /// Наполнить иконку. Подсказку передаёт окно: своей ссылки на неё у иконки нет — она рождается
        /// в рантайме, а поле Inspector'а заполнить некому.
        /// </summary>
        public void SetData(string prefab, Tooltip tooltip)
        {
            _prefab = prefab;
            _tooltip = tooltip;

            // Картинки у префаба может не быть вовсе — тогда встанет заглушка "unknow", и сетка
            // сохранит ряд вместо дыры (см. ApplyPrefabImage).
            ApplyPrefabImage(prefab);
        }

        /// <summary>
        /// Название и описание из каталога — то, что известно о любом префабе. Наследник дописывает
        /// своё: во что обходится умение, с каким шансом падает добыча.
        /// </summary>
        public override string GetTooltipText()
        {
            string title = AnimationCacheService.GetPrefabName(_prefab) ?? _prefab;
            string description = AnimationCacheService.GetPrefabDescription(_prefab);

            string text = Tooltip.Title(title);

            if (!string.IsNullOrEmpty(description))
                text += "\n" + Tooltip.Hint(description);

            return text;
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltip != null && !string.IsNullOrEmpty(_prefab))
                _tooltip.Show(transform.position, GetTooltipText());
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        /// <summary>
        /// Применения у этой иконки нет: окно сведений рассказывает о чужом, а своим распоряжаются
        /// книга заклинаний и инвентарь. В курсор иконка не берётся (клика она не слушает), так что
        /// дорога сюда — только ошибка вызывающего, и молчать о ней нельзя.
        /// </summary>
        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            throw new InvalidOperationException(
                GetType().Name + ".Use: префаб " + _prefab + " показан в окне сведений и применению не подлежит");
        }
    }
}
