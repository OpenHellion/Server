using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroGravity;

// TODO: Got some very strange logic going on here.
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
			Debug.LogError("Could not deseralize data", objectType);
		}
		return null;
	}
}
