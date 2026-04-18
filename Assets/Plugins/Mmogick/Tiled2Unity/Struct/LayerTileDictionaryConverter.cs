using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace Mmogick
{
	/// <summary>
	/// Десериализует sparse-карту тайлов из terrain.json:
	/// JSON: { "12": "abc...", "37": "def...:c", ... }
	/// в Dictionary&lt;int, LayerTile&gt;, где ключ — индекс ячейки,
	/// а значение — обёртка с ленивым парсингом sha256/флагов.
	///
	/// Сериализация обратно в JSON не используется (клиент только читает),
	/// но реализована для симметрии — пишет raw-строку.
	/// </summary>
	public class LayerTileDictionaryConverter : JsonConverter<Dictionary<int, LayerTile>>
	{
		public override Dictionary<int, LayerTile> ReadJson(
			JsonReader reader,
			Type objectType,
			Dictionary<int, LayerTile> existingValue,
			bool hasExistingValue,
			JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null) return null;
			if (reader.TokenType != JsonToken.StartObject)
				throw new JsonSerializationException(
					"Ожидался объект для tiles, получен " + reader.TokenType);

			var dict = new Dictionary<int, LayerTile>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonToken.EndObject) return dict;
				if (reader.TokenType != JsonToken.PropertyName)
					throw new JsonSerializationException(
						"Ожидался ключ tiles, получен " + reader.TokenType);

				string keyStr = (string)reader.Value;
				if (!int.TryParse(keyStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
					throw new JsonSerializationException("Ключ tile не int: " + keyStr);

				if (!reader.Read())
					throw new JsonSerializationException("Оборван JSON после ключа " + keyStr);

				if (reader.TokenType == JsonToken.Null)
				{
					dict[key] = null;
				}
				else if (reader.TokenType == JsonToken.String)
				{
					dict[key] = new LayerTile((string)reader.Value);
				}
				else
				{
					throw new JsonSerializationException(
						"Ожидалась строка для tile " + keyStr + ", получен " + reader.TokenType);
				}
			}

			throw new JsonSerializationException("Неожиданный конец JSON в tiles");
		}

		public override void WriteJson(JsonWriter writer, Dictionary<int, LayerTile> value, JsonSerializer serializer)
		{
			if (value == null) { writer.WriteNull(); return; }
			writer.WriteStartObject();
			foreach (var kv in value)
			{
				writer.WritePropertyName(kv.Key.ToString(CultureInfo.InvariantCulture));
				writer.WriteValue(kv.Value?.raw);
			}
			writer.WriteEndObject();
		}
	}
}
