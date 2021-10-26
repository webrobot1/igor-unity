using System.Collections.Generic;

/// <summary>
/// —труктура полученных данных - слои карты
/// </summary>
[System.Serializable]
public class Layer
{
	public int visible;
	public int opacity;

	public float offsetx;
	public float offsety;

	public string resource;

	public LayerTile[] tiles;
	public Dictionary<int, LayerObject> objects;
}