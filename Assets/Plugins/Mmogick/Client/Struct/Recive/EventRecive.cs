using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Mmogick
{
    public class EventRecive
    {
        public string action = null;

        /// <summary>
        /// сколько осталось до таймаута секунд .я не рассылаю точное время тк 100% на клиенте и сервере время не совпадет и будет рассинхрон пару секунд. используется для сверки когда отправлять пакет можно 
        /// </summary>
        public double? remain = null;

        /// <summary>
        /// сколько секуд таймаут события. приходит при загрузке или если в процессе игры изменился на группу события. мы не отнимаем ping (если непрерывное событие это время понадобится для отправки серверу нового пакета) так что используем для анимаций
        /// </summary>
        public double? timeout = null;

        public JObject data;

        /// <summary>
        /// Флаг что команду отправили мы (приходит от сервера, клиент не отправляет):
        /// true  — команду отправил клиент, ждём remain
        /// false — событие создал сервер, клиент может отправить публичную команду до таймаута чтобы сбросить
        /// null  — события нет (сервер не включает from_client при action="")
        /// </summary>
        public bool? from_client = null;
    }
}
