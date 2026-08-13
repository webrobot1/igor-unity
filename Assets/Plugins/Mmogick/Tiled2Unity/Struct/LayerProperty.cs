namespace Mmogick
{
	[System.Serializable]
	public class LayerProperty
	{
		public string name;
		public string value;
		public string type = "string";

		// Пользовательский тип свойства Tiled (имя типа из его набора типов); пусто — тип стандартный.
		public string propertytype;
	}
}
