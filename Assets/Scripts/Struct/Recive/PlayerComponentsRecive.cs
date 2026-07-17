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

		// inventory поднят в базовый EnemyComponentsRecive (контейнеры — enemy/entity). Своё
		// поле остаётся доступно как ((PlayerRecive)recive).components.inventory (наследование).

		// Контракт сервера (компонент equip, тип object → словарь):
		//   null            — поле отсутствует в пакете, no-op (экипировку не трогать);
		//   Count==0        — full-clear (сервер шлёт пустой словарь {}, Newtonsoft читает нативно);
		//   Count>0         — per-key delta (null значение = unequip slot, int = inventory_idx).
		public Dictionary<string, int?>? equip = null;
	}
}