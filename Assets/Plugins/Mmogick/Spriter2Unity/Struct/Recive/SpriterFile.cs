using System;
using UnityEngine;

namespace Mmogick
{
    public class SpriterFile
    {
        public int folder_id;
        public int id;
        public string resource;

        public Sprite Sprite
        {
            get
            {
                var decode = System.Convert.FromBase64String(resource);
                if (decode.Length == 0)
                    throw new Exception("Изображение " + id + " имеет размер картинки 0");

                Texture2D texture = new Texture2D(2, 2); //, TextureFormat.RGBA32, false
                texture.filterMode = FilterMode.Point;
                texture.LoadImage(decode);

                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0, 0));
            }
        }
    }
}