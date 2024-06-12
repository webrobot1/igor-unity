using UnityEngine;

public static class ImageToSpriteModel 
{
    /// <summary>
    /// Загрузка текстуры из байтов
    /// </summary>
    /// <param name="imageBytes">байты</param>
    /// <param name="transparent">HEX код прозрачности </param>
    /// <returns></returns>
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