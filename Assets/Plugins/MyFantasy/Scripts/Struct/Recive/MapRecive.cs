using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// структура получаемых данных мира определенной  карты
    /// </summary>
    [System.Serializable]
    public class MapRecive
    {
        public Dictionary<string, PlayerRecive> players;
        public Dictionary<string, EnemyRecive> enemys;
        public Dictionary<string, ObjectRecive> objects;
    }
}
