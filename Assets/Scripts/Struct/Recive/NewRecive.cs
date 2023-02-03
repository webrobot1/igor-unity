using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// —cтруктура получаемых данных данных
    /// </summary>
    [System.Serializable]
    public class NewRecive : Recive
    {
        public Dictionary<string, NewMapRecive> world;
    }
}