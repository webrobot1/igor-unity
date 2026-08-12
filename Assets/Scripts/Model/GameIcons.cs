using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Значки полосок над миром. Грузятся из Resources один раз на сессию: полоски зовут их каждый кадр,
	/// а Resources.Load ищет по имени и на горячем пути стоит дорого.
	/// </summary>
	public static class GameIcons
	{
		private const string SkullPath = "Sprites/UI/skull";
		private const string LootPath = "Sprites/UI/loot";
		private const string LootLockedPath = "Sprites/UI/loot-locked";

		private static Sprite _skull;
		private static bool _skullTried;

		private static Sprite _loot;
		private static bool _lootTried;

		private static Sprite _lootLocked;
		private static bool _lootLockedTried;

		/// <summary>Череп — сроки мёртвого тела (уход с карты, ожидание подъёма).</summary>
		public static Sprite Skull
		{
			get { return Load(SkullPath, ref _skull, ref _skullTried); }
		}

		/// <summary>Мешок — добыча тела.</summary>
		public static Sprite Loot
		{
			get { return Load(LootPath, ref _loot, ref _lootTried); }
		}

		/// <summary>Перечёркнутый мешок — добыча закреплена за другими, брать нельзя.</summary>
		public static Sprite LootLocked
		{
			get { return Load(LootLockedPath, ref _lootLocked, ref _lootLockedTried); }
		}

		// Промах загрузки — дефект сборки, а не состояние: жалуемся ОДИН раз и дальше рисуем полоску без
		// значка, иначе Error повторялся бы каждый кадр у каждого тела.
		private static Sprite Load(string path, ref Sprite cache, ref bool tried)
		{
			if (cache != null || tried)
				return cache;

			tried = true;
			cache = Resources.Load<Sprite>(path);

			if (cache == null)
				ConnectController.Error("Не найден значок " + path);

			return cache;
		}
	}
}
