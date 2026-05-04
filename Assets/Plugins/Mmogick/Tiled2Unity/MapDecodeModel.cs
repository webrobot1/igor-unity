using System;
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
		public static MapDecode generate(string json, Transform grid, int gameId)
		{
			Map map = JsonConvert.DeserializeObject<Map>(json);

			// Коллайдеры вычисляем ДО пересчёта координат объектов (obj.x/y ещё в пикселях)
			HashSet<Vector2Int> colliders = new HashSet<Vector2Int>();
			foreach (Layer layer in map.layer.Values)
			{
				if (layer.tiles != null)
				{
					foreach (var tile in layer.tiles)
					{
						string sha = tile.Value.sha256;
						var tileMeta = TileCacheService.GetMeta(sha);
						bool hasObjectgroup = tileMeta != null
							&& tileMeta.group != null
							&& tileMeta.group.Length > 0;

						if (hasObjectgroup || layer.name.ToLower() == "collision")
						{
							int cx = tile.Key % map.width;
							int cy = tile.Key / map.width;
							if (cy != 0) cy *= -1;
							colliders.Add(new Vector2Int(cx, cy));
						}
					}
				}

				if (layer.objects != null && layer.visible)
				{
					foreach (LayerObject obj in layer.objects)
					{
						if (!obj.visible || obj.polyline != null) continue;

						float objX = obj.x + layer.offsetx;
						float objY = obj.y + layer.offsety;

						if (obj.polygon != null)
						{
							foreach (Point c in obj.polygon)
							{
								int tx = (int)((objX + c.x) / map.tilewidth);
								int ty = (int)((objY + c.y) / map.tileheight);
								colliders.Add(new Vector2Int(tx, ty));
							}
						}
						else
						{
							float w = obj.width > 0 ? obj.width : map.tilewidth;
							float h = obj.height > 0 ? obj.height : map.tileheight;
							if ((int)w == map.tilewidth && (int)h == map.tileheight)
							{
								colliders.Add(new Vector2Int(
									(int)(objX / map.tilewidth),
									(int)(objY / map.tileheight)));
							}
						}
					}
				}
			}

			// Пересчёт позиционных полей из ключа словаря (pos = y*width + x)
			foreach (Layer layer in map.layer.Values)
			{
				if (layer.tiles != null)
				{
					foreach (var tile in layer.tiles)
					{
						tile.Value.x = tile.Key % map.width;
						tile.Value.y = (tile.Key / map.width) * -1;
					}
				}
				if (layer.objects != null)
				{
					foreach (LayerObject obj in layer.objects)
					{
						if (!string.IsNullOrEmpty(obj.sha256))
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

				if (layer.tiles != null)
				{
					foreach (KeyValuePair<int, LayerTile> tile in layer.tiles)
					{
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();
						newTile.transform = BuildTileMatrix(tile.Value.flipH, tile.Value.flipV, tile.Value.flipD, tile.Value.rotHex120);

						applySprite(newTile, gameId, tile.Value.sha256);
						tilemap.SetTile(new Vector3Int(tile.Value.x, tile.Value.y, 0), newTile);
					}
					Debug.Log("Карта: у слоя " + newLayer.name + " раставлены " + layer.tiles.Count + " тайлов");
				}

				if (layer.objects != null)
				{
					foreach (LayerObject obj in layer.objects)
					{
						if (string.IsNullOrEmpty(obj.sha256)) continue;

						// Сервер кодирует flip-маску в суффиксе "sha:hex" — формат идентичен LayerTile.
						// См. AbstractObject::jsonSerialize в PHP и TileFlagParser.
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

						applySprite(newTile, gameId, obj.sha256);
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

				if (layer.isSpawn == 1)
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
				debugCollision.SetActive(false);

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

		// Назначает TilemapModel либо одиночный Sprite (нет анимации), либо массив фреймов (есть).
		private static void applySprite(TilemapModel newTile, int gameId, string sha256)
		{
			var meta = TileCacheService.GetMeta(sha256);
			if (meta != null && meta.frame != null && meta.frame.Length > 0)
			{
				var frames = new TileAnimation[meta.frame.Length];
				for (int i = 0; i < meta.frame.Length; i++)
				{
					frames[i] = new TileAnimation
					{
						sha256   = meta.frame[i].sha256,
						duration = meta.frame[i].duration,
						sprite   = TileCacheService.GetSprite(gameId, meta.frame[i].sha256),
					};
				}
				newTile.addSprites(frames);
			}
			else
			{
				newTile.sprite = TileCacheService.GetSprite(gameId, sha256);
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
