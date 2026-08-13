using System.Collections.Generic;

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
			public bool visible;
			public bool ellipse;
			public bool point;
			public Point[] polygon;
			public Point[] polyline;
			// Сервер хранит property с indexBy='name' → JSON-объект {name: TileProperty} (как у группы).
			public Dictionary<string, TileProperty> property;
			public string sha256;
		}
}