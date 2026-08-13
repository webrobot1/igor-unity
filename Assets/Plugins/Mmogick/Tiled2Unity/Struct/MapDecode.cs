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
			this.name = map.name;
		}
	}
}