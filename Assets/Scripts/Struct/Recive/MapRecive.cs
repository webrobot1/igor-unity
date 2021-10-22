/// <summary>
/// Структура полученных данных - карты
/// </summary>

[System.Serializable]
public class MapRecive
{
	public string resource;

	public float x;
	public float y;

	public MapRecive[] objects;
}