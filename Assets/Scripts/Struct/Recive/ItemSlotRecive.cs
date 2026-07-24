using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// Одна позиция контейнера предметов. Формат общий для ЛЮБОГО контейнера сервера (инвентарь игрока,
	/// добыча трупа) — привязки к конкретному контейнеру у типа нет. null вместо объекта — позиция пуста.
	/// </summary>
	[System.Serializable]
	public class ItemSlotRecive
	{
		public string prefab;
		public int count;
		public Dictionary<string, string> components;

		public ItemSlotRecive(string prefab, int count, Dictionary<string, string> components = null)
		{
			this.prefab = prefab;
			this.count = count;
			this.components = components;
		}
	}
}
