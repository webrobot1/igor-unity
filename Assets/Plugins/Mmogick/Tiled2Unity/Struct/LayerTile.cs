namespace Mmogick
{
	/// <summary>
	/// Один декодированный тайл слоя тайл-карты.
	/// На проводе тайлов слоя нет как объектов — слой несёт ОДНУ CSV-строку (см. Layer.tile,
	/// формат LayerTileCsvCodec). MapDecodeModel.generate парсит её в набор LayerTile:
	/// tile (sha256 из легенды) + flip-флаги из битмаски + x/y из позиции ячейки в CSV
	/// (i = y*width+x; y инвертируется умножением на -1). Отсутствующий флаг = false.
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
