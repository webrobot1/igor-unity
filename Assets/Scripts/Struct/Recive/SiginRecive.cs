/// <summary>
/// Структура полученных данных при авторизации
/// </summary>

[System.Serializable]
public class SiginRecive
{
	public int id;
	public string token;

	/// <summary>
	/// количество пикселей на 1 unit (1 целая позиция координаты)
	/// </summary>
	public float pixels;

	/// <summary>
	/// чему равен fixedDeltaTime (как часто FixedUpdate() происходит)
	/// </summary>
	public float time;
}
