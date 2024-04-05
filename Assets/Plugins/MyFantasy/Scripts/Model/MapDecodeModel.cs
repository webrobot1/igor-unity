using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using ICSharpCode.SharpZipLib.BZip2;
using Newtonsoft.Json;
using System.IO.Compression;

namespace MyFantasy
{
	/// <summary>
	/// Класс используется для обработки пришедших данных Карты с сервера (что могли быть урезаны и сжаты с целью экономии трафика и еще не приведены в соответсвии с Unity сеткой) 
	/// </summary>
	abstract public class MapDecodeModel 
	{
	
		/// <summary>
		/// декодирование из Bzip2 в объект MapRecive
		/// </summary>
		public static MapDecode generate(string base64, Transform grid)
		{
			Map map = decode(ref base64);

			// инициализируем новый слой 
			GameObject newLayer;

			int sort = 0;

			// расставим на сцене данные карты
			foreach (Layer layer in map.layer)
			{
				newLayer = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
				newLayer.name = layer.name;
				newLayer.transform.SetParent(grid, false);
				newLayer.GetComponent<TilemapRenderer>().sortingOrder = sort;

				if (layer.visible == 0)
				{
					newLayer.SetActive(false);
					Debug.Log(layer.name + "- слой скрыт");
				}

				Tilemap tilemap = newLayer.GetComponent<Tilemap>();

				// если есть в слое набор тайлов
				if (layer.tiles != null)
				{
					foreach (KeyValuePair<int, LayerTile> tile in layer.tiles)
					{
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

						// если tile отражен по горизонтали или вертикали или у него z параметр (нужно где слои лежить друг за другом по Y)
						//+ что бы тайлы были в квадратиках Unity (а то из за того что в Tiled слева с угла идет тайл в Unity он будто всегда выше выбранного в редакторе кадрата), но это не принципиально

						var m = newTile.transform;

						// повернем как нам нужно приэтом сместим назад тайл что бы съемулировать Vector3 будто он на месте остался хоть и повернут (как в программе Tiled)
						m.SetTRS(new Vector3(tile.Value.horizontal -0.5f, tile.Value.vertical - 0.5f), Quaternion.Euler(tile.Value.vertical * 180, tile.Value.horizontal * 180, 0f), Vector3.one);
						newTile.transform = m;

                        if (!map.tileset[tile.Value.tileset_image].tile.ContainsKey(tile.Value.tile_id))
                        {
							throw new Exception("Не найден тайл "+ tile.Value.tile_id+" в набооре тайлов "+ tile.Value.tileset_image + " размером "+ map.tileset[tile.Value.tileset_image].tilecount);
                        }
                        else 
						{ 
							if (map.tileset[tile.Value.tileset_image].tile[tile.Value.tile_id].frame != null)
							{
								Debug.Log("Карта: добавим на сцену анимированный тайл " + tile.Value.tile_id);
								newTile.addSprites(map.tileset[tile.Value.tileset_image].tile[tile.Value.tile_id].frame);
							}
							else
								newTile.sprite = map.tileset[tile.Value.tileset_image].tile[tile.Value.tile_id].sprite;

							tilemap.SetTile(new Vector3Int(tile.Value.x, tile.Value.y, 0), newTile);
						}
					}
					Debug.Log("Карта: у слоя "+newLayer.name + " раставлены "+ layer.tiles.Count + " тайлов");
				}
				
				if (layer.objects != null)
				{
					foreach (LayerObject obj in layer.objects)
					{
						// если указанный тайл (клетка) не пустая
						if (obj.tile_id > 0)
						{
							TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

							// чисто что бы если повернуть и что бы тайлы были в квадратиках Unity (а то из за того что в Tiled слева с угла идет тайл в Unity он будто всегда выше выбранного в редакторе кадрата)
							var m = newTile.transform;
							m.SetTRS(new Vector3(obj.horizontal - 0.5f, obj.vertical - 0.5f), Quaternion.Euler(obj.vertical * 180, obj.horizontal * 180, 0f), Vector3.one);
							newTile.transform = m;

							if (map.tileset[obj.tileset_image].tile[obj.tile_id].frame != null)
							{
								newTile.addSprites(map.tileset[obj.tileset_image].tile[obj.tile_id].frame);
							}
							else
								newTile.sprite = map.tileset[obj.tileset_image].tile[obj.tile_id].sprite;

							// сместим координаты абсолютные на расположение главного слоя Map (у нас ноль идет от -180 для GEO расчетов) для получения относительных
							tilemap.SetTile(new Vector3Int((int)(obj.x), (int)(obj.y), 0), newTile);
						}
					}
				}

				// полупрозрачность слоя
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

				//  текущий слой на котором будем ставить игроков	
				if (layer.isSpawn == 1)
                {
					Debug.Log(layer.name + "- слой Земля"); 
					map.spawn_sort = sort;
				}	
	
				// землю нет нужды индивидуально просчитывать положения тайлов (тк мы за них не заходим и выше по слою)
				if (map.spawn_sort == null)
					newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;

				sort++;
			}

			// на случай если в админке не указан слой - спавна игрока
			if (map.spawn_sort == null)
				map.spawn_sort = 1;

			return new MapDecode(map);
		}

		private static Map decode(ref string base64)
		{
			Map map;
			using (MemoryStream source = new MemoryStream(System.Convert.FromBase64String(base64)))
			{
				using (MemoryStream target = new MemoryStream())
				{
					Debug.Log("Декодируем карту");
					using (var decompressStream = new GZipStream(source, CompressionMode.Decompress))
					{
						decompressStream.CopyTo(target);
					}

					Debug.Log("Парсим карту");
					map = JsonConvert.DeserializeObject<Map>(Encoding.UTF8.GetString(target.ToArray()));
				}
			}

			// флаг стоил ли обработать анимации 
			bool animationCheck = false;

			// порежим изображение на плитку (тайлы)
			foreach (KeyValuePair<string, Tileset> tileset in map.tileset)
			{
				// зайгрузим байты картинки в объект Texture

				var decode = System.Convert.FromBase64String(tileset.Value.resource);
				if (decode.Length == 0)
					throw new Exception("Изображение "+tileset.Key+" имеет размер картинки 0");

				Texture2D texture = ImageToSpriteModel.LoadTexture(decode, tileset.Value.trans);
				for (int i = 0; i < tileset.Value.tilecount; i++)
				{
					// посчитаем где находится необходимая область тайла
					int x = (i % tileset.Value.columns * (tileset.Value.tilewidth + tileset.Value.spacing)) + tileset.Value.margin;

					// что бы не снизу вверх брал отрезки (тайлы) а сверху вниз? при этом не менять рендеринг Vector2(0,0) в NewSprite (из за смены оторого появляются полоски)
					int y = ((tileset.Value.tilecount - i - 1) / tileset.Value.columns) * (tileset.Value.tileheight + tileset.Value.spacing) + tileset.Value.margin;

					// вырежем необходимую область
					//  программе tiled точка опоры НЕ в центре а с угла (какого можно глянуть, забыл) но если менять на 0.0 часть тайлов в unity пропадают часть куда то смещаютя
					// что бы это работала везде в этом классе где SetTRS есть (смещение) после поворота по горизонтали или вертикали смещаем таил назад (тк в Tiled он на месте остается если отражается)
					Sprite NewSprite = Sprite.Create(texture, new Rect(x - tileset.Value.margin, y - tileset.Value.margin, tileset.Value.tilewidth + tileset.Value.margin, tileset.Value.tileheight + tileset.Value.margin), Vector2.zero, map.tilewidth, 0, SpriteMeshType.FullRect);

					// если у нас нет в переданном массиве данного тайла (те у него нет никаких параметров смещения и доп свойств и он просто не передавался)
					if (tileset.Value.tile.ContainsKey(i))
					{
						tileset.Value.tile[i].sprite = NewSprite;
						if (!animationCheck && tileset.Value.tile[i].frame != null)
							animationCheck = true;
					}
					else
						tileset.Value.tile[i] = new TilesetTile(NewSprite);
				}


				// теперь когда мы заполнили спрайтами весь набор Tileset пройдем еще раз тк может быть в нем анимация
				if (animationCheck)
				{
					foreach (KeyValuePair<int, TilesetTile> tile in tileset.Value.tile)
					{
						if (tile.Value.frame != null)
						{
							foreach (TilesetTileAnimation frame in tile.Value.frame)
							{
								Debug.Log("Карта: создаем анмаиции для тайла " + tile.Key + " с тайлом "+ frame.tileid + " с паузами "+ frame.duration);
								if(tileset.Value.tile.ContainsKey(frame.tileid))
									frame.sprite = tileset.Value.tile[frame.tileid].sprite;
                                else
                                {
									throw new Exception("Не найден тайл под номером "+ frame.tileid+" в наборе tileset "+ tileset.Value.image+" ("+tileset.Key+")");
                                }
							}
						}
					}
				}
			}

			// заполним слой с тайловыми координатами на сетке
			foreach (Layer layer in map.layer)
			{
				if (layer.tiles != null)
				{
					// если есть в слое набор тайлов
					foreach (KeyValuePair<int, LayerTile>tile in layer.tiles)
					{
						// если указанный тайл (клетка) не пустая
						tile.Value.x = tile.Key % map.columns;
						tile.Value.y = (tile.Key / map.columns) * -1; // что бы не снизу вверх рисовалась сетка слоя тайловой графики а снизу вверх
					}
				}

				if (layer.objects != null)
				{
					foreach (LayerObject obj in layer.objects)
					{
						// если указанный тайл (клетка) не пустая
						if (obj.tile_id > 0)
						{
							obj.x = obj.x / map.tilewidth;
							obj.y = obj.y / map.tileheight;
						}
					}
				}
			}

			return map;
		}


		// todo посмотреть a AssetBundels (+ пришедшее на смену его Adressable)  , что работает без UNITY_EDITOR и служит для упаковки загруженных данных
	#if UNITY_EDITOR
	/*	static Sprite SaveSpriteAsAsset(Sprite sprite, string proj_path)
		{
			var abs_path = Path.Combine(Application.dataPath, proj_path);
			Debug.Log(abs_path);
			proj_path = Path.Combine("Assets", proj_path);

			Directory.CreateDirectory(Path.GetDirectoryName(abs_path));
			File.WriteAllBytes(abs_path, ImageConversion.EncodeToPNG(sprite.texture));

			AssetDatabase.Refresh();

			var ti = AssetImporter.GetAtPath(proj_path) as TextureImporter;
			ti.spritePixelsPerUnit = sprite.pixelsPerUnit;
			ti.mipmapEnabled = false;
			ti.textureType = TextureImporterType.Sprite;

			EditorUtility.SetDirty(ti);
			ti.SaveAndReimport();

			return AssetDatabase.LoadAssetAtPath<Sprite>(proj_path);
		}*/
	#endif
	}
}