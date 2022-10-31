using System;
using UnityEngine;
/// <summary>
/// Структура отправляемых данных
/// </summary>
public class Response
{                 
    private string _action;
    public string action
    {
        set {
            if (!value.Contains('/'))
            {
                value = value + "/index";
            }
            _action = value; 
        }        
        
        get {
            return _action;
        }
    }

    /// <summary>
    /// нужно для вычисления пинга (временная метка по которой мы поймем сколько прошло времени между отправкой)
    /// </summary>
    [SerializeField]
    private long time
    {
        set {
        }        
        get {
            return (new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds();
        }
    }
}
