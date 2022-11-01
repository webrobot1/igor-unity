using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Модель отвечает за расчет пингов 
/// </summary>

public class CommandModel
{
    public List<float> pings = new List<float>();
    public Dictionary<string, TimeoutRecive> timeouts = new Dictionary<string, TimeoutRecive>();


    public float ping()
    {
        return pings.Sum() / pings.Count;
    }

    /// <summary>
    /// проверка в массиве запросов какой отработал (все что ДО него - удалим)
    /// </summary>
    public void check(string key, CommandRecive recive)
    {
        if (!timeouts.ContainsKey(key))
        {
            new Exception("Ответ с командами на несуществующую группу " + key);
        }

        pings.Add((float)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - recive.command_id) / 1000 - recive.wait_time);


        /*    
                foreach (KeyValuePair<long, long> request in timeouts[key])
                {
                    if (request.Key < recive.command_id) 
                        requests.Remove(request.Key);
                    else
                        break;
                }
        */
    }
}
