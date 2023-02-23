using System;

namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class Response
    {
        /// <summary>
        /// группа событе которое которое мы хотим что бы наш игрок сделал на сервер
        /// </summary>
        public string group;

        /// <summary>
        /// метод события который хотим что бы был вызван. по умолчанию index (удоно если в событии сервера - один метод, что бы не указвать )
        /// </summary>
        public string action = "index";
        

        /// <summary>
        /// нужно для вычисления пинга (временная метка по которой мы поймем сколько прошло времени между отправкой)
        /// </summary>
        public long? unixtime = null;

        /// <summary>
        /// сам пинг (тк клиент подводит итоги пинга сервер не знает пока ему не передать напрямую. можно и подделать но мы на сервере не подвязываемся к пингу, а на клиенте отправляя раньше запросы)
        /// </summary>
        public double? ping = null;
    }
}
