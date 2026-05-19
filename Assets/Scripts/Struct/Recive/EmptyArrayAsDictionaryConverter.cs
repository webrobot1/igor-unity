using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#nullable enable

namespace Mmogick
{
	// Маппит JSON `[]` в пустой Dictionary вместо стандартного JsonSerializationException.
	// Нужно для контракта где сервер шлёт `[]` как сигнал full-clear, отличимый от null
	// (no-op) — см. base/components/equip.yaml. Без этого Newtonsoft падает на
	// "Cannot deserialize the current JSON array into Dictionary".
	public class EmptyArrayAsDictionaryConverter<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>?>
		where TKey : notnull
	{
		public override Dictionary<TKey, TValue>? ReadJson(JsonReader reader, Type objectType, Dictionary<TKey, TValue>? existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;

			if (reader.TokenType == JsonToken.StartArray)
			{
				reader.Skip();
				return new Dictionary<TKey, TValue>();
			}

			return serializer.Deserialize<Dictionary<TKey, TValue>>(reader);
		}

		public override void WriteJson(JsonWriter writer, Dictionary<TKey, TValue>? value, JsonSerializer serializer)
		{
			serializer.Serialize(writer, value);
		}
	}
}
