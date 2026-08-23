using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mmogick
{
	/// <summary>
	/// Рисует миниатюру одной карты — картинку для обзорной карты мира. Источник — та же скачанная карта,
	/// что и в игре (кеш карт), и та же сборка тайлов <see cref="MapDecodeModel.generate"/>: миниатюра
	/// показывает ровно то, что игрок видел на месте, и второй реализации отрисовки не заводит.
	///
	/// Карта собирается ВРЕМЕННО, далеко за пределами игровой сцены (<see cref="STAGE"/>), снимается
	/// отдельной камерой в текстуру и тут же уничтожается: в игровом кадре она не появляется, а в сцене
	/// не остаётся. Готовая картинка кладётся в кеш (TileCacheService) и рисуется заново, лишь когда
	/// изменились сама карта либо графика тайлов.
	///
	/// Съёмка синхронна и стоит сборки целой карты — звать по одной карте за кадр, не пачкой.
	/// </summary>
	public static class WorldMapRenderer
	{
		/// <summary>
		/// Нижняя граница детальности — пикселей на клетку карты. Карта открытого мира в сотни клеток
		/// упирается в неё: мир там виден целиком, отдельные тайлы на картинке не читаются, а вес растёт
		/// квадратом стороны.
		/// </summary>
		private const int PIXELS_PER_TILE_MIN = 8;

		/// <summary>
		/// Потолок стороны картинки в пикселях. Мелкая карта (комната, интерьер) занимает в окне обзорной
		/// карты почти всё место, и на увеличении её картинка растягивается в разы — детальность ей нужна
		/// выше, чем открытому миру. Верхний предел держит вес: на увеличении окна картинки не
		/// пересчитываются, растягивается уже нарисованное, и запаса в стороне картинки хватает.
		/// </summary>
		private const int IMAGE_MAX_SIDE = 1024;

		/// <summary>
		/// Версия отрисовки. Входит в отпечаток кешированной картинки (TileCacheService), потому смена правил
		/// рисования обесценивает уже нарисованное: данные карты при этом прежние, и по ним одним устаревшую
		/// картинку не отличить — она осталась бы на диске навсегда.
		/// v2: анимированные тайлы (вода) рисуются первым кадром — до этого проваливались пустотой.
		/// v3: кадр учитывает tileAnchor — прежде картинка уезжала на пол-клетки и по краю оставалась пустая полоса.
		/// v4: детальность считается от размера карты — прежде она была у всех карт одна (8 пикселей на клетку).
		/// </summary>
		public const int RENDER_VERSION = 4;

		/// <summary>
		/// Куда уносится временная карта на время съёмки: место заведомо вне игрового мира, чтобы её не
		/// поймала в кадр игровая камера — она следует за игроком, а тот в такую даль не заходит.
		/// </summary>
		private static readonly Vector3 STAGE = new Vector3(100000f, 100000f, 0f);

		/// <summary>
		/// PNG миниатюры карты либо null, если карты нет в кеше (игрок на ней не был).
		/// </summary>
		public static byte[] Render(int gameId, int mapId)
		{
			string json = TileCacheService.ReadCachedMap(gameId, mapId);
			if (json == null)
				return null;

			GameObject stage = new GameObject("WorldMapStage");
			stage.transform.position = STAGE;
			stage.AddComponent<Grid>();

			GameObject cameraObject = null;
			RenderTexture texture = null;
			Texture2D shot = null;

			try
			{
				MapDecode decoded = MapDecodeModel.generate(json, stage.transform, gameId);

				// Детальность — от размера самой карты: столько пикселей на клетку, чтобы большая сторона
				// картинки дошла до потолка. Выше нативной клетки не поднимаемся — в тайле больше точек
				// нет, и лишнее было бы растяжением того же изображения, только тяжёлым.
				int pixelsPerTile = Mathf.Clamp(
					IMAGE_MAX_SIDE / Mathf.Max(decoded.width, decoded.height),
					PIXELS_PER_TILE_MIN,
					decoded.tilewidth
				);

				int width = decoded.width * pixelsPerTile;
				int height = decoded.height * pixelsPerTile;

				texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);

				// Спрайт тайла кладётся не в саму клетку, а со смещением на tileAnchor тайл-карты (pivot
				// спрайта — угол клетки). Без учёта этого кадр уезжает на пол-клетки, и у картинки остаётся
				// пустая полоса по краю — на стыке двух карт она читается щелью. Берём ИЗ тайл-карты, не
				// константой: сменится anchor в префабе — сдвиг последует за ним.
				Tilemap tilemap = stage.GetComponentInChildren<Tilemap>();
				Vector3 anchor = tilemap != null ? tilemap.tileAnchor : Vector3.zero;

				cameraObject = new GameObject("WorldMapCamera");
				Camera camera = cameraObject.AddComponent<Camera>();
				camera.orthographic = true;
				camera.orthographicSize = decoded.height / 2f;
				// Тайл стоит спрайтом с pivot(0,0) в своей клетке, верхний ряд карты — y=0, нижний —
				// y=-(height-1): по вертикали карта занимает от -(height-1) до 1, отсюда центр кадра.
				camera.transform.position = STAGE + new Vector3(
					decoded.width / 2f + anchor.x,
					1f - decoded.height / 2f + anchor.y,
					-10f
				);
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.clear;   // за краем карты — прозрачность, её закрасит туман обзорной карты
				camera.targetTexture = texture;
				camera.Render();

				RenderTexture active = RenderTexture.active;
				RenderTexture.active = texture;
				shot = new Texture2D(width, height, TextureFormat.RGBA32, false);
				shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				shot.Apply();
				RenderTexture.active = active;

				return shot.EncodeToPNG();
			}
			finally
			{
				if (shot != null)
					Object.Destroy(shot);
				if (texture != null)
				{
					texture.Release();
					Object.Destroy(texture);
				}
				if (cameraObject != null)
					Object.Destroy(cameraObject);

				Object.Destroy(stage);
			}
		}
	}
}
