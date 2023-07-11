namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class LoadResponse : Response
    {
        public const string GROUP = "load";
        public override string group
        {
            get { return GROUP; }
        }
    }
}
