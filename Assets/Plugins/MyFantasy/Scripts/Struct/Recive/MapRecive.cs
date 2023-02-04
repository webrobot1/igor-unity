using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// структура получаемых данных мира определенной  карты
    /// </summary>
    [System.Serializable]
    public class MapRecive<P, E, O> where P : ObjectRecive where E : ObjectRecive where O : ObjectRecive
    {
        public Dictionary<string, P> players;
        public Dictionary<string, E> enemys;
        public Dictionary<string, O> objects;
    }
}
