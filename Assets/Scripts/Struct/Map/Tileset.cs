using System.Collections.Generic;

/// <summary>
/// Палитра изображений 
/// </summary>
[System.Serializable]
public class Tileset
{
	public string resource;
	public int firstgid;
	public int columns;
	public int tilecount;
	public int tilewidth;
	public int tileheight;


	public int spacing;
	public int margin;
	public string trans;


	public Dictionary <int, TilesetTile> tile;
}