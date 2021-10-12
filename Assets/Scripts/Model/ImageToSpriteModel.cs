using UnityEngine;
using System.IO;


public static class ImageToSpriteModel 
{

    //Static class instead of _instance
    // Usage from any other script:
    // MySprite = ImageToSpriteModel.LoadNewSprite(FilePath, [PixelsPerUnit (optional)], [spriteType(optional)])

    public static Sprite Base64ToSprite(string base64, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.FullRect)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        byte[] imageBytes = System.Convert.FromBase64String(base64);
        Texture2D SpriteTexture = new Texture2D(2, 2);
        SpriteTexture.filterMode = FilterMode.Point;
        SpriteTexture.LoadImage(imageBytes);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);

        return NewSprite;
    }    
    
    public static Sprite LoadNewSprite(string FilePath, float PixelsPerUnit = 100.0f, SpriteMeshType spriteType = SpriteMeshType.FullRect)
    {
        // Load a PNG or JPG image from disk to a Texture2D, assign this texture to a new sprite and return its reference
        Texture2D SpriteTexture = LoadTexture(FilePath);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), PixelsPerUnit, 0, spriteType);

        return NewSprite;
    }

    public static  Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);             // Create new "empty" texture
            Tex2D.filterMode = FilterMode.Point;
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                        // If data = readable -> return texture
        }
        return null;                                 // Return null if load failed
    }
}