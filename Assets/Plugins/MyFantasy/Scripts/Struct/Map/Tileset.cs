using System.Collections.Generic;

namespace MyFantasy
{
	/// <summary>
	/// Палитра изображений 
	/// </summary>
	[System.Serializable]
	public class Tileset
	{
		public int tileset_id;
		public int firstgid;
		public int columns;
		public int tilecount;
		public int tilewidth;
		public int tileheight;


		public int spacing;
		public int margin;
		public string trans;

		public string image;

		// здесь только нестанданртные tile с зависимыми данными (весь список возможных можно получить зная tilecount)
		public Dictionary<int, TilesetTile> tile = new Dictionary<int, TilesetTile> { };
	}
}