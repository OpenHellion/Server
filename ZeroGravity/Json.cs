using System;
using System.IO;
using Newtonsoft.Json;

namespace ZeroGravity;

public static class Json
{
	public enum Formatting
	{
		None,
		Indented
	}

	private static AuxDataJsonConverter auxDataJsonConverter = new AuxDataJsonConverter();

	private static AttachPointDataJsonConverter attachPointDataJsonConverter = new AttachPointDataJsonConverter();

	private static PersistenceJsonConverter persistenceJsonConverter = new PersistenceJsonConverter();

	public static string Serialize(object obj, Formatting format = Formatting.Indented)
	{
		return JsonConvert.SerializeObject(obj, (Newtonsoft.Json.Formatting)format);
	}

	public static void SerializeToFile(object obj, string filePath, Formatting format = Formatting.Indented)
	{
		File.WriteAllText(filePath, JsonConvert.SerializeObject(obj, (Newtonsoft.Json.Formatting)format, new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore
		}));
	}

	public static void SerializeToFile(object obj, string filePath, Formatting format = Formatting.Indented, JsonSerializerSettings settings = null)
	{
		File.WriteAllText(filePath, JsonConvert.SerializeObject(obj, (Newtonsoft.Json.Formatting)format, settings));
	}

	public static T Deserialize<T>(string jsonString)
	{
		return JsonConvert.DeserializeObject<T>(jsonString, new JsonConverter[3] { auxDataJsonConverter, attachPointDataJsonConverter, persistenceJsonConverter });
	}

	public static T Load<T>(string filePath)
	{
		DateTime t0 = DateTime.UtcNow;
		T ret = JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath), new JsonConverter[3] { auxDataJsonConverter, attachPointDataJsonConverter, persistenceJsonConverter });
		DateTime t1 = DateTime.UtcNow;
		return ret;
	}
}
