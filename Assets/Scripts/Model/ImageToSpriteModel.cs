using UnityEngine;
using System.IO;
using System;

public static class ImageToSpriteModel 
{

    //Static class instead of _instance
    // Usage from any other script:
    // MySprite = ImageToSpriteModel.LoadNewSprite(FilePath, [PixelsPerUnit (optional)], [spriteType(optional)])

    public static Sprite LoadNewSprite(string FilePath, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.FullRect)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        if (!File.Exists(FilePath))
            throw new Exception("Не удается найти указанный путь к картинке " + FilePath);

        byte[] FileData = File.ReadAllBytes(FilePath);
        Texture2D SpriteTexture = LoadTexture(FileData);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);

        return NewSprite;
    }

    public static Texture2D LoadTexture(byte[] imageBytes, string transparent = null)
    {
        Texture2D texture = new Texture2D(2, 2); //, TextureFormat.RGBA32, false
        texture.filterMode = FilterMode.Point;
        texture.LoadImage(imageBytes);


        // прозрачность - взято с форума https://forum.unity.com/threads/solved-create-a-texture-with-a-png-at-runtime-how-to-make-it-transparent.511818/
        if (transparent!=null) 
        {
            Color color;
            ColorUtility.TryParseHtmlString("#"+transparent, out color);

            Debug.Log(color);

            Color[] pix = texture.GetPixels();       // get pixel colors
            for (int i = 0; i < pix.Length; i++)
            {
              if(color == pix[i]){
                pix[i].a = 0;
              }
                //pix[i].a = pix[i].grayscale;         // set the alpha of each pixel to the grayscale value
            }
            texture.SetPixels(pix);                  // set changed pixel alphas
            texture.Apply();                         // upload texture to GPU
        }

        return texture;
    }
}