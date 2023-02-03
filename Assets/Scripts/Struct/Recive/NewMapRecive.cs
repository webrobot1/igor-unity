using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// структура получаемых данных мира определенной  карты
    /// </summary>
    [System.Serializable]
    public class NewMapRecive
    {
        public Dictionary<string, NewPlayerRecive> players;
        public Dictionary<string, NewEnemyRecive> enemys;
        public Dictionary<string, NewObjectRecive> objects;
    }
}
