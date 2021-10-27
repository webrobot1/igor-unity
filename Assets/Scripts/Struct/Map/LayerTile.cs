
using UnityEngine;
/// <summary>
/// тайловая карта на слое
/// </summary>
[System.Serializable]
public class LayerTile
{
	public int tile_id;
	public int tileset_id;
	public int horizontal;
	public int vertical;
	public int diagonal;

	public Sprite sprite;
	public int x;
	public int y;

	public LayerTile(Sprite sprite)
	{
		this.sprite = sprite;
	}
}