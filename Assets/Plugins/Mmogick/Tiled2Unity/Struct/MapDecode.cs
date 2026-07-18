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
		public int map_id;
		public int width;
		public int height;
		public int spawn_sort;

		/// <summary>
		/// Непроходимые клетки ИМЕННО этой карты. Проверка проходимости идёт по карте сущности
		/// (getMaps()[map].colliders), не по общему статику: в открытом мире соседние карты грузятся
		/// циклом, единый статик хранил бы коллайдеры случайного последнего сегмента, не нужной карты.
		/// </summary>
		public HashSet<Vector2Int> colliders;

		public MapDecode(Map map)
		{
			this.map_id = map.map_id;
			this.spawn_sort = (int)map.spawn_sort;
			this.width = map.width;
			this.height = map.height;
		}
	}
}