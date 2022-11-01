/// <summary>
/// Структура ответа с командами на которые среагировал сервер и вернул время сколько она крутилась на севрере (это не время работы)
/// </summary>
/// 
public class PingsRecive
{
    public long command_id;
    public float wait_time = 0;             
    public float work_time = 0;                       
}
