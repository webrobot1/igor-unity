using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class TilesetTile
	{
		public string resource;
		public Sprite sprite;
		public Point[] polygon;
				
		public TilesetTileAnimation[] frame;
		public TilesetTile(Sprite sprite)
		{
			this.sprite = sprite;
		}
	}
}