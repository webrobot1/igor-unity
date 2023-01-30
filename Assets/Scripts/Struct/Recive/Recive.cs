using System.Collections.Generic;


/// <summary>
/// —cтруктура получаемых данных данных
/// </summary>
[System.Serializable]
public class Recive
{
    public Dictionary<string, MapRecive> world;
    public Dictionary<string, int> sides;

    /// <summary>
    /// список таймаутов (высылается при load)
    /// </summary>
    public Dictionary<string, TimeoutRecive> timeouts = new Dictionary<string, TimeoutRecive>();

    /// <summary>
    /// список отработанных комманд
    /// </summary>
    public Dictionary<string, CommandRecive> commands = new Dictionary<string, CommandRecive>();

    /// <summary>
    /// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
    /// </summary>
    public string error = "";

    /// <summary>
    /// если мы переходим на другую карту тут будет новый хост для соединения
    /// </summary>
    public string new_host;   
    
    private string _action = "";

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
}