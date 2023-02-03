using System;

namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class Response
    {
        private string _action = "";
        public string action
        {
            set {
                if (!value.Contains('/'))
                {
                    value = value + "/index";
                }
                _action = value;
            }

            get {
                return _action;
            }
        }

        /// <summary>
        /// нужно для вычисления пинга (временная метка по которой мы поймем сколько прошло времени между отправкой)
        /// </summary>
        public long command_id;

        public double? ping = null;

        public string group()
        {
            return action.Split("/")[0];
        }

        public string method()
        {
            return action.Split("/")[1];
        }
    }
}
