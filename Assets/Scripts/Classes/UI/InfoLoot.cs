using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Иконка добычи в окне сведений: одна вещь — одна иконка. Достаётся вещь всегда розыгрышем, разница
    /// в шансе: сто процентов значит «лежит наверняка», меньше — бывает, а бывает и нет. Двух строк об
    /// одной вещи не бывает — что достаётся с цели, задано таблицей, где ключ и есть сама вещь.
    /// </summary>
    public class InfoLoot : InfoIcon
    {
        /// <summary>Шанс выпадения долей от единицы. Единица — вещь достаётся наверняка.</summary>
        private float _chance;

        private int _min;
        private int _max;

        /// <summary>
        /// Что достанется по этой вещи: шанс и границы количества. Шанс сервер хранит в сотых долях
        /// процента, приводит его к доле вызывающий: здесь он уже готов к показу.
        /// </summary>
        public void SetLoot(string prefab, Tooltip tooltip, float chance, int min, int max)
        {
            SetData(prefab, tooltip);
            _chance = chance;
            _min = min;
            _max = max;
        }

        public override string GetTooltipText()
        {
            string text = base.GetTooltipText();

            // Стопроцентный шанс не подписываем: сказать «выпадет с шансом 100%» — то же самое, что
            // промолчать, но читается как будто бывает и иначе.
            if (_chance > 0 && _chance < 1f)
                text += "\n" + TextStyle.Value("Шанс: " + Share(_chance));

            text += "\n" + TextStyle.Value(_min == _max
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
