namespace Mmogick
{
	/// <summary>
	/// Тайл на слое тайл-карты.
	/// На проводе — sparse-словарь int → объект:
	///   { "12": {"sha256":"abc..."}, "37": {"sha256":"def...","flipH":true}, ... }
	/// Где ключ словаря — индекс ячейки (y*width + x), значение — объект с sha256
	/// и опциональными flip-флагами (отсутствующее поле = false).
	/// Поля x/y заполняются после десериализации в MapDecodeModel.generate из ключа словаря.
	/// </summary>
	[System.Serializable]
	public class LayerTile
	{
		public string tile;

		public bool flipH;
		public bool flipV;
		public bool flipD;
		public bool rotHex120;

		public int x;
		public int y;
	}
}
