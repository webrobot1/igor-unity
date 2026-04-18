using System.Collections.Generic;
using Newtonsoft.Json;

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - слои карты
	/// </summary>
	[System.Serializable]
	public class Layer
	{
		public string name;
		public bool visible = true;
		public float opacity = 1;
		public int isSpawn = 0;

		public float offsetx;
		public float offsety;
		public float offsetz;

		public string resource;

		// Sparse-словарь: ключ — порядковый индекс ячейки на карте (y*width + x),
		// значение — сырая строка вида "sha256" или "sha256:f". Парсится через
		// LayerTileDictionaryConverter в LayerTile с ленивым извлечением полей.
		[JsonConverter(typeof(LayerTileDictionaryConverter))]
		public Dictionary<int, LayerTile> tiles;

		public LayerObject[] objects;
	}
}
