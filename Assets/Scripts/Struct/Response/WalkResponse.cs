namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class WalkResponse : Response
    {
        public const string GROUP = "move/walk";

        public override string group
        {
            get { return GROUP; }
        }

        public double? x;
        public double? y;
        public double? z;
    }
}
