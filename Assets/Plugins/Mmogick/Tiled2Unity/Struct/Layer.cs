using System.Collections.Generic;

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

		public float offsetx;
		public float offsety;
		public float offsetz;

		public string resource;

		// Sparse-словарь: ключ — порядковый индекс ячейки на карте (y*width + x),
		// значение — LayerTile с tile (sha256) и flip-флагами (отсутствующее поле = false).
		public Dictionary<int, LayerTile> tile;

		public LayerObject[] @object;

		public Dictionary<string, LayerProperty> property;
	}
}
