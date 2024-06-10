using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class ActionBarsResponse : Response
    {
        public Dictionary<int, ActionBarsRecive> actionbars = new Dictionary<int, ActionBarsRecive>();

        public override string group
        {
            get { return "ui/actionbars"; }
        }   
    }
}
