using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class TileAnimation
	{
		public string tileid;
		public int duration;
		public Sprite sprite;
	}
}