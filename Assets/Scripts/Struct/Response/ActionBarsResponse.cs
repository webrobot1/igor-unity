using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    /// <summary>
    /// Структура отправляемых данных
    /// </summary>
    public class ActionBarsResponse : Response
    {
        public const string GROUP = "ui/actionbars";

        public Dictionary<int, ActionBarsRecive?> actionbars = new Dictionary<int, ActionBarsRecive?>();

        public override string group
        {
            get { return GROUP; }
        }
    }
}
