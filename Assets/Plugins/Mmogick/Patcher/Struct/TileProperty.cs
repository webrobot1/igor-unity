using System;
using Newtonsoft.Json.Linq;

namespace Mmogick
{
    [Serializable]
    public class TileProperty
    {
        public string name;

        // Значение приходит формой своего type: bool — логическим, int и float — числом, прочие — строкой.
        public JToken value;
        public string type;
        public string propertytype;
    }
}