using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[System.Serializable]
	public class Map
	{
		// Срез карты (terrain.json) общий для игрового процесса, клиента и админки — оттого несёт и поля,
		// нужные не всем: идентичность карты, её место в открытом мире, состав наборов графики.
		public int id;
		public string name;

		// Тип проекции: orthogonal / isometric / staggered / hexagonal.
		public string orientation;

		public string renderorder;
		public int width;
		public int height;
		public int tilewidth;
		public int tileheight;

		// Левый-верхний угол карты в тайлах открытого мира; null — карта в раскладке не размещена
		// (интерьер, подземелье), к соседям она примыкает только переходами.
		public int? openworldX = null;
		public int? openworldY = null;

		// Наборы графики карты, ключ — имя файла набора. Пиксели тайлов клиент берёт не отсюда, а своим
		// каналом кеша тайлов — здесь лежит состав наборов карты, которым пользуется сборка среза и админка.
		public Dictionary<string, MapTileset> mapTileset;

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
