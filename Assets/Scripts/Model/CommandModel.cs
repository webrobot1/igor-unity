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

    public double ping()
    {
        return (pings.Count>0?Math.Round((pings.Sum() / pings.Count), 3):0);
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

        // и удалим из списка нашу команду
        timeouts[key].requests.Remove(recive.command_id);
    }
}
