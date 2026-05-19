using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - произвольыне поля
	/// </summary>
	[System.Serializable]
	public class PlayerComponentsRecive: EnemyComponentsRecive
	{
		public Dictionary<string, string>? settings = null;
		public Dictionary<string, bool>? spellBook = null;
		public Dictionary<int, ActionBarsRecive?>? actionbars = null;
		public Dictionary<int, InventorySlotRecive>? inventory = null;

		// Текущая экипировка игрока: slot_slug → inventory_idx. null-значение в map = слот пуст.
		// Сервер шлёт null (а не пустой объект) когда никакой экипировки нет — см. components/equip.php.
		public Dictionary<string, int?>? equip = null;
	}
}