using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Newtonsoft.Json;

namespace Mmogick
{
	/// <summary>
	/// Обрабатывает terrain.json (новый content-addressable формат).
	/// Графика и мета тянутся из TileCacheService (предварительно синхронизированного до входа в игру).
	/// </summary>
	abstract public class MapDecodeModel
	{
		public static HashSet<Vector2Int> Colliders { get; private set; }

		public static MapDecode generate(string json, Transform grid, int gameId)
		{
			// Канон сервера: sandbox-скаляры приходят всегда, включая null (null ≡ отсутствие ≡ дефолт).
			// Ignore не даёт Newtonsoft писать null в не-nullable поля — null оставляет дефолт поля.
			Map map = JsonConvert.DeserializeObject<Map>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

			// Grid сдвиг -0.5 совмещает визуальные границы тайлов (pivot 0,0 → [N, N+1))
			// с логическими (RoundToInt → [N-0.5, N+0.5)). Player pivot (0.5, 0.5) — центр.
			// Без сдвига игрок при позиции -8.49 (логически тайл -8) визуально в тайле -9.
			grid.localPosition = new Vector3(-0.5f, -0.5f, 0f);

			HashSet<Vector2Int> colliders = new HashSet<Vector2Int>();
			if (map.colliders != null)
			{
				foreach (var zLevel in map.colliders.Values)
				{
					foreach (string key in zLevel.Keys)
					{
						string[] parts = key.Split(',');
						colliders.Add(new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1])));
					}
				}
			}
			Colliders = colliders;

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
					foreach (LayerTile tile in tiles)
					{
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();
						newTile.transform = BuildTileMatrix(tile.flipH, tile.flipV, tile.flipD, tile.rotHex120);

						applySprite(newTile, gameId, tile.tile);
						tilemap.SetTile(new Vector3Int(tile.x, tile.y, 0), newTile);
					}
					Debug.Log("Карта: у слоя " + newLayer.name + " раставлены " + tiles.Count + " тайлов");
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

				if (layer.property != null && layer.property.ContainsKey("spawn"))
				{
					Debug.Log(layer.name + "- слой Земля");
					map.spawn_sort = sort;
				}

				if (map.spawn_sort == null)
					newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;

				sort++;
			}

			if (map.spawn_sort == null)
				map.spawn_sort = 1;

			// Отладочный слой-сетка (выключен по умолчанию, включать в инспекторе)
			GameObject debugGrid = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
			debugGrid.name = "DebugGrid";
			debugGrid.transform.SetParent(grid, false);
			debugGrid.GetComponent<TilemapRenderer>().sortingOrder = sort;
			debugGrid.SetActive(false);

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

			Tilemap debugTilemap = debugGrid.GetComponent<Tilemap>();
			for (int x = 0; x < map.width; x++)
				for (int y = 0; y < map.height; y++)
					debugTilemap.SetTile(new Vector3Int(x, -y, 0), gridTile);

			// Отладочный слой непроходимых тайлов (выключен по умолчанию, включать в инспекторе)
			if (colliders.Count > 0)
			{
				GameObject debugCollision = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
				debugCollision.name = "DebugCollision";
				debugCollision.transform.SetParent(grid, false);
				debugCollision.GetComponent<TilemapRenderer>().sortingOrder = sort + 1;
				debugCollision.SetActive(true);

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
				foreach (Vector2Int pos in colliders)
					colTilemap.SetTile(new Vector3Int(pos.x, pos.y, 0), colTile);

				Debug.Log("DebugCollision: " + colliders.Count + " непроходимых тайлов");
			}

			return new MapDecode(map);
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
