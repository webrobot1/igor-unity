using System.Collections.Generic;

namespace Mmogick
{
    [System.Serializable]
    public class TilesetMeta
    {
        public Dictionary<string, TileProperty[]> tileMeta;
        public Dictionary<string, TilesetMetaEntry> tilesetMeta;
    }

    [System.Serializable]
    public class TilesetMetaEntry
    {
        public string name;
        public TileProperty[] property;
    }
}
