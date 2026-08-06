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

		private static Sprite _sprite;

		private SpriteRenderer _renderer;
		private float _phase;   // разводит соседние метки по фазе — иначе вся карта мигает разом

		/// <summary>
		/// Построить слой меток карты. sortingOrder берётся у слоя-земли карты (spawn_sort) — тот же,
		/// по которому сортируются существа: метка лежит с ними в одной плоскости, а не поверх крыш.
		///
		/// Слой строится заново при каждом вызове: карту перекладывают целиком, второго такого слоя быть
		/// не должно.
		/// </summary>
		public static void BuildLayer(Transform grid, Map map, int sortingOrder)
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
					// Класс несёт либо сам объект, либо его слой — так же, как их различает сервер. Свой класс
					// объекта перекрывает класс слоя, и пустой строкой сервер снимает его у одного объекта:
					// так с разметки на бесшовной границе снят класс перехода.
					bool isWarp = !string.IsNullOrEmpty(obj.type) ? obj.type == warpClass : layer.@class == warpClass;
					if (!isWarp)
						continue;

					// Точка и линия площади не имеют — светить нечему.
					if (obj.width <= 0 || obj.height <= 0)
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
			// отладочный контур объекта).
			Rect area = Area(obj, tileWidth, tileHeight);
			float x = area.x;
			float y = area.y;
			float width = area.width;
			float height = area.height;

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

		/// <summary>Площадь объекта в клетках карты: якорь — левый нижний угол, y растёт вверх.</summary>
		private static Rect Area(LayerObject obj, int tileWidth, int tileHeight)
		{
			return new Rect(
				obj.x / (float) tileWidth,
				obj.y / (float) tileHeight,
				obj.width / (float) tileWidth,
				obj.height / (float) tileHeight
			);
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
