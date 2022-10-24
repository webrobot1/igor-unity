using System;
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
}
