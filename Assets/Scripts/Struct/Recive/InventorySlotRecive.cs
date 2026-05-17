using System.Collections.Generic;

namespace Mmogick
{
	[System.Serializable]
	public class InventorySlotRecive
	{
		public string prefab;
		public int count;
		public Dictionary<string, string> components;

		public InventorySlotRecive(string prefab, int count, Dictionary<string, string> components = null)
		{
			this.prefab = prefab;
			this.count = count;
			this.components = components;
		}
	}
}
