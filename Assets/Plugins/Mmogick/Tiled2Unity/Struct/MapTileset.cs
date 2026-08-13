namespace Mmogick
{
	/// <summary>
	/// Набор графики, подключённый к карте (запись mapTileset среза карты). Сами пиксели и нарезку клиент
	/// берёт своим каналом кеша тайлов по sha256 — здесь только принадлежность набора карте.
	/// </summary>
	[System.Serializable]
	public class MapTileset
	{
		// Имя файла набора: им набор адресуется внутри карты (в TMX — ссылка на файл).
		public string basename;

		public string name;

		// Класс набора из Tiled.
		public string @class;

		// SHA256 набора — его идентичность на сервере (содержимое адресуется хешем).
		public string tileset;
	}
}
