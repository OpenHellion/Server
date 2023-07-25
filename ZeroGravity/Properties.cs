using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace ZeroGravity;

public class Properties
{
	private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();

	private DateTime _propertiesChangedTime;

	private readonly string _fileName = "properties.ini";

	public Properties(string fileName)
	{
		_fileName = fileName;
		LoadProperties();
	}

	private void LoadProperties()
	{
		try
		{
			_propertiesChangedTime = File.GetLastWriteTime(_fileName);
			_properties.Clear();
			string[] array = File.ReadAllLines(_fileName);
			foreach (string row in array)
			{
				try
				{
					string[] parts = row.Split("=".ToCharArray(), 2);
					_properties.Add(parts[0].ToLower(), parts[1]);
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
		DateTime lastFileWrite = File.GetLastWriteTime(_fileName);
		if (lastFileWrite != _propertiesChangedTime)
		{
			LoadProperties();
		}
		TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
		return (T)converter.ConvertFrom(_properties[propertyName.ToLower()]);
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
		_properties[name.Trim()] = value;
	}
}
