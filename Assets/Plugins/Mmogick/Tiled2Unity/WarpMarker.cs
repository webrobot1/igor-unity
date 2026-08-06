using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mmogick
{
	/// <summary>
	/// Свечение на клетках перехода: игрок должен видеть, что отсюда можно уйти на другую карту.
	/// Сущности-портала под переходом нет — переход исполняет сама разметка карты, потому и метку
	/// клиент рисует по объектам разметки, приходящим в terrain, а не по существу в мире.
	///
	/// КАКОЙ класс объектов означает переход, решает игра: её имя приходит при входе
	/// (<see cref="ConnectController.warp_class"/>). Пусто — игра переходами по разметке не пользуется,
	/// слой не строится вовсе.
	///
	/// Разметка, ведущая на карту, с которой эта стыкуется бесшовно, метки НЕ получает: там игрок
	/// переходит границей сам, переноса не происходит. Тот же отбор делает игровой процесс, собирая
	/// свой реестр переходов, — потому метка и переход зажигаются на одних и тех же клетках.
	///
	/// Метка одна на объект и растянута на всю его площадь: широкий проход светится полосой, дверь —
	/// пятном в клетку. Компонент на самой метке гасит и разгорается — неподвижное пятно на полу
	/// теряется среди тайлов.
	/// </summary>
	public class WarpMarker : MonoBehaviour
	{
		// Имя слоя-контейнера меток внутри карты. Наружу нужно, чтобы карту можно было перестроить,
		// не плодя второй такой слой.
		public const string LAYER = "Warps";

		// Имена свойств: у объекта-перехода цель адресована кодом карты, у самой карты этот код лежит
		// её свойством. Канон общий с сервером и игровым процессом.
		private const string TARGET_MAP = "map";
		private const string MAP_CODE = "slug";

		private const string SpriteResource = "Sprites/Map/warp_glow";

		private const float AlphaMin = 0.55f;
		private const float AlphaMax = 1f;
		private const float PulseSpeed = 1.8f;

		// Свечение выходит за края площади перехода: ровно по клетке оно читается как пятно грязи на полу,
		// а размытым краем — как источник света. В клетках.
		private const float Spill = 0.6f;

		private static Sprite _sprite;

		private SpriteRenderer _renderer;
		private float _phase;   // разводит соседние метки по фазе — иначе вся карта мигает разом

		/// <summary>
		/// Построить слой меток карты. sortingOrder берётся у слоя-земли карты (spawn_sort) — тот же,
		/// по которому сортируются существа: метка лежит с ними в одной плоскости, а не поверх крыш.
		///
		/// loaded — карты, уже выложенные в игровое пространство (ключ — их номер): по ним определяется,
		/// какая цель перехода достижима границей. Слой строится заново при каждом вызове: набор смежных
		/// карт меняется по ходу игры, и метка, погашенная при прежнем наборе, обязана вернуться.
		/// </summary>
		public static void BuildLayer(Transform grid, Map map, int sortingOrder, Dictionary<int, Map> loaded)
		{
			Transform existing = grid.Find(LAYER);
			if (existing != null)
				DestroyImmediate(existing.gameObject);

			string warpClass = ConnectController.warp_class;
			if (string.IsNullOrEmpty(warpClass) || map.layer == null)
				return;

			Sprite sprite = GetSprite();
			if (sprite == null)
				return;

			HashSet<int> seam = SeamMaps(grid, map, loaded);

			GameObject layerGo = new GameObject(LAYER);
			layerGo.transform.SetParent(grid, false);

			// Метки — обычные спрайты в клеточных координатах grid, а тайлы кладёт Tilemap со своим
			// tileAnchor. Без того же сдвига метка уезжает влево-вниз от своей клетки (тот же случай,
			// что у отладочных контуров объектов). Берём анкер из самого Tilemap, не хардкодим.
			Tilemap anyTilemap = grid.GetComponentInChildren<Tilemap>();
			Vector3 tileAnchor = anyTilemap != null ? anyTilemap.tileAnchor : new Vector3(0.5f, 0.5f, 0f);
			layerGo.transform.localPosition = new Vector3(tileAnchor.x, tileAnchor.y, 0f);

			int count = 0;
			foreach (Layer layer in map.layer.Values)
			{
				if (layer.@object == null)
					continue;

				foreach (LayerObject obj in layer.@object)
				{
					// Класс несёт либо сам объект, либо его слой — так же, как их различает сервер.
					if (obj.type != warpClass && layer.@class != warpClass)
						continue;

					// Точка и линия площади не имеют — светить нечему.
					if (obj.width <= 0 || obj.height <= 0)
						continue;

					if (seam.Contains(TargetMap(obj, loaded)))
						continue;

					Create(layerGo.transform, sprite, obj, map.tilewidth, map.tileheight, sortingOrder, count);
					count++;
				}
			}

			Debug.Log("Карта: меток перехода " + count);
		}

		private static void Create(Transform parent, Sprite sprite, LayerObject obj, int tileWidth, int tileHeight, int sortingOrder, int index)
		{
			// Координаты объекта приходят в пикселях (в клетки их переводит потребитель — так же считает
			// отладочный контур объекта). Якорь — левый нижний угол площади, y растёт вверх.
			float x = obj.x / tileWidth;
			float y = obj.y / tileHeight;
			float width = obj.width / tileWidth;
			float height = obj.height / tileHeight;

			GameObject go = new GameObject(string.IsNullOrEmpty(obj.name) ? "warp" : obj.name);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = new Vector3(x + width / 2f, y + height / 2f, 0f);

			SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
			sr.sprite = sprite;
			sr.sortingOrder = sortingOrder;

			// Растягиваем картинку на площадь объекта: её собственный размер в мировых единицах задаёт
			// импорт спрайта, потому считаем от него, а не от числа точек картинки.
			Vector3 spriteSize = sprite.bounds.size;
			if (spriteSize.x > 0f && spriteSize.y > 0f)
				go.transform.localScale = new Vector3((width + Spill) / spriteSize.x, (height + Spill) / spriteSize.y, 1f);

			WarpMarker marker = go.AddComponent<WarpMarker>();
			marker._renderer = sr;
			marker._phase = index * 0.6f;
		}

		/// <summary>
		/// Номера карт, куда с этой карты уходят ГРАНИЦЕЙ: их прямоугольники в открытом мире касаются её.
		/// Считается по уже выложенным картам — сервер присылает стороны только текущей карты, поэтому у
		/// соседней её собственные дальние соседи ещё неизвестны и метка там останется до перехода на неё.
		/// </summary>
		private static HashSet<int> SeamMaps(Transform grid, Map map, Dictionary<int, Map> loaded)
		{
			HashSet<int> seam = new HashSet<int>();
			Dictionary<int, Point> sides = MapController.getSides();

			// Номер карты несёт имя её корня на сцене — в самой карте его нет (см. RebuildWarpLayers).
			if (loaded == null || !int.TryParse(grid.name, out int mapId) || !sides.TryGetValue(mapId, out Point self))
				return seam;

			foreach (KeyValuePair<int, Map> pair in loaded)
			{
				if (pair.Key == mapId || !sides.TryGetValue(pair.Key, out Point other))
					continue;

				if (Touches(self, map, other, pair.Value))
					seam.Add(pair.Key);
			}

			return seam;
		}

		/// <summary>
		/// Касаются ли карты сторонами. Позиция карты — её левый ВЕРХНИЙ угол в клетках относительно
		/// текущей (y вниз убывает), потому карта занимает [x, x+width] по горизонтали и [y-height, y]
		/// по вертикали. Открытый мир перекрытия карт не допускает — общая сторона и есть бесшовный стык.
		/// </summary>
		private static bool Touches(Point a, Map aMap, Point b, Map bMap)
		{
			float aRight = a.x + aMap.width, aBottom = a.y - aMap.height;
			float bRight = b.x + bMap.width, bBottom = b.y - bMap.height;

			bool overlapX = a.x < bRight && b.x < aRight;
			bool overlapY = aBottom < b.y && bBottom < a.y;

			bool touchX = Mathf.Approximately(aRight, b.x) || Mathf.Approximately(bRight, a.x);
			bool touchY = Mathf.Approximately(a.y, bBottom) || Mathf.Approximately(b.y, aBottom);

			return (touchX && overlapY) || (touchY && overlapX);
		}

		/// <summary>
		/// Номер карты, куда ведёт переход, либо 0, если её среди выложенных нет. Цель задана КОДОМ карты,
		/// и разбирается он как на сервере: точное совпадение кода либо код-начало (в разметке источника
		/// цель зовут коротким номером «002-2», а код карты мира несёт ещё имя и отпечаток).
		/// </summary>
		private static int TargetMap(LayerObject obj, Dictionary<int, Map> loaded)
		{
			if (obj.property == null || !obj.property.TryGetValue(TARGET_MAP, out LayerProperty target))
				return 0;

			string code = target.value == null ? "" : target.value.Trim();
			if (code.Length == 0 || loaded == null)
				return 0;

			foreach (KeyValuePair<int, Map> pair in loaded)
			{
				string slug = Code(pair.Value);
				if (slug == code || slug.StartsWith(code + "-"))
					return pair.Key;
			}

			return 0;
		}

		/// <summary>Код карты — её стабильная идентичность, им же адресуют цель переходы разметки.</summary>
		private static string Code(Map map)
		{
			if (map.property == null || !map.property.TryGetValue(MAP_CODE, out LayerProperty code))
				return "";

			return code.value ?? "";
		}

		private static Sprite GetSprite()
		{
			if (_sprite == null)
			{
				_sprite = Resources.Load<Sprite>(SpriteResource);
				if (_sprite == null)
					Debug.LogError("WarpMarker: нет картинки Resources/" + SpriteResource + " — метки переходов не рисуются");
			}
			return _sprite;
		}

		private void LateUpdate()
		{
			if (_renderer == null)
				return;

			float pulse = (Mathf.Sin(Time.time * PulseSpeed + _phase) + 1f) * 0.5f;
			Color color = _renderer.color;
			color.a = Mathf.Lerp(AlphaMin, AlphaMax, pulse);
			_renderer.color = color;
		}
	}
}
