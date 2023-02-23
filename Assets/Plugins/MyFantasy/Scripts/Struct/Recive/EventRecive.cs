using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = null;

        /// <summary>
        /// сколько осталось до таймаута секунд .€ не рассылаю точное врем€ тк 100% на клиенте и сервере врем€ не совпадет и будет рассинхрон пару секунд
        /// </summary>
        public float? remain = null;

        /// <summary>
        /// сколько секуд таймаут событи€. приходит при загрузке или если в процессе игры изменилс€ на группу событи€
        /// </summary>
        public float? timeout = null;

        public object data;
        public DateTime finish = DateTime.Now;
    }
}
