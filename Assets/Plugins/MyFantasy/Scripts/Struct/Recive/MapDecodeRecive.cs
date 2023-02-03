namespace MyFantasy
{
    /// <summary>
    /// —cтруктура получаемых данных данных
    /// </summary>
    [System.Serializable]
    public class MapDecodeRecive
    {
        public string map = "";

        /// <summary>
        /// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
        /// </summary>
        public string error = "";
    }
}