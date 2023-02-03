using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// структура получаемых данных мира определенной  карты
    /// </summary>
    [System.Serializable]
    public class NewMapRecive : MapRecive
    {
        public new Dictionary<string, NewPlayerRecive> players;
        public new Dictionary<string, NewEnemyRecive> enemys;
        public new Dictionary<string, NewObjectRecive> objects;
    }
}
