
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
                tileAnimationData.animatedSprites = sprites;
                tileAnimationData.animationSpeed = 1;
                tileAnimationData.animationStartTime = 1;
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
    }
}
