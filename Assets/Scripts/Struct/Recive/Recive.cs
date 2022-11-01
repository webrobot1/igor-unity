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

    /// <summary>
    /// список таймаутов (высылается при load)
    /// </summary>
    public Dictionary<string, TimeoutRecive> timeouts = new Dictionary<string, TimeoutRecive>();

    /// <summary>
    /// список отработанных комманд
    /// </summary>
    public Dictionary<string, CommandRecive> commands = new Dictionary<string, CommandRecive>();

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


    public string error = "";
}
