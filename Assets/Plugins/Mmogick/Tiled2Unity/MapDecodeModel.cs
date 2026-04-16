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
						var m = newTile.transform;
						m.SetTRS(new Vector3(tile.Value.horizontal - 0.5f, tile.Value.vertical - 0.5f), Quaternion.Euler(tile.Value.vertical * 180, tile.Value.horizontal * 180, 0f), Vector3.one);
						newTile.transform = m;

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

						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();
						var m = newTile.transform;
						m.SetTRS(new Vector3(obj.horizontal - 0.5f, obj.vertical - 0.5f), Quaternion.Euler(obj.vertical * 180, obj.horizontal * 180, 0f), Vector3.one);
						newTile.transform = m;

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
	}
}
