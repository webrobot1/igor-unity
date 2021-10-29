using UnityEngine.Scripting;
using UnityEngine.Tilemaps;
using UnityEngine.Scripting;

namespace UnityEngine.Tilemaps
{
    public class TilemapModel : Tile
    {
        public Sprite[] sprites;
        protected TilemapModel() { }

        public override bool GetTileAnimationData(Vector3Int location, ITilemap tileMap, ref TileAnimationData tileAnimationData)
        {
            if (sprites != null)
            {
                Debug.Log(sprites.Length);
                tileAnimationData.animatedSprites = sprites;
                tileAnimationData.animationSpeed = 1;
                tileAnimationData.animationStartTime = 1;
                return true;
            }
            return false;
        }
    }
}
