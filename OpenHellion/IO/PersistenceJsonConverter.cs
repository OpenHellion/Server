using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZeroGravity;

namespace OpenHellion.IO;

public class PersistenceJsonConverter : JsonConverter
{
	// This converter only resolves the concrete type when reading. Writing is left to the default
	// serialiser, which stores the type in PersistenceData.__ObjectType. Serialising here would
	// re-enter this converter through the same JsonSerializer and recurse until the stack overflows.
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(PersistenceData) || objectType == typeof(PersistenceObjectData);
	}

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
		{
			return null;
		}
		JObject jo = JObject.Load(reader);
		return PersistenceData.GetData(jo, serializer);
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
	{
		throw new NotSupportedException("PersistenceJsonConverter is read-only.");
	}
}
