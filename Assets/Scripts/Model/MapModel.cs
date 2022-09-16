using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using ICSharpCode.SharpZipLib.BZip2;
using Newtonsoft.Json;


/// <summary>
/// Класс используется для обработки пришедших данных Карты с сервера (что могли быть урезаны и сжаты с целью экономии трафика и еще не приведены в соответсвии с Unity сеткой) 
/// </summary>
public class MapModel 
{
	private static MapModel instance;

	private MapModel()
	{ }

	public static MapModel getInstance()
	{
		if (instance == null)
			instance = new MapModel();
		return instance;
	}

	/// <summary>
	/// декодирование из Bzip2 в объект MapRecive
	/// </summary>
	public int generate(ref string base64, GameObject grid, Cinemachine.CinemachineVirtualCamera camera)
    {
		Map map = decode(ref base64);

		// далее создаем гейм обхекты

		// инициализируем новый слой 
		GameObject newLayer;

		int sort = 0;
		int? ground_sort = null;

		// расставим на сцене данные карты
		foreach (Layer layer in map.layer)
		{
			newLayer = UnityEngine.Object.Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
			newLayer.name = layer.name;
			newLayer.transform.SetParent(grid.transform, false);
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
					if (tile.Value.tile_id > 0)
					{
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

						// если tile отражен по горизонтали или вертикали или у него z параметр (нужно где слои лежить друг за другом по Y)
						if (tile.Value.horizontal > 0 || tile.Value.vertical > 0)
						{
							var m = newTile.transform;
							m.SetTRS(Vector3.zero, Quaternion.Euler(tile.Value.vertical * 180, tile.Value.horizontal * 180, 0f), Vector3.one);
							newTile.transform = m;
						}

						if (map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites != null)
						{
							newTile.sprites = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites;
						}
						else
							newTile.sprite = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprite;

						tilemap.SetTile(new Vector3Int(tile.Value.x, tile.Value.y, 0), newTile);
					}
				}
				Debug.Log(newLayer.name + " раставлены tile");
			}
			else if (layer.objects != null)
			{
				foreach (KeyValuePair<int, LayerObject> obj in layer.objects)
				{
					// если указанный тайл (клетка) не пустая
					if (obj.Value.tile_id > 0)
					{
						TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

						if (obj.Value.horizontal > 0 || obj.Value.vertical > 0)
						{
							var m = newTile.transform;
							m.SetTRS(Vector3.zero, Quaternion.Euler(obj.Value.vertical * 180, obj.Value.horizontal * 180, 0f), Vector3.one);
							newTile.transform = m;
						}

						if (map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprites != null)
						{
							newTile.sprites = map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprites;
						}
						else
							newTile.sprite = map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprite;

						// сместим координаты абсолютные на расположение главного слоя Map (у нас ноль идет от -180 для GEO расчетов) для получения относительных
						tilemap.SetTile(new Vector3Int((int)(obj.Value.x), (int)(obj.Value.y), 0), newTile);
					}
				}
			}

			// полупрозрачность слоя
			if (layer.opacity < 1f)
			{
				Renderer[] mRenderers = newLayer.GetComponentsInChildren<Renderer>();
				Debug.Log(mRenderers.Length);
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

			sort++;

			// если еще не было слоев что НЕ выше чем сам игрок (те очевидно первый такой будет - земля, а следующий - тот на котром надо генеирить игроков и npc)
			// todo - на сервере иметь параметр "Слой игрока" 
			if (ground_sort == null)
			{
				// создадим колайдер для нашей камеры (границы за которые она не смотрит) если слой земля - самый первый (врятли так можно нарисовать что он НЕ на всю карту и первый)
				if (layer.ground == 1)
				{
					Debug.Log(layer.name + "- слой Земля");
					newLayer.AddComponent<TilemapCollider2D>().usedByComposite = true;
					newLayer.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
					CompositeCollider2D colider = newLayer.AddComponent<CompositeCollider2D>();
					colider.geometryType = CompositeCollider2D.GeometryType.Polygons;
					camera.GetComponent<Cinemachine.CinemachineConfiner>().m_BoundingShape2D = colider;

					// землю нет нужды индивидуально просчитывать положения тайлов (тк мы за них не заходим и выше по слою)
					newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;

					//  текущий слой на котором будем ставить игроков		
					ground_sort = sort;
				}
			}
		}

		// на случай если в админке не указан слой - земля
		if (ground_sort == null)
			ground_sort = 1;

		return (int)ground_sort;
	}

	private Map decode(ref string base64)
    {
		Map map;
		using (MemoryStream source = new MemoryStream(System.Convert.FromBase64String(base64)))
		{
			using (MemoryStream target = new MemoryStream())
			{
				Debug.Log("Декодируем карту");
				BZip2.Decompress(source, target, true);

				Debug.Log("Парсим карту");
				map = JsonConvert.DeserializeObject<Map>(Encoding.UTF8.GetString(target.ToArray()));
			}
		}


		// флаг стоит ли проверять повтрно набор Tileset на анмиации
		bool animationCheck = false;

		// порежим изображение на плитку (тайлы)
		foreach (KeyValuePair<int, Tileset> tileset in map.tileset)
		{
			// если у набора тайлов есть картинка
			if (tileset.Value.resource != "")
			{
				// зайгрузим байты картинки в объект Texture
				Texture2D texture = ImageToSpriteModel.LoadTexture(System.Convert.FromBase64String(tileset.Value.resource), tileset.Value.trans);

				for (int i = 0; i < tileset.Value.tilecount; i++)
				{
					// посчитаем где находится необходимая область тайла
					float x = (i % tileset.Value.columns * (tileset.Value.tilewidth + tileset.Value.spacing)) + tileset.Value.margin;

					// что бы не снизу вверх брал отрезки (тайлы) а сверху вниз? при этом не менять рендеринг Vector2(0,0) в NewSprite (из за смены оторого появляются полоски)
					float y = ((tileset.Value.tilecount - i - 1) / tileset.Value.columns) * (tileset.Value.tileheight + tileset.Value.spacing) + tileset.Value.margin;

					// вырежем необходимую область
					// для манипуляции с поворотами и отражением спрайта (что бы спрайт не сьезжал в сторону при этом) делаем точку опоры спрайта в центре Vector2(0.5f, 0.5f)
					Sprite NewSprite = Sprite.Create(texture, new Rect(x - tileset.Value.margin, y - tileset.Value.margin, tileset.Value.tilewidth + tileset.Value.margin, tileset.Value.tileheight + tileset.Value.margin), new Vector2(0.5f, 0.5f), map.tilewidth, 0, SpriteMeshType.FullRect);

					// если у нас нет в переданном массиве данного тайла (те у него нет никаких параметров смещения и он просто не передавался)
					if (tileset.Value.tile.ContainsKey(i + tileset.Value.firstgid))
					{
						tileset.Value.tile[i + tileset.Value.firstgid].sprite = NewSprite;
						if (!animationCheck && tileset.Value.tile[i + tileset.Value.firstgid].frame != null)
							animationCheck = true;
					}
					else
						tileset.Value.tile[i + tileset.Value.firstgid] = new TilesetTile(NewSprite);
				}


				// теперь когда мы заполнили спрайтами весь набор Tileset пройдем еще раз тк может быть в нем анимация
				if (animationCheck)
				{
					foreach (KeyValuePair<int, TilesetTile> tile in tileset.Value.tile)
					{
						if (tile.Value.frame != null)
						{
							tile.Value.sprites = new Sprite[tile.Value.frame.Length];
							for (int i = 0; i < tile.Value.frame.Length; i++)
							{
								tile.Value.sprites[i] = tileset.Value.tile[tile.Value.frame[i].tileid].sprite;
							}
						}
					}
				}
			}
		}

		// заполним слой с тайловыми координатами на сетке
		int columns = (int)Decimal.Round(map.width * map.tilewidth / map.tilewidth);
		foreach (Layer layer in map.layer)
		{
			if (layer.tiles != null)
			{
				// если есть в слое набор тайлов
				foreach (KeyValuePair<int, LayerTile> tile in layer.tiles)
				{
					// если указанный тайл (клетка) не пустая
					if (tile.Value.tile_id > 0)
					{
						tile.Value.x = tile.Key % columns;
						tile.Value.y = (int)(tile.Key / columns) * -1 - 1; // что бы не снизу вверх рисовалась сетка слоя тайловой графики а снизу вверх
					}
				}
			}

			if (layer.objects != null)
			{
				foreach (KeyValuePair<int, LayerObject> obj in layer.objects)
				{
					// если указанный тайл (клетка) не пустая
					if (obj.Value.tile_id > 0)
					{
						obj.Value.x = obj.Value.x / map.tileheight;
						obj.Value.y = obj.Value.y / map.tilewidth * -1;
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