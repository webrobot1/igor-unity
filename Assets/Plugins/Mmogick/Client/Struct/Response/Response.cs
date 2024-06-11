using System;
using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    abstract public class Response
    {
        /// <summary>
        /// значение action события (index по умолчанию)
        /// </summary>
        private string _action = null;

        /// <summary>
        /// группа события которое которое мы хотим что бы наш игрок сделал на сервер
        /// </summary>
        public abstract string group
        {
            get;
        }

        /// <summary>
        /// метод события который хотим что бы был вызван. по умолчанию index (удоно если в событии сервера - один метод, что бы не указвать )
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

        public void Send()
        {
            ConnectController.Send(this);
        }
    }
}
