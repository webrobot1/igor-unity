namespace Mmogick
{
	/// <summary>
	/// Компонент loot_owner трупа — очередь на добычу. Публичный: приходит всем видящим труп обычной
	/// world-дельтой сущности.
	///   пустой объект {} (queue пуста) — закреплённых нет, добыча свободна любому;
	///   queue                          — ключи игроков по убыванию нанесённого урона;
	///   open                           — сколько первых из очереди уже допущены к добыче;
	///   step                           — секунд на одну ступень (столько ждёт следующий в очереди).
	/// Круг допущенных ширится сам: сервер по истечении ступени поднимает open, а пройдя очередь целиком —
	/// гасит право. Сколько осталось до следующей ступени, несёт остаток события
	/// <see cref="CorpseLootMarker.GROUP_LOOTFREE"/> у той же сущности.
	/// </summary>
	[System.Serializable]
	public class LootOwnerRecive
	{
		public string[] queue;

		public int open;

		public double step;

		/// <summary>Добыча за кем-то закреплена (иначе она ничейная).</summary>
		public bool HasOwner
		{
			get { return queue != null && queue.Length > 0; }
		}

		/// <summary>Ключи, которым добыча уже открыта (пусто — открыта всем).</summary>
		public string[] Allowed
		{
			get
			{
				if (!HasOwner) return new string[0];

				int count = open < queue.Length ? open : queue.Length;
				if (count < 0) count = 0;

				string[] allowed = new string[count];
				System.Array.Copy(queue, allowed, count);
				return allowed;
			}
		}

		/// <summary>
		/// Разрешено ли игроку playerKey брать из этой добычи. Зеркалит серверную проверку в item/give:
		/// закреплённых нет либо он среди уже открытых ступеней. Клиентский guard — UX-фильтр, истина
		/// на сервере (он молча откажет), поэтому запоздалый приход новой ступени приводит лишь к на миг
		/// заблокированной кнопке, но не к дисконнекту.
		/// </summary>
		public bool CanTake(string playerKey)
		{
			if (!HasOwner) return true;
			if (string.IsNullOrEmpty(playerKey)) return false;

			int count = open < queue.Length ? open : queue.Length;
			for (int i = 0; i < count; i++)
				if (queue[i] == playerKey)
					return true;

			return false;
		}
	}
}
