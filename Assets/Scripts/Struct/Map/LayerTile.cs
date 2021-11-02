/// <summary>
/// тайловая карта на слое
/// </summary>
[System.Serializable]
public class LayerTile
{
	public int tile_id;
	public int tileset_id;
	public int horizontal = 0;
	public int vertical = 0;
	public int diagonal;

	public int x;
	public int y;
}