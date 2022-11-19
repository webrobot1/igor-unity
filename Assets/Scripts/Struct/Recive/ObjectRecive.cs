using System.Collections.Generic;

/// <summary>
/// Структура полученных данных - объект
/// </summary>
[System.Serializable]
public class ObjectRecive
{
	public int id;
	public string key;

	public float? x;
	public float? y;
	public float? z;

	public int map_id;
	public string prefab;

	public string action;
	public int? sort = null;

	public ComponentsRecive components;
	public ParametersRecive parameters;
}