using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - произвольыне поля
	/// </summary>
	[System.Serializable]
	public class EnemyComponentsRecive
	{
		/// <summary>
		/// если не приравнять к null - будет  0 при наличии другого лбого элемента класса
		/// </summary>
		public int? hp;
		public int? hp_max;

		public int? mp;
		public int? mp_max;

		public int? speed;

		// inventory в БАЗЕ (не только у player): контейнер (труп/сундук) — это enemy/entity,
		// и его содержимое приходит обыскивающему игроку АДРЕСНО обычной world-дельтой самой
		// сущности-контейнера (components.inventory чужого key). Без поля в базовом классе
		// Newtonsoft молча отбросил бы inventory чужого enemy. Свой player наследует поле в
		// PlayerComponentsRecive и читает его как раньше.
		//   null      — поля нет в пакете (no-op);
		//   Count==0  — контейнер пуст (сервер шлёт пустой словарь {} — компонент типа object; окно показать/закрыть);
		//   Count>0   — позиция → предмет.
		// Пустой словарь приходит {} (сервер, ComponentTypeEnum::Object), Newtonsoft читает нативно — конвертер не нужен.
		public Dictionary<int, InventorySlotRecive>? inventory = null;
	}
}
