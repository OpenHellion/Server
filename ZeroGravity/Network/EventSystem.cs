using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ZeroGravity.Network;

public class EventSystem
{
	public class InternalEventData
	{
		public InternalEventType Type;

		public object[] Objects;

		public InternalEventData(InternalEventType type, params object[] objects)
		{
			Type = type;
			Objects = objects;
		}
	}

	public delegate void NetworkDataDelegate(NetworkData data);

	public delegate void InternalEventsDelegate(InternalEventData data);

	public enum InternalEventType
	{
		GetPlayer
	}

	private List<Type> invokeImmediatelyDataTypes = new List<Type>();

	private ConcurrentDictionary<Type, NetworkDataDelegate> networkDataGroups = new ConcurrentDictionary<Type, NetworkDataDelegate>();

	private ConcurrentQueue<NetworkData> networkBuffer = new ConcurrentQueue<NetworkData>();

	private ConcurrentDictionary<InternalEventType, InternalEventsDelegate> internalDataGroups = new ConcurrentDictionary<InternalEventType, InternalEventsDelegate>();

	private ConcurrentQueue<InternalEventData> internalBuffer = new ConcurrentQueue<InternalEventData>();

	public EventSystem()
	{
		invokeImmediatelyDataTypes.Add(typeof(PlayerHitMessage));
	}

	public void AddListener(Type group, NetworkDataDelegate function)
	{
		if (networkDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<Type, NetworkDataDelegate> concurrentDictionary = networkDataGroups;
			concurrentDictionary[group] = (NetworkDataDelegate)Delegate.Combine(concurrentDictionary[group], function);
		}
		else
		{
			networkDataGroups[group] = function;
		}
	}

	public void RemoveListener(Type group, NetworkDataDelegate function)
	{
		if (networkDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<Type, NetworkDataDelegate> concurrentDictionary = networkDataGroups;
			concurrentDictionary[group] = (NetworkDataDelegate)Delegate.Remove(concurrentDictionary[group], function);
		}
	}

	public void Invoke(NetworkData data)
	{
		if (networkDataGroups.ContainsKey(data.GetType()) && networkDataGroups[data.GetType()] != null)
		{
			if (invokeImmediatelyDataTypes.Contains(data.GetType()) || Thread.CurrentThread.ManagedThreadId == Server.MainThreadID)
			{
				networkDataGroups[data.GetType()](data);
			}
			else
			{
				networkBuffer.Enqueue(data);
			}
		}
		else if (!(data is MainServerGenericResponse) || (data as MainServerGenericResponse).Response != ResponseResult.Success)
		{
			if (data is MainServerGenericResponse)
			{
				Dbg.Info("Unhandled network package", data.Sender, data.GetType(), (data as MainServerGenericResponse).Message);
			}
			else
			{
				Dbg.Info("Unhandled network package", data.Sender, data.GetType());
			}
		}
	}

	public void InvokeQueuedData()
	{
		while (networkBuffer.Count > 0)
		{
			try
			{
				if (networkBuffer.TryDequeue(out var data2) && networkDataGroups.TryGetValue(data2.GetType(), out var ndd))
				{
					ndd(data2);
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}
		}
		while (internalBuffer.Count > 0)
		{
			try
			{
				if (internalBuffer.TryDequeue(out var data) && internalDataGroups.TryGetValue(data.Type, out var ied))
				{
					ied(data);
				}
			}
			catch (Exception ex2)
			{
				Dbg.Exception(ex2);
			}
		}
	}

	public void AddListener(InternalEventType group, InternalEventsDelegate function)
	{
		if (internalDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<InternalEventType, InternalEventsDelegate> concurrentDictionary = internalDataGroups;
			concurrentDictionary[group] = (InternalEventsDelegate)Delegate.Combine(concurrentDictionary[group], function);
		}
		else
		{
			internalDataGroups[group] = function;
		}
	}

	public void RemoveListener(InternalEventType group, InternalEventsDelegate function)
	{
		if (internalDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<InternalEventType, InternalEventsDelegate> concurrentDictionary = internalDataGroups;
			concurrentDictionary[group] = (InternalEventsDelegate)Delegate.Remove(concurrentDictionary[group], function);
		}
	}

	public void Invoke(InternalEventData data)
	{
		if (internalDataGroups.ContainsKey(data.Type) && internalDataGroups[data.Type] != null)
		{
			if (Thread.CurrentThread.ManagedThreadId == Server.MainThreadID)
			{
				internalDataGroups[data.Type](data);
			}
			else
			{
				internalBuffer.Enqueue(data);
			}
		}
		else
		{
			Dbg.Error("Cannot invoke ", data.Type, data);
		}
	}
}
