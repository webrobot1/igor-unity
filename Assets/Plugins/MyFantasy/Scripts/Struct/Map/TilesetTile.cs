using UnityEngine;

namespace MyFantasy
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class TilesetTile
	{
		public int tile_id;
		public string resource;
		public Sprite sprite;
		public TilesetTileAnimation[] frame;

		public TilesetTile(Sprite sprite)
		{
			this.sprite = sprite;
		}
	}
}