
using System;
using System.Globalization;

namespace MyFantasy
{
    abstract public class Debug : UnityEngine.Debug
    {
        new public static void Log(object obj)
        {
            UnityEngine.Debug.Log(DateTime.Now.ToString("[HH:mm:ss:fff]", CultureInfo.InvariantCulture) + " " + obj);
        }      
        
        new public static void LogError(object obj)
        {
            UnityEngine.Debug.LogError(DateTime.UtcNow.ToString("[HH:mm:ss:fff]", CultureInfo.InvariantCulture) + " " + obj);
        }     
        
        new public static void LogWarning(object obj)
        {
            UnityEngine.Debug.LogWarning(DateTime.UtcNow.ToString("[HH:mm:ss:fff]") + " " + obj);
        }
    }
}
