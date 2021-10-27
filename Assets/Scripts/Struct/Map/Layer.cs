using System.Collections.Generic;

/// <summary>
/// —труктура полученных данных - слои карты
/// </summary>
[System.Serializable]
public class Layer
{
	public int layer_id;
	public string name;
	public int visible = 1;
	public int opacity = 1;

	public float offsetx;
	public float offsety;

	public string resource;

	public Dictionary<int, LayerTile> tiles = new Dictionary<int, LayerTile> { } ;
	public Dictionary<int, LayerObject> objects = new Dictionary<int, LayerObject> { } ;
}