using System.Collections.Generic;
/// <summary>
/// —труктура получаемых данных данных
/// </summary>
[System.Serializable]
public class Recive
{   
    public PlayerRecive[] players;
    public EnemyRecive[] enemys;
    public ObjectRecive[] objects;
    public Dictionary<string, PingsRecive> pings = new Dictionary<string, PingsRecive>();

    public string action;
    public string error;
}
