using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// структура получаемых данных мира определенной  карты
    /// </summary>
    [System.Serializable]
    public class MapRecive<P, E, O> where P : EntityRecive where E : EntityRecive where O : EntityRecive
    {
        public Dictionary<string, P> player;
        public Dictionary<string, E> enemy;
        public Dictionary<string, O> objects;
        public Dictionary<string, E> animal;
    }
}
