using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[System.Serializable]
	public class Map
	{
		public int map_id;

		public string renderorder;
		public int width;
		public int height;
		public int tilewidth;
		public int tileheight;

		public int? spawn_sort = null;

		// Свойства карты (terrain.json). Известное: spawn = имя слоя-земли (граница спавна игроков),
		// один на карту. Резолв имени в spawn_sort (индекс слоя) — MapDecodeModel.generate.
		public Dictionary<string, LayerProperty> property;

		public Dictionary<int, Layer> layer = new Dictionary<int, Layer> {};

		// Непроходимые области по z-слоям ПРЯМОУГОЛЬНИКАМИ [x, y, ширина, высота]: x/y — левая ВЕРХНЯЯ клетка
		// (y внутри карты <= 0), прямоугольник простирается вправо и вниз, то есть y убывает. Поклеточный
		// словарь тех же преград на типовой карте в десятки раз тяжелее — терраин им бы распух. Разворот
		// в множество клеток — MapDecodeModel.generate.
		public Dictionary<int, List<int[]>> colliders = new Dictionary<int, List<int[]>>();
	}
}
