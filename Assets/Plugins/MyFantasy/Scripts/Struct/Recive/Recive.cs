using System;
using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// —cтруктура получаемых данных данных
    /// </summary>
    [System.Serializable]
    public class Recive<P, E, O> where P : ObjectRecive where E : ObjectRecive where O : ObjectRecive
    {
        public Dictionary<int, MapRecive<P, E, O>> world;

        /// <summary>
        ///  с какой стороны какой номер карты (мы мо этим номерам при пеерходе на другую карту смещаем карты что бы не запрашивать их снова)
        /// </summary>
        public Dictionary<int, string> sides;

        /// <summary>
        ///  временная метка которую выслал клиент и отправленная назад за вычетом времени ожидания этой отправки на сервере (отправляется в потоке с другими данынми когда они будут)
        /// </summary>
        public long unixtime;      
        
        /// <summary>
        ///  Экстраполяция - время в секундах для непрерывных событий которым стоит продлевать анимацию их движения. Спустя remain время события пакет дойдет только с сервера
        /// </summary>
        public double extrapol;
       
        /// <summary>
        /// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
        /// </summary>
        public string error;

        /// <summary>
        /// в большинсве своем это название анимации объекта который (название текущего состояния объекта на сервере, что он делает. ее формат опредяется кодом серверных событий)
        /// </summary>
        public string action;
    }
}