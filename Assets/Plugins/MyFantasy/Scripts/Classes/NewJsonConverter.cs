using Newtonsoft.Json;
using System;
using UnityEngine;

namespace MyFantasy
{
	public class NewJsonConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return (objectType == typeof(decimal) || objectType == typeof(decimal?) || objectType == typeof(double) || objectType == typeof(double?) || objectType == typeof(float) || objectType == typeof(float?));
		}

		public override void WriteJson(JsonWriter writer, object value,  JsonSerializer serializer)
		{
			var valCasted = Convert.ToDecimal(value);
			if (Math.Round(valCasted, 10) == Math.Truncate(valCasted))
			{
				writer.WriteValue((int)Math.Truncate(valCasted));
			}
			else
			{
				writer.WriteValue(valCasted);
			}
		}

		public override bool CanRead { get { return false; } }

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
