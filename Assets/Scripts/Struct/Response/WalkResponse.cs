namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class WalkResponse : Response
    {
        public const string GROUP = "move/walk";

        /// <summary>Движение к точке (x, y, z); действие по умолчанию — шаг в направлении.</summary>
        public const string ACTION_TO = "to";

        public override string group
        {
            get { return GROUP; }
        }

        public double? x;
        public double? y;
        public double? z;
    }
}
