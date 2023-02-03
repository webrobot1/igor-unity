namespace MyFantasy
{
    /// <summary>
    /// Структура ответа с командой на которые среагировал сервер и вернул время сколько она крутилась на севрере (это не время работы)
    /// </summary>
    /// 
    public class CommandRecive
    {
        public long command_id;
        public float wait_time;                                  
    }
}
