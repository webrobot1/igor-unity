/// <summary>
/// Структура полученных данных при авторизации
/// </summary>

[System.Serializable]
public class SiginRecive
{
	public int id;
	public int map_id;
	public string token;

	/// <summary>
	/// чему равен fixedDeltaTime (как часто FixedUpdate() происходит)
	/// </summary>
	public float time;


	public string map;
}
