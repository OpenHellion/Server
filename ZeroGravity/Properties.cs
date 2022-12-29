using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace ZeroGravity;

public class Properties
{
	private Dictionary<string, string> properties = new Dictionary<string, string>();

	private DateTime propertiesChangedTime;

	private string fileName = "properties.ini";

	public Properties(string fileName)
	{
		this.fileName = fileName;
		loadProperties();
	}

	private void loadProperties()
	{
		try
		{
			propertiesChangedTime = File.GetLastWriteTime(fileName);
			properties.Clear();
			string[] array = File.ReadAllLines(fileName);
			foreach (string row in array)
			{
				try
				{
					string[] parts = row.Split("=".ToCharArray(), 2);
					properties.Add(parts[0].ToLower(), parts[1]);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	public T GetProperty<T>(string propertyName)
	{
		DateTime dt = File.GetLastWriteTime(fileName);
		if (dt != propertiesChangedTime)
		{
			loadProperties();
		}
		TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
		return (T)converter.ConvertFrom(properties[propertyName.ToLower()]);
	}

	public void GetProperty<T>(string propertyName, ref T value)
	{
		try
		{
			value = GetProperty<T>(propertyName);
		}
		catch
		{
		}
	}

	public void SetProperty(string name, string value)
	{
		properties[name.Trim()] = value;
	}
}
