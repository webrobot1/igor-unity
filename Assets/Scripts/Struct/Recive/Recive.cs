/// <summary>
/// —труктура получаемых данных данных
/// </summary>

[System.Serializable]
public class Recive
{   
    public PlayerRecive[] players;
    public EnemyRecive[] enemys;
    public ObjectRecive[] objects;
    public MapRecive map;

    public string action;
    public string error;
}
