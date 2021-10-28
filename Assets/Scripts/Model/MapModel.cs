using Newtonsoft.Json;
using ICSharpCode.SharpZipLib.BZip2;
using System.IO;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
    public Map decode(string base64, float PixelsPerUnit)
    {
		Map map;
		using (MemoryStream source = new MemoryStream(System.Convert.FromBase64String(base64)))
        {
            using (MemoryStream target = new MemoryStream())
            {
                BZip2.Decompress(source, target, true);
				map = JsonConvert.DeserializeObject<Map>(Encoding.UTF8.GetString(target.ToArray()));
            }
        }


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
					//new Vector2(0, 1f)  означает что на карте первый спрат с нулевой координаты рисуется вниз, но с ним появляются полосы так что оставим 0,0 и вычитыем высоту и координат yb;t
					Sprite NewSprite = Sprite.Create(texture, new Rect(x, y, tileset.Value.tilewidth + tileset.Value.margin, tileset.Value.tileheight + tileset.Value.margin), new Vector2(0, 0), PixelsPerUnit, 0, SpriteMeshType.FullRect);

					// если у нас нет в переданном массиве данного тайла (те у него нет никаких параметров смещения и он просто не передавался)
					if (tileset.Value.tile.ContainsKey(i + tileset.Value.firstgid))
					{
						tileset.Value.tile[i + tileset.Value.firstgid].sprite = NewSprite;
					}
					else
						tileset.Value.tile[i + tileset.Value.firstgid] = new TilesetTile(NewSprite);
				}
			}
		}

		// заполним слой с тайловыми координатами на сетке
		int columns = (int)Decimal.Round(map.width * map.tilewidth / map.tilewidth);
		foreach (Layer layer in map.layer)
		{
			// если есть в слое набор тайлов
			foreach (KeyValuePair<int, LayerTile> tile in layer.tiles)
			{
				// если указанный тайл (клетка) не пустая
				if (tile.Value.tile_id > 0)
				{
					tile.Value.x = tile.Key % (int)columns;
					tile.Value.y = (int)(tile.Key / columns) * -1 - 1; // что бы не снизу вверх рисовалась сетка слоя тайловой графики а снизу вверх
				}
			}

			/*foreach (LayerObject obj in layer.objects)
			{
				// если указанный тайл (клетка) не пустая
				if (obj.tile_id > 0)
				{
					
				}
			}*/
		}

		return map;
	}

#if UNITY_EDITOR
	static Sprite SaveSpriteAsAsset(Sprite sprite, string proj_path)
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
	}
#endif
}