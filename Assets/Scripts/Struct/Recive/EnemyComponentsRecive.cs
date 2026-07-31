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

		// Добыча трупа (компонент loot) — ПУБЛИЧНЫЙ: приходит ВСЕМ видящим сущность обычной world-дельтой,
		// адресного запроса содержимого нет. Формат слота тот же, что у инвентаря; ёмкость равна числу
		// слотов инвентаря.
		//   null           — поля нет в пакете (no-op, прежнее содержимое в силе);
		//   позиция → null — позиция пуста; все позиции null = добычи нет (не выпало либо обыскан);
		//   позиция → item — предмет.
		// Дельта ЧАСТИЧНА (сервер шлёт per-slot diff) — сливать в накопленное состояние, не подменять
		// его целиком: не пришедшая позиция означает «не менялась», а не «опустела».
		public Dictionary<int, ItemSlotRecive>? loot = null;

		// Право на добычу (компонент loot_owner), тоже публичный. null — поля нет в пакете; пустой
		// объект — владельца нет. Приходит целиком (не по-ключево).
		public LootOwnerRecive? loot_owner = null;
	}
}
