namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class BoltResponse : Response
    {
        public const string GROUP = "fight/bolt";
        public override string group
        {
            get { return GROUP; }
        }

        public double? x;
        public double? y;
        public double? z;

        public string prefab;
        public string target;
    }
}
