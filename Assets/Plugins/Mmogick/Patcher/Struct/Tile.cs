using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class Tile
	{
		public TileObjectGroup[] group;
		public TileAnimation[] frame;

		// Сервер хранит property с indexBy='name' → JSON-объект {name: TileProperty}.
		public Dictionary<string, TileProperty> property;
	}
}
