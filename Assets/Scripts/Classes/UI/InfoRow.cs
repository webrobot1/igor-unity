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

        /// <summary>Подпись, уходящая в подсказку иконки. null — показывать подсказкой нечего.</summary>
        public readonly string Hint;

        /// <summary>Строка сама себе фраза: подписи у неё нет, и заменять иконке нечего.</summary>
        public InfoRow(string text) : this(text, text, null, null) { }

        public InfoRow(string text, string value, string icon, string hint)
        {
            Text = text;
            Value = value;
            Icon = icon;
            Hint = hint;
        }

        /// <summary>Та же ли это строка: пункты списка пересобираются только при смене набора.</summary>
        public bool Equals(InfoRow other)
        {
            return other != null && Text == other.Text && Value == other.Value
                && Icon == other.Icon && Hint == other.Hint;
        }
    }
}
