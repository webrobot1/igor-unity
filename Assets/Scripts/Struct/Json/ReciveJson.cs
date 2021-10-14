/// <summary>
/// —труктура получаемых данных данных
/// </summary>

[System.Serializable]
public class ReciveJson
{   
    public PlayerJson[] players;
    public EnemyJson[] enemys;
    public ObjectJson[] objects;
    public MapJson map;

    public string action;
    public string error;
}
