namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    abstract public class Response
    {
        public const string ACTION_INDEX = "index";

        /// <summary>
        /// значение action события. По умолчанию index; уходит на сервер ВСЕГДА — сервер требует явный
        /// action (иначе вложенную группу событий не отличить от пары «группа/событие»).
        /// </summary>
        private string _action = ACTION_INDEX;

        /// <summary>
        /// группа события которое которое мы хотим что бы наш игрок сделал на сервер
        /// </summary>
        public abstract string group
        {
            get;
        }

        /// <summary>
        /// метод события (сегмент после группы), который хотим вызвать. По умолчанию index, но уходит на сервер
        /// всегда — команды вида take/put/open/to/follow переопределяют его через сеттер.
        /// </summary>
        public string action
        {
            get { return _action; }
            set { _action = value; }
        }
              

        /// <summary>
        /// нужно для вычисления пинга (временная метка по которой мы поймем сколько прошло времени между отправкой)
        /// </summary>
        public long? unixtime = null;

        /// <summary>
        /// сам пинг (тк клиент подводит итоги пинга сервер не знает пока ему не передать напрямую. можно и подделать но мы на сервере не подвязываемся к пингу, а на клиенте отправляя раньше запросы)
        /// </summary>
        public double? ping = null;

        /// <summary>Отправить серверу; исход — как у <see cref="ConnectController.Send"/>.</summary>
        public bool Send()
        {
            return ConnectController.Send(this);
        }
    }
}
