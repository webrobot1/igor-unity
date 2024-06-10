namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class AttackResponse : Response
    {
        public const string GROUP = "fight/attack";

        public override string group
        {
            get { return GROUP; }
        }

        public double? x;
        public double? y;
        public double? z;


        public string target;
        public string magic;
    }
}
