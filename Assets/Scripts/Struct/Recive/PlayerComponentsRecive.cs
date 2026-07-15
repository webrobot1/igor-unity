using System.Collections.Generic;
using Newtonsoft.Json;

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

		// Контракт сервера (см. base/components/equip.yaml):
		//   null            — поле отсутствует в пакете, no-op (экипировку не трогать);
		//   Count==0        — full-clear (сервер прислал JSON `[]`, конвертер маппит в пустой Dictionary);
		//   Count>0         — per-key delta (null значение = unequip slot, int = inventory_idx).
		// EmptyArrayAsDictionaryConverter нужен потому что стандартный Newtonsoft на `[]` для
		// Dictionary падает с JsonSerializationException.
		[JsonConverter(typeof(EmptyArrayAsDictionaryConverter<string, int?>))]
		public Dictionary<string, int?>? equip = null;
	}
}