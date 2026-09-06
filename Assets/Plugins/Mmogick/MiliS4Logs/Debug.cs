
using System;
using System.Globalization;

namespace Mmogick
{
    /// <summary>
    /// Журнал клиента с меткой времени. Класс скрывает движковый <see cref="UnityEngine.Debug"/>
    /// одноимёнными методами, и скрытие срабатывает на КАЖДОМ неквалифицированном вызове в клиенте:
    /// файлы лежат в этом же пространстве имён, а своё пространство компилятор предпочитает импорту.
    /// Обойти его можно только полным именем движкового класса.
    ///
    /// Время у всех трёх строк ОДНО и в одном формате: журнал читают подряд, сопоставляя сообщение с
    /// ошибкой, случившейся следом, — и метки в разных часовых поясах разводили бы соседние строки на
    /// величину смещения. Местное: журнал читает человек, у которого часы этой же машины.
    /// </summary>
    abstract public class Debug : UnityEngine.Debug
    {
        private static string Stamp()
        {
            return DateTime.Now.ToString("[HH:mm:ss:fff]", CultureInfo.InvariantCulture) + " ";
        }

        new public static void Log(object obj)
        {
            UnityEngine.Debug.Log(Stamp() + obj);
        }

        new public static void LogError(object obj)
        {
            UnityEngine.Debug.LogError(Stamp() + obj);
        }

        new public static void LogWarning(object obj)
        {
            UnityEngine.Debug.LogWarning(Stamp() + obj);
        }
    }
}
