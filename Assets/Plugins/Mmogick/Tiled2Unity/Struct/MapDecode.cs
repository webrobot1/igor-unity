using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[Serializable]
	public class MapDecode
	{
		/// <summary>
		/// Порядок отрисовки сущностей на карте БЕЗ слоя-земли. Землю тогда выбрала не карта, и число это
		/// запасное: слой второй по счёту — под ним обычно пол, над ним всё прочее.
		/// </summary>
		public const int FallbackGroundOrder = 1;

		/// <summary>Слой-земля этажа: его порядок отрисовки и имя, каким его назвали в редакторе карт.</summary>
		public struct Ground
		{
			public int order;
			public string name;
		}

		public int width;
		public int height;

		/// <summary>
		/// Слои-земли карты по этажам: номер этажа — индекс. Этаж k — корневые слои после земли k−1 (либо от
		/// первого слоя) до земли k включительно, слои после последней земли — потолок. Существо этажа
		/// рисуется на порядке его земли: слои этажа под ним, слои выше этой земли — над ним. Пусто — у карты
		/// нет корневого слоя с классом слоя-земли, и этаж один (<see cref="FallbackGroundOrder"/>).
		/// </summary>
		public Ground[] grounds = new Ground[0];

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
		/// Непроходимые клетки ИМЕННО этой карты по этажам (ключ — номер этажа, как его шлёт сервер).
		/// Проверка проходимости идёт по карте сущности (getMaps()[map]), не по общему статику: в открытом
		/// мире соседние карты грузятся циклом, единый статик хранил бы коллайдеры случайного последнего
		/// сегмента, не нужной карты. И по этажу сущности: стены другого этажа её не держат.
		/// </summary>
		public Dictionary<int, HashSet<Vector2Int>> colliders = new Dictionary<int, HashSet<Vector2Int>>();

		/// <summary>
		/// Клетки этой карты, где лежит хоть один тайл, по этажам — тем же, по которым игра раскладывает
		/// слои (уровень слоя в terrain.json). Тайлы потолка (<see cref="Layer.ceiling"/>) сюда не идут:
		/// полом клетки они не служат. Клетка без тайла — та самая чернота за краем рисунка карты, как и
		/// клетка, где тайл есть только в потолке: сервер её в матрицу проходимости своего этажа не берёт
		/// и шага туда не делает, потому клиентская проверка холостых команд движения спрашивает и её.
		/// </summary>
		public Dictionary<int, HashSet<Vector2Int>> tiles = new Dictionary<int, HashSet<Vector2Int>>();

		/// <summary>
		/// Клетки, где нарисован тайл слоя, по порядку отрисовки слоя — только слоёв, способных перекрыть
		/// существо (выше первой земли либо запасного порядка). По ним окно прозрачности (TilemapXray)
		/// открывается лишь тогда, когда свой игрок шагнул под перекрывающий слой, а не на подходе к нему.
		/// </summary>
		public Dictionary<int, HashSet<Vector2Int>> drawn = new Dictionary<int, HashSet<Vector2Int>>();

		public MapDecode(Map map)
		{
			this.width = map.width;
			this.height = map.height;
			this.tilewidth = map.tilewidth;
			this.name = map.name;
		}

		/// <summary>
		/// Этаж сущности с уровнем z на этой карте. Уровень, которому этажа у карты нет, — первый этаж (0):
		/// правило игры, чтобы код механик не ставил уровень каждой сущности, когда земля у большинства карт
		/// одна, а переход её меняет. Клетку уровня сервер считает тем же округлением (банковским).
		/// </summary>
		public int Floor(float z)
		{
			int floor = Mathf.RoundToInt(z);
			return floor >= 0 && floor < grounds.Length ? floor : 0;
		}

		/// <summary>Порядок отрисовки сущности с уровнем z — порядок земли её этажа.</summary>
		public int GroundOrder(float z)
		{
			return grounds.Length == 0 ? FallbackGroundOrder : grounds[Floor(z)].order;
		}

		/// <summary>Стоит ли преграда в клетке cell на этаже сущности с уровнем z.</summary>
		public bool IsCollider(float z, Vector2Int cell)
		{
			return colliders.TryGetValue(Floor(z), out HashSet<Vector2Int> cells) && cells.Contains(cell);
		}

		/// <summary>Лежит ли тайл в клетке cell на этаже сущности с уровнем z.</summary>
		public bool HasTile(float z, Vector2Int cell)
		{
			return tiles.TryGetValue(Floor(z), out HashSet<Vector2Int> cells) && cells.Contains(cell);
		}

		/// <summary>Нарисован ли в клетке cell тайл слоя, чей порядок отрисовки выше order.</summary>
		public bool IsCovered(int order, Vector2Int cell)
		{
			foreach (KeyValuePair<int, HashSet<Vector2Int>> layer in drawn)
				if (layer.Key > order && layer.Value.Contains(cell))
					return true;

			return false;
		}
	}
}
