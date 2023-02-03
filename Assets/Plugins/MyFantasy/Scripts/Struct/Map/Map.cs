using System.Collections.Generic;

namespace MyFantasy
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[System.Serializable]
	public class Map
	{
		public int map_id;

		public string renderorder;
		public int width;
		public int height;
		public int tilewidth;
		public int tileheight;

		public int? spawn_sort = null;

		// всегда словарь тк ключ - его ид для быстрого доступа из слоям
		public Dictionary<int, Tileset> tileset = new Dictionary<int, Tileset> {};
		public Layer[] layer;
	}
}