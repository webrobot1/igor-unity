using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Форма системного указателя: раскрытая ладонь — «это можно взять либо потянуть», сжатая — «держу».
    /// Держатель один на всю игру: вид указателя — общее состояние, и разложенный по окнам он расходится
    /// молча — одно окно ставит ладонь, соседнее её тут же снимает. Кто и когда его меняет, решает
    /// <see cref="CursorController"/>; исключение — перетаскивание карты мира, оно держит сжатую ладонь
    /// само (<see cref="WorldMapDrag"/>).
    /// </summary>
    public static class HandCursor
    {
        public enum Shape
        {
            /// <summary>Обычная стрелка системы.</summary>
            Default,

            /// <summary>Раскрытая ладонь: под указателем то, что берётся либо тянется.</summary>
            Open,

            /// <summary>Сжатая ладонь: держим и тянем.</summary>
            Closed
        }

        /// <summary>
        /// Точка картинки, которой указатель «попадает» в экран. Значения — из самих курсоров системной
        /// темы, откуда взяты картинки: у раскрытой и сжатой ладони они разные, и при подмене картинки
        /// без подмены точки рука прыгала бы в момент захвата.
        /// </summary>
        private static readonly Vector2 HOTSPOT_OPEN = new Vector2(11, 2);

        private static readonly Vector2 HOTSPOT_CLOSED = new Vector2(9, 5);

        private static Texture2D open;

        private static Texture2D closed;

        /// <summary>
        /// Что стоит сейчас. Системе форму отдаём только на смене: указатель решается каждый кадр, а
        /// пересборка курсора на каждом кадре — работа впустую.
        /// </summary>
        private static Shape current = Shape.Default;

#if UNITY_EDITOR
        /// <summary>
        /// Картинка раскрытой ладони и её точка попадания — ими оснастка съёмки роликов рисует указатель
        /// прямо в кадре: системный курсор рисует система поверх картинки окна, и в запись он не попадает.
        /// Берётся отсюда, а не своей копией у оснастки: указатель на снятом ролике обязан выглядеть ровно
        /// тем же, что видит игрок.
        /// </summary>
        public static Texture2D OpenTexture
        {
            get { return Load(ref open, "Cursors/hand"); }
        }

        public static Vector2 OpenHotspot
        {
            get { return HOTSPOT_OPEN; }
        }
#endif

        public static void Set(Shape shape)
        {
            if (shape == current)
                return;

            switch (shape)
            {
                case Shape.Open:
                    Cursor.SetCursor(Load(ref open, "Cursors/hand"), HOTSPOT_OPEN, CursorMode.Auto);
                    break;

                case Shape.Closed:
                    Cursor.SetCursor(Load(ref closed, "Cursors/hand_drag"), HOTSPOT_CLOSED, CursorMode.Auto);
                    break;

                default:
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    break;
            }

            current = shape;
        }

        /// <summary>
        /// Картинки лежат в Resources, а не ссылкой в сцене: это ассеты курсора, а не объекты интерфейса,
        /// и назначать их руками в каждом окне-потребителе незачем. Статика переживает остановку игры, а
        /// загруженный ассет — нет, поэтому живость проверяем на каждой выдаче.
        /// </summary>
        private static Texture2D Load(ref Texture2D cache, string path)
        {
            if (cache == null)
            {
                cache = Resources.Load<Texture2D>(path);

                if (cache == null)
                    BaseController.Error("не найдена картинка указателя Resources/" + path);
            }

            return cache;
        }
    }
}
