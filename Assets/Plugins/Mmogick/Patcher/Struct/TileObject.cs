namespace Mmogick
{
        [System.Serializable]
		public class TileObject
		{
			public string name;
			public string type;
			public float x;
			public float y;
			public float width;
			public float height;
			public float rotation;
			public bool ellipse;
			public bool point;
			public Point[] polygon;
			public Point[] polyline;
			public string sha256;
		}
}