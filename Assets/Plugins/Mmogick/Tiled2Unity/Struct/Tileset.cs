using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// Палитра изображений 
	/// </summary>
	[System.Serializable]
	public class Tileset
	{
		public int columns;
		public int tilecount;
		public int tilewidth;
		public int tileheight;


		public int spacing = 0;
		public int margin = 0;
		public string trans;

		public string resource;
		public string tileset_image;
		
		// здесь только нестанданртные tile с зависимыми данными (весь список возможных можно получить зная tilecount)
		public Dictionary<int, TilesetTile> tile = new Dictionary<int, TilesetTile> { };
	}
}