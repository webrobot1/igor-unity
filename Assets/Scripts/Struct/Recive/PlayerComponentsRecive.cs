using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - произвольыне поля
	/// </summary>
	[System.Serializable]
	public class PlayerComponentsRecive: CreatureComponentsRecive
	{
		public Dictionary<string, string>? settings = null;
		// Заклинания, выученные ЭТОЙ сущностью: ключ — префаб заклинания, значение — true. Имя поля —
		// slug серверного компонента (spell_book), а не camelCase: приходит внутри components штатным
		// разбором по именам. Само окно книги строится отсюда: чем является каждое заклинание (название,
		// описание, стоимость маны, группа команды) — свойства его префаба из каталога, отдельного
		// справочника заклинаний сервер не шлёт.
		public Dictionary<string, bool>? spell_book = null;
		public Dictionary<int, ActionBarsRecive?>? actionbars = null;

		// Инвентарь — ПРИВАТНЫЙ компонент: сервер шлёт его только своему игроку, чужой сущности он не
		// приходит никогда. Содержимое трупа-контейнера живёт в отдельном публичном компоненте
		// (CreatureComponentsRecive.loot), а не здесь.
		//   null      — поля нет в пакете (no-op);
		//   позиция → предмет либо null (пустая позиция). Дельта частична — приходят только изменившиеся.
		public Dictionary<int, ItemSlotRecive>? inventory = null;
	}
}