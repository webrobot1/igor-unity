using ICSharpCode.SharpZipLib.BZip2;
using System.IO;
using System.Text;
using UnityEngine;

public class MapModel 
{
    private static MapModel instance;

    private MapModel()
    { }

    public static MapModel getInstance()
    {
        if (instance == null)
            instance = new MapModel();
        return instance;
    }

    /// <summary>
	/// декодирование из Bzip2 в объект MapRecive
	/// </summary>
    public MapRecive decode(string base64)
    {
        using (MemoryStream source = new MemoryStream(System.Convert.FromBase64String(base64)))
        {
            using (MemoryStream target = new MemoryStream())
            {
                BZip2.Decompress(source, target, true);
                return JsonUtility.FromJson<MapRecive>(Encoding.UTF8.GetString(target.ToArray()));
            }
        }
    }
}