using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = null;

        /// <summary>
        /// сколько осталось до таймаута секунд .я не рассылаю точное время тк 100% на клиенте и сервере время не совпадет и будет рассинхрон пару секунд. используется для сверки когда отправлять пакет можно и сколько длится должна анимация
        /// </summary>
        public double? remain = null;

        /// <summary>
        /// сколько секуд таймаут события. приходит при загрузке или если в процессе игры изменился на группу события. нужен только для статистики
        /// </summary>
        public double? timeout = null;

        public JObject data;
        public DateTime finish = DateTime.Now;

        public bool? is_client = null;          // флаг что команду отправили мы. если false то над разрешить до таймаута отправлять подобные комнад (если они пуличные) если хотим сбросить
    }
}
