using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = "";

        /// <summary>
        /// сколько осталось до таймаута секунд .я не рассылаю точное время тк 100% на клиенте и сервере время не совпадет и будет рассинхрон пару секунд
        /// </summary>
        public double? remain = null;

        public double? timeout = null;

        public object data;
        public DateTime finish = DateTime.Now;
    }
}
