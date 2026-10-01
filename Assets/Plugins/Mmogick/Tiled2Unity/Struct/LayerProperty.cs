using System;
using Newtonsoft.Json.Linq;

namespace Mmogick
{
	[Serializable]
	public class LayerProperty
	{
		public string name;

		// Значение приходит формой своего type: bool — логическим, int и float — числом, прочие — строкой.
		public JToken value;
		public string type = "string";

		// Пользовательский тип свойства Tiled (имя типа из его набора типов); пусто — тип стандартный.
		public string propertytype;
	}
}
