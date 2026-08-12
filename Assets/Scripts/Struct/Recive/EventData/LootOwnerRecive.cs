namespace Mmogick
{
	/// <summary>
	/// Право на добычу трупа — данные команды <see cref="CorpseLootMarker.GROUP_LOOTFREE"/>. Приходят всем
	/// видящим труп обычной world-дельтой сущности.
	///   open — ключи игроков, которым добыча уже открыта.
	/// Круг допущенных ширится сам: по истечении ступени сервер дописывает в список следующего по вкладу
	/// урона и вешает команду заново. Дописывать больше некого — команду он не перевешивает, добыча
	/// свободна любому: право живёт ровно столько, сколько висит сама команда. Сколько осталось до
	/// расширения круга, несёт остаток той же команды, длина ступени — её длительность.
	/// </summary>
	[System.Serializable]
	public class LootOwnerRecive
	{
		public string[] open;

		/// <summary>Добыча за кем-то закреплена (иначе она ничейная).</summary>
		public bool HasOwner
		{
			get { return open != null && open.Length > 0; }
		}

		/// <summary>Ключи, которым добыча уже открыта (пусто — открыта всем).</summary>
		public string[] Allowed
		{
			get { return open ?? new string[0]; }
		}

		/// <summary>
		/// Разрешено ли игроку playerKey брать из этой добычи. Зеркалит серверную проверку в item/give:
		/// закреплённых нет либо он среди них. Клиентский guard — UX-фильтр, истина на сервере (он молча
		/// откажет), поэтому запоздалый приход новой ступени приводит лишь к на миг заблокированной кнопке,
		/// но не к дисконнекту.
		/// </summary>
		public bool CanTake(string playerKey)
		{
			if (!HasOwner) return true;
			if (string.IsNullOrEmpty(playerKey)) return false;

			for (int i = 0; i < open.Length; i++)
				if (open[i] == playerKey)
					return true;

			return false;
		}
	}
}
