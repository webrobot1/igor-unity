using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Mmogick
{
    public class Event:EventRecive
    {
        /// <summary>
        /// орентировочное время завершения события с учетом пинга. оно высчитывается как оставшееся время что передал нам сервер за вычетом половины пинга (времени которое пакет доставлялся нам)
        /// </summary>
        public DateTime? finish = null;
    }
}
