/// <summary>
/// Структура полученных данных при авторизации
/// </summary>
[System.Serializable]
public class SiginRecive
{
	public int id;

	public string host;
	public string token;

	public string error = "";
}
