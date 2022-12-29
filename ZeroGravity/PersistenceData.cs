using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroGravity;

public abstract class PersistenceData
{
	public string __ObjectType => GetType().ToString();

	public static object GetData(JObject jo, JsonSerializer serializer)
	{
		string objectType = (string)jo["__ObjectType"];
		try
		{
			return jo.ToObject(Type.GetType(objectType), serializer);
		}
		catch
		{
			Dbg.Error("Could not deseralize data", objectType);
		}
		return null;
	}
}
