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

    private string _action;

    // если пришла команда action в сокращенной форме то добавим index
    public string action
    {
        set
        {
            if (value != "" && !value.Contains('/'))
            {
                value = value + "/index";
            }
            _action = value;
        }

        get
        {
            return _action;
        }
    }


    public string error;
}
