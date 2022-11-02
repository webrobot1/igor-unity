/// <summary>
/// Структура полученных данных при авторизации
/// </summary>
[System.Serializable]
public class SiginRecive
{
	public int id;
	public int port;
	public string token;

	/// <summary>
	/// чему равен fixedDeltaTime (как часто FixedUpdate() происходит)
	/// </summary>
	public float time;
	public string map;

	public string error = "";
}
