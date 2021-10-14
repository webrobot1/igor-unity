/// <summary>
/// Структура полученных данных - объект
/// </summary>

[System.Serializable]
public abstract class ObjectRecive
{
	public int id;
	public int map_id;
	public float[] position;
	public string prefab;

	public string action;
}