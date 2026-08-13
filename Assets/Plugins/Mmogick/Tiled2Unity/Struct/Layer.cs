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

		// Тип слоя Tiled: tilelayer (тайлы), objectgroup (объекты-разметка), group (контейнер дочерних).
		public string type;

		// Порядок сортировки слоя (0 — авто, по позиции в карте).
		public int sort;

		// Цвет тонирования слоя из Tiled (#RRGGBB либо #AARRGGBB); пусто — тонирования нет.
		public string tintcolor;

		// class слоя из Tiled (напр. "collision" у слоя непроходимых зон). Debug-рендер объектов-разметки
		// (DebugObjects в MapDecodeModel) исключает слой коллизий по этому признаку — он уже в DebugCollision.
		public string @class;

		public bool visible = true;
		public float opacity = 1;

		public float offsetx;
		public float offsety;
		public float offsetz;

		public string resource;

		// Компактная самодостаточная CSV-строка тайлов слоя (формат LayerTileCsvCodec на сервере):
		// "легенда\nданные". До '\n' — distinct sha256 тайлов через ';'. После — CSV из width*height
		// ячеек через ','; значение ячейки = индекс тайла в легенде (1-based; 0 = пусто) + опц. флаги
		// через '|' битмаской (1=flipH, 2=flipV, 4=flipD, 8=rotHex120). Позиция ячейки i = y*width+x.
		// Пустой слой — "". Декодируется в LayerTile-ы в MapDecodeModel.generate.
		public string tile;

		public LayerObject[] @object;

		public Dictionary<string, LayerProperty> property;

		// Дочерние слои слоя-контейнера (type=group), ключ — id слоя. Структура рекурсивна.
		public Dictionary<int, Layer> layer;
	}
}
