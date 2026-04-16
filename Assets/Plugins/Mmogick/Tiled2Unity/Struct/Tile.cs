namespace Mmogick
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class Tile
	{
		public LayerObject[] objects;
		public TileAnimation[] frame;
	}
}