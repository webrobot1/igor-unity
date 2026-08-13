using System.Collections.Generic;

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
		// id и shape кладёт в срез карты сама сборка (сервер): по id объект адресуют страницы админки, по shape
		// рисуют его форму, не повторяя у себя правило её выбора. Срез общий, потому оба поля приходят и клиенту.
		public int id;

		// Форма объекта одним значением: rectangle / ellipse / point / polygon / polyline.
		public string shape;

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

		// Объект — точка (взаимоисключающе с ellipse/polygon/polyline).
		public bool point;

		public Point[] polygon;
		public Point[] polyline;

		public string text;

		// Свойства объекта (ключ — name). У объекта класса переходов ими задана цель: map — код карты
		// назначения (пусто — прибытие в пределах своей карты), x/y — клетка прибытия.
		public Dictionary<string, LayerProperty> property;
	}
}
