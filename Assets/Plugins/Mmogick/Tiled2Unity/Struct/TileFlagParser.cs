namespace Mmogick
{
	/// <summary>
	/// Парсер sha256-строки c опциональным flip-суффиксом, который сервер
	/// (см. AbstractObject::jsonSerialize в PHP) шлёт для тайлов и объектов:
	///
	///   "abc..."     — только sha256, флагов нет
	///   "abc...:f"   — sha256 + ':' + одна hex-цифра (1..F) с битовой маской флагов
	///
	/// Битовая маска (4 младших бита):
	///   0x8 → flipH      (отражение по X)
	///   0x4 → flipV      (отражение по Y)
	///   0x2 → flipD      (диагональное отражение Tiled)
	///   0x1 → rotHex120  (поворот hex-тайла на 120°)
	///
	/// Используется и в LayerTile (десятки тысяч инстансов на карту — критично
	/// не аллоцировать без нужды), и в LayerObject — оба формата идентичны на проводе.
	/// </summary>
	public static class TileFlagParser
	{
		public const int FlagFlipH     = 0x8;
		public const int FlagFlipV     = 0x4;
		public const int FlagFlipD     = 0x2;
		public const int FlagRotHex120 = 0x1;

		/// <summary>
		/// Возвращает индекс ':' в raw или -1 если двоеточия нет.
		/// raw == null → -1.
		/// </summary>
		public static int IndexOfColon(string raw)
		{
			return raw == null ? -1 : raw.IndexOf(':');
		}

		/// <summary>
		/// "Чистый" sha256 без суффикса. Если двоеточия нет — возвращает raw как есть.
		/// </summary>
		public static string Sha256(string raw)
		{
			int c = IndexOfColon(raw);
			return c < 0 ? raw : raw.Substring(0, c);
		}

		/// <summary>
		/// 4-битная flip-маска. 0 если суффикса нет или он невалиден.
		/// </summary>
		public static int Flags(string raw)
		{
			int c = IndexOfColon(raw);
			if (c < 0 || raw == null || c + 1 >= raw.Length) return 0;
			return HexDigit(raw[c + 1]);
		}

		public static bool FlipH(int flags)     => (flags & FlagFlipH)     != 0;
		public static bool FlipV(int flags)     => (flags & FlagFlipV)     != 0;
		public static bool FlipD(int flags)     => (flags & FlagFlipD)     != 0;
		public static bool RotHex120(int flags) => (flags & FlagRotHex120) != 0;

		private static int HexDigit(char ch)
		{
			if (ch >= '0' && ch <= '9') return ch - '0';
			if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
			if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
			return 0;
		}
	}
}
