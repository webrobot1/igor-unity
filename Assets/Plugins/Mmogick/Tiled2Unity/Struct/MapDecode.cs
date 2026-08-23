using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[System.Serializable]
	public class MapDecode
	{
		public int width;
		public int height;
		public int spawn_sort;

		/// <summary>
		/// Имя слоя-земли карты — того, чей порядок отрисовки лёг в <see cref="spawn_sort"/>. Пусто — карта
		/// слой не назвала (свойство spawn) либо назвала тот, которого у неё нет: тогда клиент подставляет
		/// запасной индекс, и слой-земля выбран не картой. Показывается в служебном блоке счётчиков.
		/// </summary>
		public string spawn;

		/// <summary>
		/// Сторона клетки карты в пикселях графики — как её задали в редакторе карт. Задаёт потолок
		/// детальности при рисовании карты в картинку (<see cref="WorldMapRenderer"/>): выше него
		/// растёт только вес, новых точек в тайле не появляется.
		/// </summary>
		public int tilewidth;

		/// <summary>
		/// Название карты, как задано ей в редакторе карт. Показывается в служебном блоке счётчиков рядом
		/// с её номером: номер адресует карту, название говорит, где игрок находится.
		/// </summary>
		public string name;

		/// <summary>
		/// Непроходимые клетки ИМЕННО этой карты. Проверка проходимости идёт по карте сущности
		/// (getMaps()[map].colliders), не по общему статику: в открытом мире соседние карты грузятся
		/// циклом, единый статик хранил бы коллайдеры случайного последнего сегмента, не нужной карты.
		/// </summary>
		public HashSet<Vector2Int> colliders;

		public MapDecode(Map map)
		{
			this.spawn_sort = (int)map.spawn_sort;
			this.width = map.width;
			this.height = map.height;
			this.tilewidth = map.tilewidth;
			this.name = map.name;
		}
	}
}