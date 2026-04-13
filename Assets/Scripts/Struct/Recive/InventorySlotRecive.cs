using System.Collections.Generic;

namespace Mmogick
{
	[System.Serializable]
	public class InventorySlotRecive
	{
		public string id;
		public int count;
		public Dictionary<string, string> components;

		public InventorySlotRecive(string id, int count, Dictionary<string, string> components = null)
		{
			this.id = id;
			this.count = count;
			this.components = components;
		}
	}
}
