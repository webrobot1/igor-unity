/// <summary>
/// Структура полученных данных при авторизации
/// </summary>
[System.Serializable]
public class SiginRecive
{
	public int id;
	public int port;
	public string token;


	public string map;

	public string error = "";
}
