using System;
using System.Collections.Generic;
/// <summary>
/// Структура ответа с командой на которые среагировал сервер и вернул время сколько она крутилась на севрере (это не время работы)
/// </summary>
/// 
public class TimeoutRecive
{
    public float timeout = 0;
    public List<long> requests = new List<long>();
}
