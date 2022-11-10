
using System;
using System.Collections.Generic;
/// <summary>
/// Структура полученных данных - объект
/// </summary>
[System.Serializable]
public class ObjectRecive
{
	public int id;
	public string key;

	public DateTime created;
	public PositionRecive position;

	public int map_id;
	public string prefab;

	public string action;
	public int? sort = null;
}