#nullable enable

namespace Mmogick
{
	// Открытие контейнера (труп/сундук): action=open, key — сущность-контейнер. Сервер сам ведёт
	// игрока к цели и отвечает world-дельтой самой сущности (её components.inventory, адресно) —
	// реакция в LootWindowController.UpdateObject. Перенос/перестановка предметов — группа
	// ui/inventory (InventoryResponse: take/put/index с key), не эта команда.
	public class LootResponse : Response
	{
		public string? key = null;

		public override string group
		{
			get { return "ui/loot"; }
		}
	}
}
