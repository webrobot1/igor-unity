using System;

namespace Mmogick
{
    /// <summary>
    /// Готовая строка блока окна сведений — то, что показу остаётся лишь положить в пункт списка.
    /// Собирается там же, где разрешается значение: оформление строки (подпись, приглушение видового)
    /// — часть её сборки, а не показа, и второго места, где решают, как строка выглядит, быть не должно.
    ///
    /// Форм у строки две, и выбирает между ними сам пункт. Есть у компонента иконка — она встаёт вместо
    /// подписи, рядом с ней идёт одно <see cref="Value"/>, а подпись читается подсказкой
    /// (<see cref="Hint"/>). Иконки нет — строка встаёт целиком (<see cref="Text"/>), как вставала
    /// раньше. Обе формы готовятся заранее: картинка бывает битой, и тогда пункт обязан вернуться к
    /// тексту, а собирать его на месте нечем — правило сборки живёт не там.
    /// </summary>
    public class InfoRow : IEquatable<InfoRow>
    {
        /// <summary>Строка целиком: подпись и значение. Так она встаёт без иконки.</summary>
        public readonly string Text;

        /// <summary>Одно значение, без подписи. Так строка встаёт рядом с иконкой.</summary>
        public readonly string Value;

        /// <summary>Slug компонента, чья иконка заменяет подпись. null — иконки у строки не бывает.</summary>
        public readonly string Icon;

        /// <summary>
        /// Подпись величины, уходящая в подсказку пункта, где значок заменил её собой, а значение видно
        /// рядом. null — подписи у строки нет: подсказке достаётся вся фраза.
        /// </summary>
        public readonly string Caption;

        /// <summary>
        /// Описание свойства из справочника — оно объясняет, что величина значит. Идёт в подсказку под
        /// тем, что пункт не показал сам. null — описания нет либо строка не про сам компонент.
        /// </summary>
        public readonly string Description;

        /// <summary>
        /// Строка о ПРИБЫЛИ запаса: её значок заимствован у самого запаса (сердце, жемчужина) и говорит
        /// лишь, чего прибывает, — что речь о росте, называет стрелка поверх значка. У строки о самом
        /// свойстве значок свой, и стрелки на нём не бывает.
        /// </summary>
        public readonly bool Gain;

        /// <summary>Строка сама себе фраза: подписи у неё нет, и заменять иконке нечего.</summary>
        public InfoRow(string text) : this(text, text, null, null, null) { }

        public InfoRow(string text, string value, string icon, string caption, string description, bool gain = false)
        {
            Text = text;
            Value = value;
            Icon = icon;
            Caption = caption;
            Description = description;
            Gain = gain;
        }

        /// <summary>Та же ли это строка: пункты списка пересобираются только при смене набора.</summary>
        public bool Equals(InfoRow other)
        {
            return other != null && Text == other.Text && Value == other.Value && Icon == other.Icon
                && Caption == other.Caption && Description == other.Description && Gain == other.Gain;
        }
    }
}
