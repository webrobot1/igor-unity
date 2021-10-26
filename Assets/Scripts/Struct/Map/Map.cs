using System.Collections.Generic;

/// <summary>
/// Структура полученных данных - карты
/// </summary>
[System.Serializable]
public class Map
{
	public string renderorder;
	public int width;
	public int height;
	public int tilewidth;
	public int tileheight;


	public Dictionary<int, Tileset> tileset;
	public Dictionary<int, Layer> layer;

}