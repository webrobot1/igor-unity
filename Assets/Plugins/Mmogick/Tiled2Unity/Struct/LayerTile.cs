namespace Mmogick
{
	/// <summary>
	/// тайловая карта на слое.
	/// На проводе — sparse-словарь int → string в одном из двух форматов:
	///   "abc..."     — только sha256, флагов нет
	///   "abc...:f"   — sha256 + ':' + одна hex-цифра (1..F) с битовой маской флагов
	///
	/// Битовая маска (4 младших бита):
	///   0x8 → flipH      (отражение по X)
	///   0x4 → flipV      (отражение по Y)
	///   0x2 → flipD      (диагональное отражение Tiled)
	///   0x1 → rotHex120  (поворот hex-тайла на 120°)
	///
	/// Поля x/y — координаты ячейки на тайлмапе, заполняются после
	/// десериализации в MapDecodeModel.generate из ключа словаря.
	///
	/// Парсинг raw-строки делегирован в TileFlagParser — общий между LayerTile и LayerObject.
	/// </summary>
	public class LayerTile
	{
		// Сырое значение как пришло с сервера. Десятки тысяч инстансов
		// на карту — критично не дублировать строки.
		public readonly string raw;

		public int x;
		public int y;

		// Кешируем индекс ':' и парсим лениво. -2 = ещё не считали, -1 = двоеточия нет.
		private int colonIndex = -2;

		public LayerTile(string raw)
		{
			this.raw = raw;
		}

		private int ColonIndex
		{
			get
			{
				if (colonIndex == -2)
					colonIndex = TileFlagParser.IndexOfColon(raw);
				return colonIndex;
			}
		}

		public string sha256
		{
			get
			{
				int c = ColonIndex;
				return c < 0 ? raw : raw.Substring(0, c);
			}
		}

		public int flags => TileFlagParser.Flags(raw);

		public bool flipH      => TileFlagParser.FlipH(flags);
		public bool flipV      => TileFlagParser.FlipV(flags);
		public bool flipD      => TileFlagParser.FlipD(flags);
		public bool rotHex120  => TileFlagParser.RotHex120(flags);
	}
}
