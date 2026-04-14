namespace Mmogick
{
	/// <summary>
	/// тайловая карта на слое
	/// </summary>
	[System.Serializable]
	public class LayerTile
	{
		public string tile_id;
		public string tileset_image;
		public int horizontal = 0;
		public int vertical = 0;
		public int diagonal = 0;
		public int rotated_hex120 = 0;

		public int x;
		public int y;
	}
}