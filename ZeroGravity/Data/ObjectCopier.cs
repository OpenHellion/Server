using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ZeroGravity.Data;

public static class ObjectCopier
{
	public static T Copy<T>(T source)
	{
		return DeepCopy(source, 0);
	}

	public static T DeepCopy<T>(T source, int depth = 10)
	{
		if (source == null || depth < 0)
		{
			return source;
		}
		Type type = source.GetType();
		if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
		{
			return source;
		}
		T ret;
		if (source is Array)
		{
			object[] dim = new object[type.GetArrayRank()];
			for (int i = 0; i < dim.Length; i++)
			{
				dim[i] = (source as Array).GetLength(i);
			}
			ret = (T)Activator.CreateInstance(type, dim);
			IEnumerable<IEnumerable<int>> dimensionBounds = from x in Enumerable.Range(0, (source as Array).Rank)
				select Enumerable.Range((source as Array).GetLowerBound(x), (source as Array).GetUpperBound(x) - (source as Array).GetLowerBound(x) + 1);
			foreach (IEnumerable<int> indexSet in dimensionBounds.CartesianProduct())
			{
				int[] pos = indexSet.ToArray();
				(ret as Array).SetValue(DeepCopy((source as Array).GetValue(pos), depth - 1), pos);
			}
		}
		else
		{
			ret = (T)Activator.CreateInstance(type);
			if (source is IDictionary)
			{
				foreach (object key in (source as IDictionary).Keys)
				{
					(ret as IDictionary)[key] = DeepCopy((source as IDictionary)[key], depth - 1);
				}
			}
			else if (type.IsGenericType && source is IEnumerable && type.GetGenericArguments().Length == 1)
			{
				MethodInfo add = type.GetMethod("Add");
				foreach (object obj in source as IEnumerable)
				{
					add.Invoke(ret, new object[1] { DeepCopy(obj, depth - 1) });
				}
			}
			else
			{
				FieldInfo[] fields = type.GetFields();
				FieldInfo[] array = fields;
				foreach (FieldInfo fi in array)
				{
					object value = fi.GetValue(source);
					fi.SetValue(ret, DeepCopy(value, depth - 1));
				}
				PropertyInfo[] properties = type.GetProperties();
				PropertyInfo[] array2 = properties;
				foreach (PropertyInfo pi in array2)
				{
					if (pi.CanRead && pi.CanWrite && pi.GetIndexParameters().Length == 0)
					{
						object value2 = pi.GetValue(source, null);
						pi.SetValue(ret, DeepCopy(value2, depth - 1), null);
					}
				}
			}
		}
		return ret;
	}

	public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IEnumerable<T>> sequences)
	{
		IEnumerable<IEnumerable<T>> emptyProduct = new IEnumerable<T>[1] { Enumerable.Empty<T>() };
		return sequences.Aggregate(emptyProduct, (IEnumerable<IEnumerable<T>> accumulator, IEnumerable<T> sequence) => from accseq in accumulator
			from item in sequence
			select accseq.Concat(new T[1] { item }));
	}
}
