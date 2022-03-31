using System.Collections.Generic;

/// <summary>
/// —труктура полученных данных - карты
/// </summary>
[System.Serializable]
public class Map
{
	public string renderorder;
	public int width;
	public int height;
	public int tilewidth;
	public int tileheight;

	// всегда словарь тк ключ - его ид дл¤ быстрого доступа из сло¤
	public Dictionary<int, Tileset> tileset = new Dictionary<int, Tileset> {};
	public Layer[] layer;
}