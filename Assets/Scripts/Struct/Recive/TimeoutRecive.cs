using System;
using System.Collections.Generic;
/// <summary>
/// Структура ответа с командой на которые среагировал сервер и вернул время сколько она крутилась на севрере (это не время работы)
/// </summary>
/// 
public class TimeoutRecive
{
    public float timeout = 0;
    public DateTime? time = null;

    public Dictionary<long, long> requests = new Dictionary<long, long>();
}
