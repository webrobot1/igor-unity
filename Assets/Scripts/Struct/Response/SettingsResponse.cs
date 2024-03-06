using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class SettingsResponse : Response
    {
        public Dictionary<string, string> settings;

        public override string group
        {
            get { return "system/settings"; }
        }   
    }
}
