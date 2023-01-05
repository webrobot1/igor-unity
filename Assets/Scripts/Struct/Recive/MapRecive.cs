using System.Collections.Generic;
/// <summary>
/// —труктура получаемых данных данных
/// </summary>
[System.Serializable]
public class MapRecive
{
    public Dictionary<string, PlayerRecive> players;
    public Dictionary<string, EnemyRecive> enemys;
    public Dictionary<string, ObjectRecive> objects;
}
