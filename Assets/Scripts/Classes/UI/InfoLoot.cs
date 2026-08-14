using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Иконка добычи в окне сведений: что с существа падает либо что лежит в контейнере. Два случая
    /// показываются одной сеткой, но рассказывают о разном, и подсказка это различает: у существа
    /// добыча разыгрывается — есть шанс и разброс количества; в контейнере она задана — количество
    /// известно точно.
    /// </summary>
    public class InfoLoot : InfoIcon
    {
        /// <summary>Шанс выпадения долей от единицы. Ноль — выпадение не разыгрывается (контейнер).</summary>
        private float _chance;

        private int _min;
        private int _max;

        /// <summary>
        /// Разыгрываемая добыча существа: шанс и границы количества. Шанс сервер хранит в сотых долях
        /// процента — приводит его к доле вызывающий, здесь он уже готов к показу.
        /// </summary>
        public void SetLoot(string prefab, Tooltip tooltip, float chance, int min, int max)
        {
            SetData(prefab, tooltip);
            _chance = chance;
            _min = min;
            _max = max;
        }

        /// <summary>Заданное содержимое контейнера: количество известно, разыгрывать нечего.</summary>
        public void SetContent(string prefab, Tooltip tooltip, int count)
        {
            SetLoot(prefab, tooltip, 0f, count, count);
        }

        public override string GetTooltipText()
        {
            string text = base.GetTooltipText();

            if (_chance > 0)
                text += "\n" + Tooltip.Value("Шанс: " + Share(_chance));

            text += "\n" + Tooltip.Value(_min == _max
                ? "Количество: " + _min
                : "Количество: " + _min + "–" + _max);

            return text;
        }

        /// <summary>
        /// Доля процентами. Мелкие шансы не округляем до нуля — «0 %» читалось бы как «не падает
        /// вовсе», хотя падает, просто редко.
        /// </summary>
        private static string Share(float value)
        {
            float percent = value * 100f;
            return percent >= 1f ? Mathf.RoundToInt(percent) + "%" : percent.ToString("0.##") + "%";
        }
    }
}
