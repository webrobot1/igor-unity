/// <summary>
/// Структура полученных данных при авторизации
/// </summary>
[System.Serializable]
public class SigninRecive
{
	public string host;

	public string key;
	public string token;

	public string error = "";
}
