using System.Collections.Generic;

namespace Mmogick
{
		[System.Serializable]
		public class TileObjectGroup
		{
			public string name;
			public TileObject[] @object;
			// Сервер хранит property с indexBy='name' → JSON-объект {name: TileProperty}.
			public Dictionary<string, TileProperty> property;
		}
}
