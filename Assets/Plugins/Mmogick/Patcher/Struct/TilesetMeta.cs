using System;
using System.Collections.Generic;

namespace Mmogick
{
    [Serializable]
    public class TilesetMeta
    {
        public Dictionary<string, Tile> tile;
        public Dictionary<string, Dictionary<string, TileProperty>> tileMeta;
        public Dictionary<string, TilesetMetaEntry> tilesetMeta;
    }

    [Serializable]
    public class TilesetMetaEntry
    {
        public string name;
        public Dictionary<string, TileProperty> property;
    }
}
