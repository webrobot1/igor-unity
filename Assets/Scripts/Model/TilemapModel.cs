using System.Collections.Generic;

namespace UnityEngine.Tilemaps
{
    public class TilemapModel : Tile
    {
        private List<Sprite> sprites = new List<Sprite> { };
        protected TilemapModel() { }

        // максимальная скорость
        private int speed = 1000;

        public override bool GetTileAnimationData(Vector3Int location, ITilemap tileMap, ref TileAnimationData tileAnimationData)
        {
            if (sprites != null)
            {
                tileAnimationData.animatedSprites = sprites.ToArray();
                tileAnimationData.animationSpeed = speed;
                tileAnimationData.animationStartTime = 0;
                return true;
            }
            return false;
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);
        }

        public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        {
            base.RefreshTile(position, tilemap);
        }

        public void addSprites(TilesetTileAnimation[] animations)
        {
            foreach (TilesetTileAnimation anim in animations)
            {
                for (int i = 0; i < anim.duration;  i++)
                {
                    this.sprites.Add(anim.sprite);
                }   
            }
        }
    }
}
