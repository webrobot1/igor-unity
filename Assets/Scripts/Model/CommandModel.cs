using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Модель отвечает за расчет пингов 
/// </summary>

public class CommandModel
{
    public Dictionary<long, long> requests = new Dictionary<long, long>();

    public float work_time = 0;
    public float timeout = 0;
    public DateTime time = DateTime.Now;     // время последнего посыла команды         
    public float ping = 0;

    public float wait_time = 0;

    /// <summary>
    /// проверка в массиве запросов какой отработал (все что ДО него - удалим)
    /// </summary>
    public void check(PingsRecive recive)
    {
        this.wait_time = recive.wait_time;
        this.work_time = recive.work_time;

        this.ping = (float)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - recive.command_id) / 1000 - wait_time;

 
/*        foreach (KeyValuePair<long, long> request in requests)
        {
            if (request.Key < recive.command_id) 
                requests.Remove(request.Key);
            else
                break;
        }*/
    }
}
