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
        public Dictionary<string, MapRecive<P, E, O>> world;
        public Dictionary<string, int> sides;


        public List<PingRecive> pings;

        /// <summary>
        /// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
        /// </summary>
        public string error = "";
        private string _action = "";

        // если пришла команда action в сокращенной форме то добавим index
        public string action
        {
            set
            {
                if (value != "" && !value.Contains('/'))
                {
                    value = value + "/index";
                }
                _action = value;
            }

            get
            {
                return _action;
            }
        }
    }
}