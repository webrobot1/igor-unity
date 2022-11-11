using System;
using System.Collections.Generic;
/// <summary>
/// Структура ответа с командой на которые среагировал сервер и вернул время сколько она крутилась на севрере (это не время работы)
/// </summary>
/// 
public class TimeoutRecive
{
    public float timeout = 0;

    /// <summary>
    /// unixtimestamp когда завершится команда
    /// </summary>
    public long finish;

    /// <summary>
    /// когда последний раз запускалось
    /// </summary>
    public DateTime? time = null;

    /// <summary>
    /// список доступных на сервере событий со значением - доступна ли она по прямому вызову
    /// </summary>
    public Dictionary<string, bool> actions = new Dictionary<string, bool>();

    /// <summary>
    /// список отосланных номеров команд (с сервера не приходят)
    /// </summary>
    public List<long> requests = new List<long>();
}
