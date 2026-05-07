namespace Mmogick
{
    [System.Serializable]
    public class TilesetMeta
    {
        public string name;
        public TileProperty[] property;
        public System.Collections.Generic.Dictionary<string, Tile> tile;
    }
}
