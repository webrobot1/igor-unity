using UnityEngine;

namespace MyFantasy
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class TilesetTileAnimation
	{
		public int tileid;
		public int duration;
		public Sprite sprite;
	}
}