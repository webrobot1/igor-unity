using UnityEngine;
using System.IO;
using System;

public static class ImageToSpriteModel 
{

    //Static class instead of _instance
    // Usage from any other script:
    // MySprite = ImageToSpriteModel.LoadNewSprite(FilePath, [PixelsPerUnit (optional)], [spriteType(optional)])

    public static Texture2D Base64ToTexture(string base64)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        byte[] imageBytes = System.Convert.FromBase64String(base64);
        return LoadTexture(imageBytes); 
    }


    public static Sprite Base64ToSprite(string base64, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.FullRect)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        Texture2D SpriteTexture = Base64ToTexture(base64);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);

        return NewSprite;
    }    
    
    public static Sprite LoadNewSprite(string FilePath, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.FullRect)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        if (!File.Exists(FilePath))
            throw new Exception("Ќе удаетс€ найти указанный путь к картинке " + FilePath);

        byte[] FileData = File.ReadAllBytes(FilePath);
        Texture2D SpriteTexture = LoadTexture(FileData);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);

        return NewSprite;
    }

    public static Texture2D LoadTexture(byte[] imageBytes)
    {
        Texture2D texture = new Texture2D(2, 2);
        texture.filterMode = FilterMode.Point;
        texture.LoadImage(imageBytes);

        return texture;
    }
}