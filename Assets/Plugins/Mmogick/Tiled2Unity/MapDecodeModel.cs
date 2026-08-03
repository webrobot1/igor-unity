using System.Collections.Generic;
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
		/// Данные карты, из которых строятся отладочные слои: сама карта, её непроходимые клетки и слой
		/// сортировки. Хранятся до включения галочки — сами слои создаются только тогда (см. EnsureDebugLayer).
		/// Ключ — корень карты на сцене; уничтоженные карты отсеиваются при обращении.
		/// </summary>
		private class DebugSource
		{
			public Map map;
			public HashSet<Vector2Int> colliders;
			public int sort;
		}

		private static readonly Dictionary<Transform, DebugSource> debugSources = new Dictionary<Transform, DebugSource>();

		private static readonly Dictionary<string, TilemapModel> tileAssets = new Dictionary<string, TilemapModel>();
		private static int tileAssetsGame;

		/// <summary>
		/// Плитка для клетки: из набора, а при первом появлении — создаётся и в набор кладётся.
		/// Ключ — картинка вместе с флагами разворота: одна и та же картинка, повёрнутая иначе, это другая плитка.
		/// </summary>
		private static TilemapModel getTileAsset(int gameId, LayerTile tile)
		{
			if (tileAssetsGame != gameId)
			{
				tileAssets.Clear();
				tileAssetsGame = gameId;
			}

			string key = tile.tile
				+ (tile.flipH ? "H" : "")
				+ (tile.flipV ? "V" : "")
				+ (tile.flipD ? "D" : "")
				+ (tile.rotHex120 ? "R" : "");

			if (tileAssets.TryGetValue(key, out TilemapModel known) && known != null)
				return known;

			TilemapModel created = TilemapModel.CreateInstance<TilemapModel>();
			created.transform = BuildTileMatrix(tile.flipH, tile.flipV, tile.flipD, tile.rotHex120);
			applySprite(created, gameId, tile.tile);

			tileAssets[key] = created;
			return created;
		}

		public static MapDecode generate(string json, Transform grid, int gameId)
		{
			// Канон сервера: sandbox-скаляры приходят всегда, включая null (null ≡ отсутствие ≡ дефолт).
			// Ignore не даёт Newtonsoft писать null в не-nullable поля — null оставляет дефолт поля.
			Map map = JsonConvert.DeserializeObject<Map>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

			// grid.localPosition здесь НЕ трогаем: MapController.SortMap выставляет его сразу после generate
			// (позиция карты в открытом мире + TILE_OFFSET). Прежняя установка -0.5 тут была мёртвой (затиралась).

			// Преграды приходят прямоугольниками (см. Map.colliders) — разворачиваем в клетки: проверка шага
			// спрашивает конкретную клетку, а z-уровни схлопнуты (клиентская проверка плоская).
			HashSet<Vector2Int> colliders = new HashSet<Vector2Int>();
			if (map.colliders != null)
			{
				foreach (var zLevel in map.colliders.Values)
				{
					foreach (int[] rect in zLevel)
					{
						for (int row = 0; row < rect[3]; row++)
						{
							for (int col = 0; col < rect[2]; col++)
								colliders.Add(new Vector2Int(rect[0] + col, rect[1] - row));
						}
					}
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

			// Слой-земля (граница спавна игроков) задаётся на уровне КАРТЫ свойством spawn = имя слоя,
			// один на карту (прежде — property spawn на самом слое, где в Tiled легко задвоить на двух слоях).
			// Его 0-based индекс при итерации ниже → spawn_sort (сортировка игрока + граница Chunk-режима).
			string spawnLayerName = null;
			if (map.property != null && map.property.TryGetValue("spawn", out LayerProperty spawnProp))
				spawnLayerName = spawnProp.value;

			int sort = 0;

			foreach (Layer layer in map.layer.Values)
			{
				GameObject newLayer = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
				newLayer.name = layer.name;
				newLayer.transform.SetParent(grid, false);
				newLayer.GetComponent<TilemapRenderer>().sortingOrder = sort;

				if (!layer.visible)
				{
					newLayer.SetActive(false);
					Debug.Log(layer.name + "- слой скрыт");
				}

				Tilemap tilemap = newLayer.GetComponent<Tilemap>();

				if (!string.IsNullOrEmpty(layer.tile))
				{
					List<LayerTile> tiles = DecodeTileCsv(layer.tile, map.width);

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
						assets[i] = getTileAsset(gameId, tile);
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
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();
						Matrix4x4 trs = BuildTileMatrix(obj.flipH, obj.flipV, obj.flipD, obj.rotHex120);

						// Tiled rotation для объектов — CW в градусах вокруг точки (x,y),
						// которая совпадает с pivot спрайта (0,0) в координатах ячейки
						// (Sprite.Create с pivot=(0,0)). Знак инвертируем: Tiled CW → Unity Z CCW.
						// Поворот применяется СЛЕВА от flip-матрицы: сначала нормализуется
						// ориентация флагами (внутри ячейки), затем весь объект крутится вокруг pivot.
						if (obj.rotation != 0f)
						{
							Matrix4x4 rotMat = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -obj.rotation));
							trs = rotMat * trs;
						}

						newTile.transform = trs;

						applySprite(newTile, gameId, obj.tile);
						tilemap.SetTile(new Vector3Int((int)obj.x, (int)obj.y, 0), newTile);
					}
				}

				if (layer.opacity < 1f)
				{
					Renderer[] mRenderers = newLayer.GetComponentsInChildren<Renderer>();
					for (int i = 0; i < mRenderers.Length; i++)
					{
						for (int j = 0; j < mRenderers[i].materials.Length; j++)
						{
							Color matColor = mRenderers[i].materials[j].color;
							matColor.a = layer.opacity;
							mRenderers[i].materials[j].color = matColor;
						}
					}
				}

				if (spawnLayerName != null && layer.name == spawnLayerName)
				{
					Debug.Log(layer.name + "- слой Земля (spawn)");
					map.spawn_sort = sort;
				}

				if (map.spawn_sort == null)
					newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;

				sort++;
			}

			if (map.spawn_sort == null)
				map.spawn_sort = 1;

			// Отладочные слои (сетка, непроходимые клетки, объекты-разметка) сразу НЕ строятся. Вместе они
			// накрывают карту трижды — у карты 140×120 это больше тридцати тысяч клеток плюс все контуры объектов, —
			// а нужны, только когда их включают галочкой в тестовом режиме. Потому здесь лишь запоминаем данные,
			// из которых слой можно построить, а строит его EnsureDebugLayer в момент включения.
			debugSources[grid] = new DebugSource { map = map, colliders = colliders, sort = sort };

			// Уже включённые слои строим сразу: карта могла прийти позже, чем игрок нажал галочку.
			if (DebugLayers.ShowGrid)
				EnsureDebugLayer(grid, DebugLayers.GRID);
			if (DebugLayers.ShowCollision)
				EnsureDebugLayer(grid, DebugLayers.COLLISION);
			if (DebugLayers.ShowObjects)
				EnsureDebugLayer(grid, DebugLayers.OBJECTS);

			MapDecode decoded = new MapDecode(map);
			decoded.colliders = colliders;   // per-map коллайдеры (не общий статик — см. MapDecode.colliders)
			return decoded;
		}


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

			if (!debugSources.TryGetValue(grid, out DebugSource src))
				return;

			if (layerName == DebugLayers.GRID)
				buildDebugGrid(grid, src);
			else if (layerName == DebugLayers.COLLISION)
				buildDebugCollision(grid, src);
			else if (layerName == DebugLayers.OBJECTS)
				buildDebugObjects(grid, src);
		}

		private static void buildDebugGrid(Transform grid, DebugSource src)
		{
			// Отладочный слой-сетка. Видимость — галочка «Сетка» debug-панели (DebugPanelController.ShowGrid),
			// применяется и к картам, загружаемым позже (см. DebugPanelController).
			GameObject debugGrid = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
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

		private static void buildDebugCollision(Transform grid, DebugSource src)
		{
			if (src.colliders.Count == 0)
				return;

			// Отладочный слой непроходимых тайлов. Видимость — галочка «Коллизии» debug-панели
			// (DebugPanelController.ShowCollision); её начальное значение = isDebug игры (прод — выкл, dev — вкл),
			// далее пользователь переключает свободно. Применяется и к картам, загружаемым позже.
				GameObject debugCollision = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
				debugCollision.name = DebugLayers.COLLISION;
				debugCollision.transform.SetParent(grid, false);
				debugCollision.GetComponent<TilemapRenderer>().sortingOrder = src.sort + 1;
				debugCollision.SetActive(DebugLayers.ShowCollision);

				Texture2D colTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
				colTex.filterMode = FilterMode.Point;
				Color32 fill = new Color32(255, 50, 50, 80);
				Color32 edge = new Color32(255, 50, 50, 180);
				var colPixels = new Color32[32 * 32];
				for (int i = 0; i < colPixels.Length; i++)
				{
					int px = i % 32;
					int py = i / 32;
					colPixels[i] = (px == 0 || py == 0 || px == 31 || py == 31) ? edge : fill;
				}
				colTex.SetPixels32(colPixels);
				colTex.Apply();

				Sprite colSprite = Sprite.Create(colTex, new Rect(0, 0, 32, 32), Vector2.zero, 32);
				UnityEngine.Tilemaps.Tile colTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
				colTile.sprite = colSprite;

				Tilemap colTilemap = debugCollision.GetComponent<Tilemap>();
				Vector3Int[] colPositions = new Vector3Int[src.colliders.Count];
				TileBase[] colTiles = new TileBase[src.colliders.Count];
				int colIndex = 0;
				foreach (Vector2Int pos in src.colliders)
				{
					colPositions[colIndex] = new Vector3Int(pos.x, pos.y, 0);
					colTiles[colIndex] = colTile;
					colIndex++;
				}
				colTilemap.SetTiles(colPositions, colTiles);

				Debug.Log("DebugCollision: " + src.colliders.Count + " непроходимых тайлов");
		}

		private static void buildDebugObjects(Transform grid, DebugSource src)
		{
			// Отладочный слой объектов-разметки (зоны спавна, варпы, полигоны). Видимость — галочка
			// «Полигоны» debug-панели (DebugPanelController.ShowObjects). Рисуем формы линиями поверх карты.
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

			Material lineMat = new Material(Shader.Find("Sprites/Default"));
			int objectsCount = 0;

			foreach (Layer layer in src.map.layer.Values)
			{
				if (layer.@object == null || layer.@class == "collision")
					continue;

				foreach (LayerObject obj in layer.@object)
				{
					if (!string.IsNullOrEmpty(obj.tile))
						continue;

					DrawDebugObject(debugObjects.transform, labelCanvas.transform, labelFont, obj, src.map.tilewidth, src.map.tileheight, lineMat, src.sort + 2);
					objectsCount++;
				}
			}

			debugObjects.SetActive(DebugLayers.ShowObjects);
			Debug.Log("DebugObjects: " + objectsCount + " объектов-разметки");
		}

		// Декод CSV-строки тайлов слоя в набор LayerTile (зеркало серверного LayerTileCsvCodec::decodeCsv).
		// Формат: "легенда\nданные". До '\n' — distinct sha256 через ';'. После — CSV ячеек через ','.
		// Ячейка = индекс в легенде (1-based; 0/пусто пропускается) + опц. флаги через '|' битмаской
		// (1=flipH, 2=flipV, 4=flipD, 8=rotHex120). Позиция ячейки i = y*width+x; y инвертируется (*-1).
		private static List<LayerTile> DecodeTileCsv(string s, int width)
		{
			List<LayerTile> result = new List<LayerTile>();

			s = s.Trim();
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
				int idx = int.Parse(cellParts[0]);
				if (idx < 1 || idx > legend.Length)
				{
					Debug.LogError("CSV-слой: индекс легенды " + idx + " вне диапазона (легенда из " + legend.Length + " sha, позиция " + i + ")");
					continue;
				}

				int flags = cellParts.Length > 1 ? int.Parse(cellParts[1]) : 0;

				result.Add(new LayerTile
				{
					tile      = legend[idx - 1],
					flipH     = (flags & 1) != 0,
					flipV     = (flags & 2) != 0,
					flipD     = (flags & 4) != 0,
					rotHex120 = (flags & 8) != 0,
					x         = i % width,
					y         = (i / width) * -1,
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
		/// Координаты объекта приходят в ПИКСЕЛЯХ в серверной convention (anchor = top-left, y+ вверх, y вниз
		/// отрицателен). Перевод в клетки — деление на размер клетки; якорь (x/tw, y/th) совпадает с SetTile
		/// тайл-объектов, прямоугольник растёт от якоря вверх-вправо (+width/+height), как pivot(0,0)-спрайт
		/// тайла заполняет клетку. Формы: polygon/ellipse/rect — замкнутый контур, polyline — незамкнутый.
		/// Точки polygon/polyline — относительно якоря объекта.
		/// </summary>
		private static void DrawDebugObject(Transform parent, Transform labelParent, Font font, LayerObject obj, int tw, int th, Material mat, int sortingOrder)
		{
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
				float rx = obj.width / tw / 2f;
				float ry = obj.height / th / 2f;
				float cx = ox + rx;
				float cy = oy + ry;
				const int seg = 24;
				for (int i = 0; i < seg; i++)
				{
					float a = (float)i / seg * Mathf.PI * 2f;
					pts.Add(new Vector3(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, 0));
				}
			}
			else
			{
				float w = obj.width / tw;
				float h = obj.height / th;
				pts.Add(new Vector3(ox, oy, 0));
				pts.Add(new Vector3(ox + w, oy, 0));
				pts.Add(new Vector3(ox + w, oy + h, 0));
				pts.Add(new Vector3(ox, oy + h, 0));
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
				// Верх контура: у прямоугольника oy+h, у прочих форм — сам якорь (pts уже подняты геометрией).
				float topY = (obj.polygon == null && obj.polyline == null && !obj.ellipse) ? oy + obj.height / th : oy;
				rt.localPosition = new Vector3(ox, topY + 0.15f, 0f);
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
		/// Собирает матрицу преобразования тайла по Tiled-флагам.
		///
		/// Спрайты тайлов имеют pivot (0,0) (см. TileCacheService.Sprite.Create),
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
