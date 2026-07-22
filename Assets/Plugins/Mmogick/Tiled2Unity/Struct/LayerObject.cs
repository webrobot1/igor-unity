namespace Mmogick
{
	/// <summary>
	/// Объекты на слое (полигоны, текст, картинки).
	/// Если объект — тайл-объект, поле tile содержит sha256 привязанного тайла,
	/// flip-флаги передаются отдельными bool-полями.
	/// </summary>
	[System.Serializable]
	public class LayerObject
	{
		public string tile;

		public bool flipH;
		public bool flipV;
		public bool flipD;
		public bool rotHex120;

		public string tileset_image;

		// Класс объекта Tiled (warp / spawn / particle_effect …). Задаёт цвет debug-рамки в DebugObjects.
		public string type;

		public string name = "";

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
