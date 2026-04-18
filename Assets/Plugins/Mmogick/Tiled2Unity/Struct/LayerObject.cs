using Newtonsoft.Json;

namespace Mmogick
{
	/// <summary>
	/// объекты на слое (полигоны, текст, картинки)
	///
	/// Поле sha256 на проводе теперь приходит в одном из двух форматов:
	///   "abc..."     — только sha256, флагов нет
	///   "abc...:f"   — sha256 + ':' + одна hex-цифра (1..F) с битовой маской флагов
	///
	/// См. AbstractObject::jsonSerialize в PHP и общий парсер TileFlagParser
	/// (используется также в LayerTile — формат идентичен).
	/// </summary>
	[System.Serializable]
	public class LayerObject
	{
		// Сырое значение как пришло с сервера: либо чистый sha256, либо "sha256:hex".
		// JSON-ключ остаётся "sha256" — полная совместимость с серверным форматом.
		[JsonProperty("sha256")]
		private string raw_sha256;

		public string tileset_image;

		public string name = "";

		public float x;
		public float y;

		public float width;
		public float height;

		public float rotation;
		public bool visible = true;

		public bool ellipse;

		public Point[] polygon;
		public Point[] polyline;

		public string text;

		/// <summary>
		/// «Чистый» sha256 без flip-суффикса. Используется для лукапа тайла в TileCacheService.
		/// Возвращает null/"" если сервер не прислал привязанный тайл — IsNullOrEmpty(obj.sha256)
		/// корректно срабатывает в обоих случаях.
		/// </summary>
		[JsonIgnore]
		public string sha256 => TileFlagParser.Sha256(raw_sha256);

		/// <summary>Сырая строка как пришла с сервера (для отладки/логов).</summary>
		[JsonIgnore]
		public string rawSha256 => raw_sha256;

		[JsonIgnore]
		public int flags => TileFlagParser.Flags(raw_sha256);

		[JsonIgnore] public bool flipH      => TileFlagParser.FlipH(flags);
		[JsonIgnore] public bool flipV      => TileFlagParser.FlipV(flags);
		[JsonIgnore] public bool flipD      => TileFlagParser.FlipD(flags);
		[JsonIgnore] public bool rotHex120  => TileFlagParser.RotHex120(flags);
	}
}
