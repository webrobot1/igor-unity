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

		// Инвентарь — ПРИВАТНЫЙ компонент: сервер шлёт его только своему игроку, чужой сущности он не
		// приходит никогда. Содержимое трупа-контейнера живёт в отдельном публичном компоненте
		// (EnemyComponentsRecive.loot), а не здесь.
		//   null      — поля нет в пакете (no-op);
		//   позиция → предмет либо null (пустая позиция). Дельта частична — приходят только изменившиеся.
		public Dictionary<int, ItemSlotRecive>? inventory = null;

		// Контракт сервера (компонент equip, тип object → словарь):
		//   null            — поле отсутствует в пакете, no-op (экипировку не трогать);
		//   Count==0        — full-clear (сервер шлёт пустой словарь {}, Newtonsoft читает нативно);
		//   Count>0         — per-key delta (null значение = unequip slot, int = inventory_idx).
		public Dictionary<string, int?>? equip = null;
	}
}