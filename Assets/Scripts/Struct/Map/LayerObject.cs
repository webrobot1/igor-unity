/// <summary>
/// Объекты слоя (полигоны, текс, картинки)
/// </summary>

[System.Serializable]
public class LayerObject
{
	public int tile_id;
	public int tileset_id;

	public string name;

	public int horizontal;
	public int vertical;

	public float x;
	public float y;	
	
	public float width;
	public float height;

	public float rotation;
	public int visible = 1;

	public int ellipse;
	public string polygon;
	public string polyline;
	public string text;
}