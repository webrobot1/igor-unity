namespace Mmogick
{
	/// <summary>
	/// объекты на слое (полигоны, текс, картинки)
	/// </summary>

	[System.Serializable]
	public class LayerObject
	{
		public string tile_id;
		public string tileset_image;

		public string name = "";

		public int horizontal;
		public int vertical;

		public float x;
		public float y;	
	
		public float width;
		public float height;

		public float rotation;
		public bool visible = true;

		public bool ellipse;
		
		public Point[] polygon;
		public Point[] polyline;
		
		public string text;
	}
}