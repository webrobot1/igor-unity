using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace Mmogick
{
	/// <summary>
	/// Обрабатывает terrain.json (новый content-addressable формат).
	/// Графика и мета тянутся из TileCacheService (предварительно синхронизированного до входа в игру).
	/// </summary>
	abstract public class MapDecodeModel
	{
		/// <summary>
		/// Готовые плитки: «картинка + поворот» → объект плитки. Объект плитки не зависит от места, где её
		/// поставили, поэтому один и тот же кусок травы обслуживает тысячи клеток — и одну карту, и соседние
		/// (набор картинок у карт мира общий). Набор живёт, пока идёт игра одной игры; при смене игры
		/// сбрасывается — картинки там другие.
		/// </summary>
		/// <summary>
		/// Данные карты, из которых строятся слои поверх неё: сама карта, её разбор (непроходимые клетки по
		/// этажам) и слой сортировки. Отладочные слои по ним создаются лишь при включении галочки (см.
		/// EnsureDebugLayer), Ключ — корень карты на сцене; уничтоженные карты отсеиваются при появлении новой.
		/// </summary>
		private class MapSource
		{
			public Map map;
			public MapDecode decoded;
			public int sort;
		}

		private static readonly Dictionary<Transform, MapSource> mapSources = new Dictionary<Transform, MapSource>();

		/// <summary>
		/// Уровень своего игрока — от него отладочный слой преград выбирает этажи, чьи стены показать, а слой
		/// меток переходов — этаж, чьи метки видны (см. <see cref="ShowFloor"/>). До входа игрока — первый этаж.
		/// </summary>
		private static float shownZ;

		/// <summary>
		/// Своего игрока перенесли на другой уровень (вход, переход, смена этажа): у выложенных карт видны метки
		/// переходов его нового этажа, а отладочный слой преград показывает стены этого этажа и этажа над ним.
		/// Слои меняются только у карт, где этаж и правда сменился, — у соседа с одной землёй любой уровень
		/// остаётся первым этажом.
		/// </summary>
		public static void ShowFloor(float z)
		{
			if (z == shownZ)
				return;

			float previous = shownZ;
			shownZ = z;

			foreach (KeyValuePair<Transform, MapSource> source in mapSources)
			{
				if (source.Key == null)
					continue;

				int floor = source.Value.decoded.Floor(z);
				if (source.Value.decoded.Floor(previous) == floor)
					continue;

				WarpMarker.ShowFloor(source.Key, floor);

				Transform built = source.Key.Find(DebugLayers.COLLISION);
				if (built == null)
					continue;

				// Немедленно: следующая строка ищет слой по имени и должна его не найти.
				UnityEngine.Object.DestroyImmediate(built.gameObject);
				if (DebugLayers.ShowCollision)
					EnsureDebugLayer(source.Key, DebugLayers.COLLISION);
			}
		}

		private static readonly Dictionary<string, TilemapModel> tileAssets = new Dictionary<string, TilemapModel>();
		private static int tileAssetsGame;

		/// <summary>Resources-префаб слоя тайлов: на нём стоят и игровые слои карты, и отладочные.</summary>
		private const string TilemapPrefab = "Prefabs/Tilemap";

		/// <summary>
		/// Новый слой тайлов из Resources-префаба. Префаб — часть сборки клиента, и его отсутствие значит
		/// сборку битую: без проверки движок отдаёт пустоту, а падение приходит строкой ниже, у первого
		/// обращения к компоненту, и причины уже не называет.
		/// </summary>
		private static GameObject NewTilemapLayer()
		{
			UnityEngine.Object prefab = Resources.Load(TilemapPrefab, typeof(GameObject));
			if (prefab == null)
				throw new Exception("Карта: в сборке нет префаба слоя тайлов Resources/" + TilemapPrefab);

			return (GameObject) UnityEngine.Object.Instantiate(prefab);
		}

		/// <summary>
		/// Плитка для клетки: из набора, а при первом появлении — создаётся и в набор кладётся.
		/// Ключ — картинка вместе с флагами разворота и углом: одна и та же картинка, повёрнутая иначе,
		/// это другая плитка. Угол ненулевым приходит только от объектов слоя — у клеточных тайлов его нет.
		/// </summary>
		private static TilemapModel getTileAsset(int gameId, string sha256, bool flipH, bool flipV, bool flipD, bool rotHex120, float rotation = 0f)
		{
			if (tileAssetsGame != gameId)
			{
				// Плитки созданы кодом (CreateInstance): снятие ссылки объект движка не уничтожает — он жил бы
				// до конца сессии. Картинки прошлой игры к этому моменту уже ни на чём не стоят: карты сносит
				// MapController.Awake при входе.
				foreach (TilemapModel stale in tileAssets.Values)
					if (stale != null)
						UnityEngine.Object.Destroy(stale);

				tileAssets.Clear();
				tileAssetsGame = gameId;
			}

			string key = sha256
				+ (flipH ? "H" : "")
				+ (flipV ? "V" : "")
				+ (flipD ? "D" : "")
				+ (rotHex120 ? "R" : "")
				+ (rotation != 0f ? "A" + rotation.ToString(CultureInfo.InvariantCulture) : "");

			// Живость проверяем и у самой плитки, и у её картинки: спрайты кеша тайлов сносит его очистка
			// (SpriteCache.Clear — приход нового архива, сброс кеша), а плитку она не трогает. Плитка с
			// уничтоженной картинкой рисуется белым прямоугольником — ни компилятор, ни консоль этого не
			// показывают. Первый кадр анимированной плитки лежит и в sprite (см. TilemapModel.addSprites),
			// потому одной проверки хватает обоим родам плиток.
			if (tileAssets.TryGetValue(key, out TilemapModel known))
			{
				if (known != null && known.sprite != null)
					return known;

				if (known != null)
					UnityEngine.Object.Destroy(known);
			}

			TilemapModel created = TilemapModel.CreateInstance<TilemapModel>();

			Matrix4x4 trs = BuildTileMatrix(flipH, flipV, flipD, rotHex120);

			// Tiled rotation для объектов — CW в градусах вокруг точки (x,y), которая совпадает с pivot
			// спрайта (0,0) в координатах ячейки (Sprite.Create с pivot=(0,0)). Знак инвертируем: Tiled CW →
			// Unity Z CCW. Поворот применяется СЛЕВА от flip-матрицы: сначала нормализуется ориентация
			// флагами (внутри ячейки), затем весь объект крутится вокруг pivot.
			if (rotation != 0f)
				trs = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -rotation)) * trs;

			created.transform = trs;
			applySprite(created, gameId, sha256);

			tileAssets[key] = created;
			return created;
		}

		/// <summary>
		/// Разбор скачанной карты (terrain.json). Отдельно от сборки на сцене: разметка карты нужна и без
		/// неё — обзорная карта мира берёт переходы у карт, которых сейчас в сцене нет вовсе.
		/// </summary>
		public static Map parse(string json)
		{
			// Канон сервера: sandbox-скаляры приходят всегда, включая null (null ≡ отсутствие ≡ дефолт).
			// Ignore не даёт Newtonsoft писать null в не-nullable поля — null оставляет дефолт поля.
			return JsonConvert.DeserializeObject<Map>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
		}

		public static MapDecode generate(string json, Transform grid, int gameId)
		{
			// Класс слоя-земли приходит при входе и контрактом не пуст (SigninController.Contract). Пустой здесь —
			// разбор позван до входа: сравнение с пустым классом взяло бы землёй любой слой без класса.
			if (string.IsNullOrEmpty(ConnectController.ground_class))
				throw new InvalidOperationException("Разбор карты до входа в игру: класс слоя-земли не получен");

			Map map = parse(json);

			// grid.localPosition здесь НЕ трогаем: MapController.SortMap выставляет его сразу после generate
			// (позиция карты в открытом мире + TILE_OFFSET). Прежняя установка -0.5 тут была мёртвой (затиралась).

			MapDecode decoded = new MapDecode(map);

			// Преграды приходят прямоугольниками по этажам (см. Map.colliders) — разворачиваем в клетки своего
			// этажа: проверка шага спрашивает конкретную клетку на этаже сущности.
			if (map.colliders != null)
			{
				foreach (KeyValuePair<int, List<int[]>> floor in map.colliders)
				{
					HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
					foreach (int[] rect in floor.Value)
					{
						for (int row = 0; row < rect[3]; row++)
						{
							for (int col = 0; col < rect[2]; col++)
								cells.Add(new Vector2Int(rect[0] + col, rect[1] - row));
						}
					}

					decoded.colliders[floor.Key] = cells;
				}
			}
			// Пересчёт позиционных полей объектов (тайлы слоёв декодируются из CSV ниже)
			foreach (Layer layer in map.layer.Values)
			{
				if (layer.@object != null)
				{
					foreach (LayerObject obj in layer.@object)
					{
						if (!string.IsNullOrEmpty(obj.tile))
						{
							// terrain.json уже в sandbox-convention: anchor = top-left, y+ вверх.
							// Unity tilemap тоже y+ вверх от верха карты, поэтому только делим на размер клетки.
							obj.x = obj.x / map.tilewidth;
							obj.y = obj.y / map.tileheight;
						}
					}
				}
			}

			// Слои-земли — корневые слои с классом ConnectController.ground_class, по одной на этаж, в порядке
			// слоёв (см. MapDecode.grounds). Порядок отрисовки земли — порядок её этажа у сущностей; первая
			// земля ещё и граница Chunk-режима. Земли у карты может не быть — тогда этаж один, а порядок
			// сущностей запасной (MapDecode.FallbackGroundOrder).
			List<MapDecode.Ground> grounds = new List<MapDecode.Ground>();

			int sort = 0;

			foreach (Layer layer in map.layer.Values)
			{
				GameObject newLayer = NewTilemapLayer();
				newLayer.name = layer.name;
				newLayer.transform.SetParent(grid, false);
				newLayer.GetComponent<TilemapRenderer>().sortingOrder = sort;

				if (!layer.visible)
				{
					newLayer.SetActive(false);
					Debug.Log(layer.name + "- слой скрыт");
				}

				Tilemap tilemap = newLayer.GetComponent<Tilemap>();

				// Клетки слоя, способного перекрыть существо (см. MapDecode.drawn): слои под первой землёй существ не
				// перекрывают, скрытый слой не рисуется вовсе.
				HashSet<Vector2Int> drawnCells = null;
				if (layer.visible && (grounds.Count > 0 || sort > MapDecode.FallbackGroundOrder))
					decoded.drawn[sort] = drawnCells = new HashSet<Vector2Int>();

				if (!string.IsNullOrEmpty(layer.tile))
				{
					List<LayerTile> tiles = DecodeTileCsv(map, layer);

					// Клетки с тайлом (см. MapDecode.tiles) — на этаже слоя, как их раскладывает игра: сервер берёт
					// их в матрицу проходимости этого этажа, а клетку без тайла держит непроходимой наравне с
					// преградой. Потолок полом клетки не служит — его тайлы только рисуются. Собираем попутно с
					// раскладкой — отдельного прохода по карте это не стоит.
					HashSet<Vector2Int> tileCells = null;
					if (!layer.ceiling)
					{
						int floor = Mathf.RoundToInt(layer.offsetz);
						if (!decoded.tiles.TryGetValue(floor, out tileCells))
							decoded.tiles[floor] = tileCells = new HashSet<Vector2Int>();
					}

					// Плитка на клетку не создаётся: одна и та же картинка с тем же поворотом повторяется на карте
					// тысячи раз, а объект плитки от места не зависит — берём готовый из общего набора (см. tileAssets).
					// Раскладываем всё одним движением: поклеточная установка перестраивает внутренние структуры
					// тайл-карты на каждую клетку, и на слое в двадцать тысяч клеток это занимало почти секунду —
					// ровно та задержка, что была видна при возвращении на карту с двумя соседями.
					Vector3Int[] positions = new Vector3Int[tiles.Count];
					TileBase[] assets = new TileBase[tiles.Count];

					for (int i = 0; i < tiles.Count; i++)
					{
						LayerTile tile = tiles[i];

						positions[i] = new Vector3Int(tile.x, tile.y, 0);
						TilemapModel asset = getTileAsset(gameId, tile.tile, tile.flipH, tile.flipV, tile.flipD, tile.rotHex120);
						assets[i] = asset;

						tileCells?.Add(new Vector2Int(tile.x, tile.y));
						if (drawnCells != null)
							AddCovered(drawnCells, asset, tile.x, tile.y);
					}

					tilemap.SetTiles(positions, assets);

					Debug.Log("Карта: у слоя " + newLayer.name + " раставлены " + tiles.Count + " тайлов (" + tileAssets.Count + " разных плиток в наборе)");
				}

				if (layer.@object != null)
				{
					foreach (LayerObject obj in layer.@object)
					{
						if (string.IsNullOrEmpty(obj.tile)) continue;

						// Сервер шлёт sha256 в поле tile + flip-флаги отдельными bool. Формат идентичен LayerTile.
						// Плитка берётся из ОБЩЕГО набора наравне с клеточными: объект слоя от клетки отличается
						// только углом, а объект плитки от места не зависит — своя копия на объект и повторялась
						// бы у одинаковых объектов, и жила бы до конца сессии (набор сносит смена игры).
						TilemapModel newTile = getTileAsset(gameId, obj.tile, obj.flipH, obj.flipV, obj.flipD, obj.rotHex120, obj.rotation);

						tilemap.SetTile(new Vector3Int((int)obj.x, (int)obj.y, 0), newTile);
						if (drawnCells != null)
							AddCovered(drawnCells, newTile, (int)obj.x, (int)obj.y);
					}
				}

				if (layer.opacity < 1f)
					ApplyOpacity(newLayer, layer.opacity);

				if (layer.@class == ConnectController.ground_class)
				{
					Debug.Log(layer.name + " — слой-земля этажа " + grounds.Count + " (класс " + ConnectController.ground_class + ")");
					grounds.Add(new MapDecode.Ground { order = sort, name = layer.name });

					// Слой-земля делит порядок отрисовки с существами своего этажа (порядок уходит им в
					// SortingGroup, см. MapController.SortMap), а внутри одного порядка их разводит ось прозрачной
					// сортировки камеры: она вычитает глубину из высоты, и кто по этой мере дальше, тот
					// позади. Точка сортировки тайла — низ его картинки, то есть НИЖНИЙ край клетки, и там
					// же стоит сущность этой клетки: та привязана ногами (см. MapController.TILE_OFFSET).
					// Ничья — порядок неопределён, и объект слоя-земли накрывал стоящего в его клетке
					// целиком. Уводим слой по глубине на ПОЛКЛЕТКИ: его точка сортировки уезжает в
					// СЕРЕДИНУ клетки, туда же встаёт граница «перед/за» — в нижней половине клетки
					// существо перед объектом, в верхней уже за ним.
					// Двигаем сторону КАРТЫ: глубина сущности занята игровым уровнем, приходящим с сервера
					// (EntityModel.SetData), сдвиг существ сломал бы уровни.
					newLayer.transform.localPosition = new Vector3(0f, 0f, -tilemap.cellSize.y * 0.5f);
				}

				// Слои под первой землёй существ не перекрывают никогда — им хватает дешёвой отрисовки кусками.
				if (grounds.Count == 0)
					newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;

				sort++;
			}

			decoded.grounds = grounds.ToArray();

			// Отладочные слои (сетка, непроходимые клетки, объекты-разметка) сразу НЕ строятся. Вместе они
			// накрывают карту трижды — у карты 140×120 это больше тридцати тысяч клеток плюс все контуры объектов, —
			// а нужны, только когда их включают галочкой в тестовом режиме. Потому здесь лишь запоминаем данные,
			// из которых слой можно построить, а строит его EnsureDebugLayer в момент включения.
			mapSources[grid] = new MapSource { map = map, decoded = decoded, sort = sort };

			// Метки переходов — обычный слой карты, не отладочный: их видит игрок, а не разработчик, и строятся
			// они сразу. Соседей учитывать не нужно: разметке на бесшовной границе класс перехода снимает сервер,
			// собирая карту, — какая метка горит, решают только данные самой карты.
			// Переход срабатывает лишь на этаже слоя своего объекта — видны метки этажа своего игрока.
			WarpMarker.BuildLayer(grid, map, decoded);
			WarpMarker.ShowFloor(grid, decoded.Floor(shownZ));

			// Уничтоженные карты выпадают отсюда же: их корни на сцене снесены, а ключи остались бы навсегда.
			foreach (Transform key in new List<Transform>(mapSources.Keys))
				if (key == null)
					mapSources.Remove(key);

			// Уже включённые слои строим сразу: карта могла прийти позже, чем игрок нажал галочку.
			if (DebugLayers.ShowGrid)
				EnsureDebugLayer(grid, DebugLayers.GRID);
			if (DebugLayers.ShowCollision)
				EnsureDebugLayer(grid, DebugLayers.COLLISION);
			if (DebugLayers.ShowObjects)
				EnsureDebugLayer(grid, DebugLayers.OBJECTS);

			return decoded;
		}


		/// <summary>
		/// Собственная прозрачность слоя карты (opacity из редактора карт) — общим экземпляром материала
		/// на её значение, не своим у каждого слоя.
		///
		/// Обращение к `materials` рендерера ИНСТАНЦИРУЕТ его материалы и отдаёт новый массив на каждый
		/// вызов: прежний код звал его трижды за проход и заводил экземпляр каждому слою каждой карты, а
		/// созданный кодом материал уничтожения рендерера не переживает как ассет — карты же выкладывают
		/// и сносят на каждом переходе открытого мира, и сироты копились бы всю сессию. Значений
		/// прозрачности в карте единицы, потому набор общий; тем же приёмом живут материалы окна
		/// прозрачности (TilemapXray.Instance), и оттенок отсюда читает как раз оно.
		/// </summary>
		private static void ApplyOpacity(GameObject layerObject, float opacity)
		{
			foreach (Renderer renderer in layerObject.GetComponentsInChildren<Renderer>())
			{
				Material source = renderer.sharedMaterial;
				if (source == null)
					continue;

				Color tint = source.color;
				tint.a = opacity;
				renderer.sharedMaterial = TintedMaterial(source, tint);
			}
		}

		/// <summary>Материал-копия исходного с заданным цветом; общий на пару «исходный, цвет».</summary>
		private static Material TintedMaterial(Material source, Color tint)
		{
			// Ключ — сам исходный материал, не его номер: номер движок объявил устаревшим, а ссылка
			// адресует тот же экземпляр и в словаре сравнивается по нему же.
			var key = (source, (Color32)tint);
			if (tinted.TryGetValue(key, out Material known) && known != null)
				return known;

			Material instance = new Material(source) { color = tint };
			tinted[key] = instance;
			return instance;
		}

		private static readonly Dictionary<(Material, Color32), Material> tinted =
			new Dictionary<(Material, Color32), Material>();

		/// <summary>Шейдер линий отладочных контуров объектов-разметки (см. buildDebugObjects).</summary>
		private const string DebugLineShader = "Sprites/Default";

		/// <summary>
		/// Построить отладочный слой карты, если он ещё не построен. Зовётся при включении галочки тестового
		/// режима: до этого слоёв нет вовсе — они втрое дороже самой карты, а видит их лишь разработчик.
		/// Карта уже уничтожена либо данных о ней нет — тихо выходим.
		/// </summary>
		public static void EnsureDebugLayer(Transform grid, string layerName)
		{
			if (grid == null)
				return;

			if (grid.Find(layerName) != null)
				return;

			if (!mapSources.TryGetValue(grid, out MapSource src))
				return;

			if (layerName == DebugLayers.GRID)
				buildDebugGrid(grid, src);
			else if (layerName == DebugLayers.COLLISION)
				buildDebugCollision(grid, src);
			else if (layerName == DebugLayers.OBJECTS)
				buildDebugObjects(grid, src);
		}

		private static void buildDebugGrid(Transform grid, MapSource src)
		{
			// Отладочный слой-сетка. Видимость — галочка «Сетка» debug-панели (DebugLayers.ShowGrid),
			// применяется и к картам, загружаемым позже (см. DebugPanelController).
			GameObject debugGrid = NewTilemapLayer();
			debugGrid.name = DebugLayers.GRID;
			debugGrid.transform.SetParent(grid, false);
			debugGrid.GetComponent<TilemapRenderer>().sortingOrder = src.sort;
			debugGrid.SetActive(DebugLayers.ShowGrid);

			Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
			tex.filterMode = FilterMode.Point;
			Color32 transparent = new Color32(0, 0, 0, 0);
			Color32 border = new Color32(255, 255, 255, 60);
			var pixels = new Color32[32 * 32];
			for (int i = 0; i < pixels.Length; i++)
			{
				int px = i % 32;
				int py = i / 32;
				pixels[i] = (px == 0 || py == 0 || px == 31 || py == 31) ? border : transparent;
			}
			tex.SetPixels32(pixels);
			tex.Apply();

			Sprite gridSprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.zero, 32);
			UnityEngine.Tilemaps.Tile gridTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
			gridTile.sprite = gridSprite;

			// Раскладка одним движением — как и у игровых слоёв: сетка накрывает карту целиком (у карты в
			// 140×120 это почти семнадцать тысяч клеток), и поклеточная установка стоила бы столько же, сколько
			// сам игровой слой. Плитка тут одна на все клетки, разной её не бывает.
			Tilemap debugTilemap = debugGrid.GetComponent<Tilemap>();
			Vector3Int[] gridPositions = new Vector3Int[src.map.width * src.map.height];
			TileBase[] gridTiles = new TileBase[gridPositions.Length];
			int gridIndex = 0;
			for (int x = 0; x < src.map.width; x++)
				for (int y = 0; y < src.map.height; y++)
				{
					gridPositions[gridIndex] = new Vector3Int(x, -y, 0);
					gridTiles[gridIndex] = gridTile;
					gridIndex++;
				}
			debugTilemap.SetTiles(gridPositions, gridTiles);
		}

		/// <summary>
		/// Непрозрачность стен этажа над своим: его стены видны, но от стен своего этажа отличимы.
		/// </summary>
		private const float UpperFloorAlpha = 0.35f;

		private static void buildDebugCollision(Transform grid, MapSource src)
		{
			// Отладочный слой непроходимых клеток. Видимость — галочка «Коллизии» debug-панели
			// (DebugLayers.ShowCollision); её блок открывает настройка игрока «Тестовый режим», а сами слои
			// стартуют выключенными — их зажигает только сама галочка. Применяется и к картам, загружаемым позже.
			// Показаны стены этажа своего игрока и, полупрозрачно, этажа над ним; стены нижних этажей не
			// показываются, пока игрок на них не спустится (смена этажа перестраивает слой — ShowFloor).
			int floor = src.decoded.Floor(shownZ);
			src.decoded.colliders.TryGetValue(floor, out HashSet<Vector2Int> own);
			HashSet<Vector2Int> upper = null;
			if (floor + 1 < src.decoded.grounds.Length)
				src.decoded.colliders.TryGetValue(floor + 1, out upper);

			int ownCount = own?.Count ?? 0;
			int upperCount = upper?.Count ?? 0;
			if (ownCount + upperCount == 0)
				return;

			GameObject debugCollision = NewTilemapLayer();
			debugCollision.name = DebugLayers.COLLISION;
			debugCollision.transform.SetParent(grid, false);
			debugCollision.GetComponent<TilemapRenderer>().sortingOrder = src.sort + 1;
			debugCollision.SetActive(DebugLayers.ShowCollision);

			UnityEngine.Tilemaps.Tile ownTile = DebugCollisionTile(1f);
			UnityEngine.Tilemaps.Tile upperTile = DebugCollisionTile(UpperFloorAlpha);

			// Клетка со стеной на обоих этажах рисуется как стена своего: она держит игрока.
			Vector3Int[] colPositions = new Vector3Int[ownCount + upperCount];
			TileBase[] colTiles = new TileBase[colPositions.Length];
			int colIndex = 0;
			if (upper != null)
				foreach (Vector2Int pos in upper)
				{
					if (own != null && own.Contains(pos))
						continue;

					colPositions[colIndex] = new Vector3Int(pos.x, pos.y, 0);
					colTiles[colIndex] = upperTile;
					colIndex++;
				}
			if (own != null)
				foreach (Vector2Int pos in own)
				{
					colPositions[colIndex] = new Vector3Int(pos.x, pos.y, 0);
					colTiles[colIndex] = ownTile;
					colIndex++;
				}

			Array.Resize(ref colPositions, colIndex);
			Array.Resize(ref colTiles, colIndex);
			debugCollision.GetComponent<Tilemap>().SetTiles(colPositions, colTiles);

			Debug.Log("DebugCollision: этаж " + floor + " — " + ownCount + " непроходимых клеток, этаж над ним — " + upperCount);
		}

		/// <summary>Плитка непроходимой клетки: красная заливка с каймой, прозрачность — множитель alpha.</summary>
		private static UnityEngine.Tilemaps.Tile DebugCollisionTile(float alpha)
		{
			Texture2D colTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
			colTex.filterMode = FilterMode.Point;
			Color32 fill = new Color32(255, 50, 50, (byte)(80 * alpha));
			Color32 edge = new Color32(255, 50, 50, (byte)(180 * alpha));
			var colPixels = new Color32[32 * 32];
			for (int i = 0; i < colPixels.Length; i++)
			{
				int px = i % 32;
				int py = i / 32;
				colPixels[i] = (px == 0 || py == 0 || px == 31 || py == 31) ? edge : fill;
			}
			colTex.SetPixels32(colPixels);
			colTex.Apply();

			UnityEngine.Tilemaps.Tile colTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
			colTile.sprite = Sprite.Create(colTex, new Rect(0, 0, 32, 32), Vector2.zero, 32);
			return colTile;
		}

		private static void buildDebugObjects(Transform grid, MapSource src)
		{
			// Отладочный слой объектов-разметки (зоны спавна, варпы, полигоны). Видимость — галочка
			// «Полигоны» debug-панели (DebugLayers.ShowObjects). Рисуем формы линиями поверх карты.
			// Исключаем: tile-объекты (obj.tile — визуал карты, уже нарисованы тайлами выше) и слой класса
			// коллизий (@class=="collision" — эти зоны уже показаны в DebugCollision).
			GameObject debugObjects = new GameObject(DebugLayers.OBJECTS);
			debugObjects.transform.SetParent(grid, false);
			// LineRenderer рисует в ЧИСТЫХ клеточных координатах grid, а тайлы/коллизии кладутся через Tilemap, который
			// смещает спрайт каждой клетки на свой tileAnchor (у Prefabs/Tilemap = 0.5,0.5 — тот же сдвиг, что
			// MapController.TILE_OFFSET компенсирует для тайлов↔сущностей). LineRenderer этого сдвига не имеет → контуры
			// уезжают на tileAnchor влево-вниз. Совмещаем сдвигом слоя на tileAnchor Tilemap'а — берём ИЗ НЕГО, не
			// хардкодим 0.5 (единый источник: сменится tileAnchor prefab'а — сдвиг следует за ним).
			Tilemap anyTilemap = grid.GetComponentInChildren<Tilemap>();
			Vector3 tileAnchor = anyTilemap != null ? anyTilemap.tileAnchor : new Vector3(0.5f, 0.5f, 0f);
			debugObjects.transform.localPosition = new Vector3(tileAnchor.x, tileAnchor.y, 0f);

			// Canvas подписей объектов: World Space + UI Text (по правилу клиента — не TextMesh, несовместимый с 2D
			// sorting). Дочерний debugObjects → наследует его +0.5,+0.5 сдвиг, подписи выравниваются с контурами.
			GameObject labelCanvasGo = new GameObject("ObjectLabels");
			labelCanvasGo.transform.SetParent(debugObjects.transform, false);
			Canvas labelCanvas = labelCanvasGo.AddComponent<Canvas>();
			labelCanvas.renderMode = RenderMode.WorldSpace;
			labelCanvas.sortingOrder = src.sort + 3;
			Font labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

			// Материал линий ОБЩИЙ, а не свой у каждой карты: слой строится заново при каждой её выкладке, а
			// созданный кодом материал уничтожения рендерера не переживает как ассет — карты же выкладывают и
			// сносят на каждом переходе открытого мира, и сироты копились бы всю сессию (тем же приёмом живут
			// материалы прозрачности, см. TintedMaterial). Рендерер контура ставит его через Renderer.material,
			// а тот назначает КОПИЮ — общий экземпляр никем не правится.
			Material lineMaterial = ShaderMaterial.Get(DebugLineShader, "линии отладочных контуров карты рисовать нечем");

			int objectsCount = 0;

			foreach (Layer layer in src.map.layer.Values)
			{
				if (layer.@object == null || layer.@class == "collision")
					continue;

				foreach (LayerObject obj in layer.@object)
				{
					if (!string.IsNullOrEmpty(obj.tile))
						continue;

					DrawDebugObject(debugObjects.transform, labelCanvas.transform, labelFont, obj, src.map.tilewidth, src.map.tileheight, lineMaterial, src.sort + 2);
					objectsCount++;
				}
			}

			debugObjects.SetActive(DebugLayers.ShowObjects);
			Debug.Log("DebugObjects: " + objectsCount + " объектов-разметки");
		}

		// Адрес негодной ячейки для текста отказа: карт в сцене несколько, слоёв у каждой десяток, а клеток
		// десятки тысяч — без адреса «ошибка разбора карты» читателю журнала не говорит ничего.
		private static string CellAddress(Map map, Layer layer, int cell)
			=> "Карта " + map.id + " «" + map.name + "», слой «" + layer.name + "», клетка ("
				+ (cell % map.width) + "," + ((cell / map.width) * -1) + "), позиция " + cell + " в строке";

		// Декод CSV-строки тайлов слоя в набор LayerTile (зеркало серверного LayerTileCsvCodec::decodeCsv).
		// Формат: "легенда\nданные". До '\n' — distinct sha256 через ';'. После — CSV ячеек через ','.
		// Ячейка = индекс в легенде (1-based; 0/пусто пропускается) + опц. флаги через '|' битмаской
		// (1=flipH, 2=flipV, 4=flipD, 8=rotHex120). Позиция ячейки i = y*width+x; y инвертируется (*-1).
		// Негодное значение ячейки — отказ разбора ВСЕЙ карты (ловит MapController: сброс кеша и выход на
		// экран входа с этим текстом). Пропуск такой клетки был бы дырой в карте: сервер по ней ходит и шлёт
		// туда сущностей, а клиент рисует пустоту и держит клетку непроходимой.
		private static List<LayerTile> DecodeTileCsv(Map map, Layer layer)
		{
			List<LayerTile> result = new List<LayerTile>();

			string s = layer.tile.Trim();
			if (s.Length == 0)
				return result;

			int nl = s.IndexOf('\n');
			if (nl < 0)
				return result; // легенда без данных — пусто

			string[] legend = s.Substring(0, nl).Split(';');
			string[] cells  = s.Substring(nl + 1).Split(',');

			for (int i = 0; i < cells.Length; i++)
			{
				string cell = cells[i].Trim();
				if (cell.Length == 0 || cell == "0")
					continue;

				string[] cellParts = cell.Split('|');
				if (!int.TryParse(cellParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
					throw new Exception(CellAddress(map, layer, i) + ": номер тайла «" + cellParts[0] + "» не число");

				if (idx < 1 || idx > legend.Length)
					throw new Exception(CellAddress(map, layer, i) + ": номер тайла " + idx
						+ " вне легенды слоя — в ней " + legend.Length + " записей");

				int flags = 0;
				if (cellParts.Length > 1 && !int.TryParse(cellParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
					throw new Exception(CellAddress(map, layer, i) + ": флаги разворота «" + cellParts[1] + "» не число");

				result.Add(new LayerTile
				{
					tile      = legend[idx - 1],
					flipH     = (flags & 1) != 0,
					flipV     = (flags & 2) != 0,
					flipD     = (flags & 4) != 0,
					rotHex120 = (flags & 8) != 0,
					x         = i % map.width,
					y         = (i / map.width) * -1,
				});
			}

			return result;
		}

		// Назначает TilemapModel либо одиночный Sprite (нет анимации), либо массив фреймов (есть).
		// Битый тайл-PNG: TryGetSprite инвалидирует свой кеш и бросает exception — здесь не ловим,
		// всплывёт до MapController.LoadMap (он сделает ResetCache и Error).
		private static void applySprite(TilemapModel newTile, int gameId, string sha256)
		{
			var meta = TileCacheService.GetMeta(sha256);
			if (meta != null && meta.frame != null && meta.frame.Length > 0)
			{
				var frames = new TileAnimation[meta.frame.Length];
				for (int i = 0; i < meta.frame.Length; i++)
					frames[i] = new TileAnimation
					{
						frame    = meta.frame[i].frame,
						duration = meta.frame[i].duration,
						sprite   = TileCacheService.TryGetSprite(gameId, meta.frame[i].frame),
					};
				newTile.addSprites(frames);
			}
			else
			{
				newTile.sprite = TileCacheService.TryGetSprite(gameId, sha256);
			}
		}

		/// <summary>
		/// Рисует контур одного объекта-разметки линиями (LineRenderer) в системе координат grid'а — той же,
		/// что тайлы и DebugGrid (grid уже сдвинут на TILE_OFFSET в MapController.SortMap, объект — его потомок).
		///
		/// Координаты объекта приходят в ПИКСЕЛЯХ в серверной convention: якорь — ЛЕВЫЙ ВЕРХНИЙ угол,
		/// ось Y смотрит вверх, ряды идут от якоря ВНИЗ. Площадь в клетках считает <see cref="WarpMarker.Area"/> —
		/// один счёт на весь клиент, иначе контур показывал бы не то место, где горит метка перехода и где
		/// сервер держит преграду. Формы: polygon/ellipse/rect — замкнутый контур, polyline — незамкнутый.
		/// Точки polygon/polyline — относительно якоря объекта.
		/// </summary>
		private static void DrawDebugObject(Transform parent, Transform labelParent, Font font, LayerObject obj, int tw, int th, Material mat, int sortingOrder)
		{
			Rect area = WarpMarker.Area(obj, tw, th);

			float ox = obj.x / tw;
			float oy = obj.y / th;

			List<Vector3> pts = new List<Vector3>();
			bool loop = true;

			if (obj.polygon != null && obj.polygon.Length > 0)
			{
				foreach (Point p in obj.polygon)
					pts.Add(new Vector3(ox + p.x / tw, oy + p.y / th, 0));
			}
			else if (obj.polyline != null && obj.polyline.Length > 0)
			{
				foreach (Point p in obj.polyline)
					pts.Add(new Vector3(ox + p.x / tw, oy + p.y / th, 0));
				loop = false;
			}
			else if (obj.ellipse)
			{
				float rx = area.width / 2f;
				float ry = area.height / 2f;
				const int seg = 24;
				for (int i = 0; i < seg; i++)
				{
					float a = (float)i / seg * Mathf.PI * 2f;
					pts.Add(new Vector3(area.center.x + Mathf.Cos(a) * rx, area.center.y + Mathf.Sin(a) * ry, 0));
				}
			}
			else
			{
				pts.Add(new Vector3(area.xMin, area.yMin, 0));
				pts.Add(new Vector3(area.xMax, area.yMin, 0));
				pts.Add(new Vector3(area.xMax, area.yMax, 0));
				pts.Add(new Vector3(area.xMin, area.yMax, 0));
			}

			GameObject go = new GameObject(string.IsNullOrEmpty(obj.name) ? "object" : obj.name);
			go.transform.SetParent(parent, false);

			LineRenderer lr = go.AddComponent<LineRenderer>();
			lr.useWorldSpace = false;
			lr.material = mat;
			lr.startColor = lr.endColor = DebugObjectColor(obj.type);
			lr.startWidth = lr.endWidth = 0.08f;
			lr.numCapVertices = 0;
			lr.numCornerVertices = 0;
			lr.loop = loop;
			lr.sortingOrder = sortingOrder;
			lr.positionCount = pts.Count;
			lr.SetPositions(pts.ToArray());

			// Подпись name объекта — UI Text в World Space Canvas (labelParent), над верхним краем контура. Мелкий
			// localScale: World Space Canvas по умолчанию 1 unit = 1 px, иначе текст был бы во весь экран.
			if (!string.IsNullOrEmpty(obj.name))
			{
				GameObject lblGo = new GameObject("label");
				lblGo.transform.SetParent(labelParent, false);
				Text lbl = lblGo.AddComponent<Text>();
				lbl.font = font;
				lbl.text = obj.name;
				lbl.fontSize = 32;
				lbl.color = DebugObjectColor(obj.type);
				lbl.alignment = TextAnchor.LowerLeft;
				lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
				lbl.verticalOverflow = VerticalWrapMode.Overflow;
				RectTransform rt = lbl.rectTransform;
				rt.sizeDelta = new Vector2(200f, 40f);
				rt.localScale = Vector3.one * 0.03f;
				// Верх контура: у прямоугольника и эллипса — верх площади, у polygon/polyline сам якорь
				// (их точки подняты собственной геометрией).
				float topY = (obj.polygon == null && obj.polyline == null) ? area.yMax : oy;
				rt.localPosition = new Vector3(area.xMin, topY + 0.15f, 0f);
			}
		}

		// Цвет debug-контура по классу объекта Tiled (obj.type).
		private static Color DebugObjectColor(string type)
		{
			switch (type)
			{
				case "warp":            return new Color(0.3f, 0.7f, 1f, 1f);   // голубой — переходы между картами
				case "spawn":           return new Color(0.4f, 1f, 0.4f, 1f);   // зелёный — зоны спавна
				case "particle_effect": return new Color(1f, 0.4f, 1f, 1f);     // розовый — частицы
				default:                return new Color(1f, 0.9f, 0.2f, 1f);   // жёлтый — прочее
			}
		}

		/// <summary>
		/// Клетки, которые накрывает картинка плитки, поставленной в клетку (x, y): угол картинки стоит в углу
		/// клетки, и крупная плитка (крона, крыша) накрывает и соседние — окно прозрачности должно открываться
		/// под любой из них (см. MapDecode.drawn). Разворот плитки сдвигает картинку — углы берутся после него.
		/// </summary>
		private static void AddCovered(HashSet<Vector2Int> cells, TilemapModel tile, int x, int y)
		{
			if (tile.sprite == null)
			{
				cells.Add(new Vector2Int(x, y));
				return;
			}

			Bounds bounds = tile.sprite.bounds;
			Vector3 a = tile.transform.MultiplyPoint3x4(bounds.min);
			Vector3 b = tile.transform.MultiplyPoint3x4(bounds.max);
			Vector3 c = tile.transform.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.max.y, 0f));
			Vector3 d = tile.transform.MultiplyPoint3x4(new Vector3(bounds.max.x, bounds.min.y, 0f));
			Vector3 min = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
			Vector3 max = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));

			// Запас на дробную погрешность границы: картинка ровно в клетку соседнюю задевать не должна.
			const float edge = 0.01f;
			for (int dx = Mathf.FloorToInt(min.x + edge); dx < Mathf.CeilToInt(max.x - edge); dx++)
				for (int dy = Mathf.FloorToInt(min.y + edge); dy < Mathf.CeilToInt(max.y - edge); dy++)
					cells.Add(new Vector2Int(x + dx, y + dy));
		}

		/// <summary>
		/// Собирает матрицу преобразования тайла по Tiled-флагам.
		///
		/// Спрайты тайлов имеют pivot (0,0) (см. конвенцию кеша тайлов в TileCacheService),
		/// один тайл = 1 unit. Все преобразования применяются вокруг центра ячейки (0.5, 0.5).
		///
		/// Сводная таблица для квадратной карты (Tiled tmx-spec):
		///   D H V → rotZ(deg) scaleX scaleY
		///   0 0 0 →   0       +1     +1
		///   0 1 0 →   0       -1     +1   (отражение по X)
		///   0 0 1 →   0       +1     -1   (отражение по Y)
		///   0 1 1 → 180       +1     +1   (= rot 180)
		///   1 1 0 →  90       +1     +1   (rotate 90° против часовой в Unity-смысле)
		///   1 1 1 →  90       +1     -1
		///   1 0 1 → 270       +1     +1
		///   1 0 0 → 270       +1     -1
		///
		/// rotHex120 — для hex-карт; на квадратных не приходит, но обрабатываем как Z-поворот на 120°.
		/// </summary>
		private static Matrix4x4 BuildTileMatrix(bool flipH, bool flipV, bool flipD, bool rotHex120)
		{
			if (!flipH && !flipV && !flipD && !rotHex120)
				return Matrix4x4.identity;

			float rotZ = 0f;
			float sx = 1f, sy = 1f;

			if (!flipD)
			{
				if (flipH && flipV) { rotZ = 180f; }
				else if (flipH)     { sx = -1f; }
				else if (flipV)     { sy = -1f; }
			}
			else
			{
				if (flipH && flipV)      { rotZ = 90f;  sy = -1f; }
				else if (flipH)          { rotZ = 90f;  }
				else if (flipV)          { rotZ = 270f; }
				else                     { rotZ = 270f; sy = -1f; }
			}

			if (rotHex120) rotZ += 120f;

			Quaternion rot = Quaternion.Euler(0f, 0f, rotZ);
			Vector3 scale  = new Vector3(sx, sy, 1f);

			// Поворот/масштаб вокруг центра ячейки (0.5, 0.5).
			// Итог: T(c) * R * S * T(-c)
			Vector3 c = new Vector3(0.5f, 0.5f, 0f);
			Matrix4x4 trs = Matrix4x4.TRS(Vector3.zero, rot, scale);
			Vector3 offset = c - trs.MultiplyPoint3x4(c);
			return Matrix4x4.TRS(offset, rot, scale);
		}
	}
}
