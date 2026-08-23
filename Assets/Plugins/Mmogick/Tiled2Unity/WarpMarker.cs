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
	/// Разметку на бесшовной границе разбирать не приходится: класс перехода ей снимает сервер, собирая
	/// карту, — сюда она приходит уже обычным объектом. Потому метка и перенос зажигаются на одних и тех
	/// же клетках, а стык карт клиент вообще не считает.
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

		private const string SpriteResource = "Sprites/Map/warp_glow";

		private const float AlphaMin = 0.55f;
		private const float AlphaMax = 1f;
		private const float PulseSpeed = 1.8f;

		// Свечение выходит за края площади перехода: ровно по клетке оно читается как пятно грязи на полу,
		// а размытым краем — как источник света. В клетках.
		private const float Spill = 0.6f;

		// Отступ слоя меток по глубине, в долях клетки. Внутри одного порядка отрисовки камера разводит
		// спрайты осью прозрачной сортировки (высота минус глубина, см. Startup): кто по этой мере дальше,
		// тот позади. Метка стоит в СЕРЕДИНЕ своей клетки, и ровно туда же слой-земля увёл свою точку
		// сортировки, отступив по глубине на полклетки (см. MapDecodeModel.generate), — вышла ничья, при
		// которой порядок метки и тайла её клетки неопределён. Отступаем ещё немного: метка остаётся
		// позади тайла, как и до того сдвига. Отступ намеренно много меньше полклетки — иначе метка
		// уехала бы за сущностей соседнего ряда и ничья вернулась бы там.
		private const float DepthBehindGround = 0.05f;

		private static Sprite _sprite;

		/// <summary>
		/// Место перехода в координатах СЦЕНЫ — тех, в которых стоят сущности и считаны проходы
		/// (<see cref="MapController.Gate"/>). Нужно показам, кладущим метку в один ряд с точками сущностей
		/// (радар): сам объект метки лежит иначе — он накрывает КВАДРАТ клетки нарисованного полотна, а
		/// сцена считает клетку её нижним краем, у ног сущности; оттого перевод и есть полклетки вниз.
		/// По горизонтали разницы нет: и полотно, и сцена считают клетку от её середины.
		///
		/// Считается от ТЕКУЩЕЙ позиции метки, а не запоминается при постройке: карту ставит на её место в
		/// открытом мире MapController.SortMap — уже ПОСЛЕ разбора (при постройке корень карты стоит в
		/// нуле), да и переход между локациями двигает карты заново.
		/// </summary>
		public Vector2 scene => (Vector2) transform.position - new Vector2(0f, _halfCell);

		private SpriteRenderer _renderer;
		private float _halfCell;   // полклетки карты — из самого Tilemap, не хардкодом (см. BuildLayer)
		private float _phase;      // разводит соседние метки по фазе — иначе вся карта мигает разом

		/// <summary>
		/// Построить слой меток карты. sortingOrder берётся у слоя-земли карты (spawn_sort) — тот же,
		/// по которому сортируются существа: метка лежит с ними в одной плоскости, а не поверх крыш.
		/// Оттого метку под аркой или крышей перекрывает верхний слой — видимой её держит то же окно
		/// прозрачности, что и существа: TilemapXray собирает центры окон и по слою LAYER.
		///
		/// Слой строится заново при каждом вызове: карту перекладывают целиком, второго такого слоя быть
		/// не должно.
		/// </summary>
		public static void BuildLayer(Transform grid, Map map, int sortingOrder)
		{
			Transform existing = grid.Find(LAYER);
			if (existing != null)
				DestroyImmediate(existing.gameObject);

			List<LayerObject> warps = Objects(map);
			if (warps.Count == 0)
				return;

			Sprite sprite = GetSprite();
			if (sprite == null)
				return;

			GameObject layerGo = new GameObject(LAYER);
			layerGo.transform.SetParent(grid, false);

			// Метки — обычные спрайты в клеточных координатах grid, а тайлы кладёт Tilemap со своим
			// tileAnchor. Без того же сдвига метка уезжает влево-вниз от своей клетки (тот же случай,
			// что у отладочных контуров объектов). Берём анкер из самого Tilemap, не хардкодим.
			// По глубине слой отступает на DepthBehindGround — им и разводится ничья со слоем-землёй.
			Tilemap anyTilemap = grid.GetComponentInChildren<Tilemap>();
			Vector3 tileAnchor = anyTilemap != null ? anyTilemap.tileAnchor : new Vector3(0.5f, 0.5f, 0f);
			Vector3 cellSize = anyTilemap != null ? anyTilemap.cellSize : Vector3.one;
			layerGo.transform.localPosition = new Vector3(tileAnchor.x, tileAnchor.y, -cellSize.y * DepthBehindGround);

			for (int i = 0; i < warps.Count; i++)
				Create(layerGo.transform, sprite, warps[i], map.tilewidth, map.tileheight, cellSize.y * 0.5f, sortingOrder, i);

			Debug.Log("Карта: меток перехода " + warps.Count);
		}

		/// <summary>
		/// Объекты разметки, которые игра считает переходами. Правило одно на всех потребителей: свечение
		/// на земле, точка на радаре и точка на обзорной карте мира зажигаются от одного отбора, второго
		/// разбора разметки клиент не заводит.
		///
		/// Переход задаёт СОБСТВЕННЫЙ класс объекта — так же, как его различает сервер: класс слоя несёт
		/// лишь коллизии. Разметке, легшей на бесшовную границу, сервер ставит пустой класс, и переходом
		/// она быть перестаёт. Точка и линия площади не имеют — переходом они не считаются.
		///
		/// Игра переходами по разметке не пользуется (<see cref="ConnectController.warp_class"/> пуст) либо
		/// разметки у карты нет — список пуст.
		/// </summary>
		public static List<LayerObject> Objects(Map map)
		{
			List<LayerObject> warps = new List<LayerObject>();

			string warpClass = ConnectController.warp_class;
			if (string.IsNullOrEmpty(warpClass) || map.layer == null)
				return warps;

			foreach (Layer layer in map.layer.Values)
			{
				if (layer.@object == null)
					continue;

				foreach (LayerObject obj in layer.@object)
					if (obj.type == warpClass && obj.width > 0 && obj.height > 0)
						warps.Add(obj);
			}

			return warps;
		}

		private static void Create(Transform parent, Sprite sprite, LayerObject obj, int tileWidth, int tileHeight, float halfCell, int sortingOrder, int index)
		{
			// Координаты объекта приходят в пикселях (в клетки их переводит потребитель — так же считает
			// отладочный контур объекта).
			Rect area = Area(obj, tileWidth, tileHeight);

			GameObject go = new GameObject(string.IsNullOrEmpty(obj.name) ? "warp" : obj.name);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = new Vector3(area.center.x, area.center.y, 0f);

			SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
			sr.sprite = sprite;
			sr.sortingOrder = sortingOrder;

			// Растягиваем картинку на площадь объекта: её собственный размер в мировых единицах задаёт
			// импорт спрайта, потому считаем от него, а не от числа точек картинки.
			Vector3 spriteSize = sprite.bounds.size;
			if (spriteSize.x > 0f && spriteSize.y > 0f)
				go.transform.localScale = new Vector3((area.width + Spill) / spriteSize.x, (area.height + Spill) / spriteSize.y, 1f);

			WarpMarker marker = go.AddComponent<WarpMarker>();
			marker._renderer = sr;
			marker._halfCell = halfCell;
			marker._phase = index * 0.6f;
		}

		/// <summary>
		/// Площадь объекта в КЛЕТКАХ карты — тех же, которыми Tilemap адресует тайлы: столбец c занимает
		/// [c .. c+1], ряд r — [r .. r+1].
		///
		/// Объект приходит якорем в ЛЕВОМ ВЕРХНЕМ углу, ось Y смотрит вверх, а ряды идут от него ВНИЗ
		/// (terrain уже в sandbox-конвенции — см. MapDecodeModel.generate, тем же счётом разворачиваются
		/// в клетки преграды карты). Потому НИЖНИЙ ряд площади — это obj.y минус её высота плюс одна
		/// клетка: у объекта в одну клетку это сам obj.y, у более высокого — ряд под ним.
		///
		/// Единственное место этого счёта на весь клиент: тем же считает и отладочный контур объекта
		/// (<see cref="MapDecodeModel.DrawDebugObject"/>) — иначе контур и метка показывали бы разные места.
		/// </summary>
		public static Rect Area(LayerObject obj, int tileWidth, int tileHeight)
		{
			float width  = obj.width  / (float) tileWidth;
			float height = obj.height / (float) tileHeight;

			return new Rect(obj.x / (float) tileWidth, obj.y / (float) tileHeight - height + 1f, width, height);
		}

		/// <summary>
		/// Середина площади перехода в координатах СЦЕНЫ — тех, в которых стоят сущности и считаны проходы
		/// (<see cref="MapController.Gate"/>): клетка есть целая точка. Площадь <see cref="Area"/> считана
		/// по КРАЯМ клеток, а сцена держит клетку серединой по горизонтали и нижним краем по вертикали —
		/// оттого перевод и есть полклетки по обеим осям.
		///
		/// Тем же местом метка ложится на радар и на обзорную карту мира; выложенная на сцене метка отдаёт
		/// его от своей позиции (<see cref="scene"/>), здесь — для разметки карт, которых в сцене нет вовсе.
		/// </summary>
		public static Vector2 Center(LayerObject obj, int tileWidth, int tileHeight)
		{
			return Area(obj, tileWidth, tileHeight).center - new Vector2(0.5f, 0.5f);
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
