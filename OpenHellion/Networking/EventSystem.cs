using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using OpenHellion.Networking.Message.MainServer;
using ZeroGravity;
using ZeroGravity.Network;

namespace OpenHellion.Networking;

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

	private List<Type> _invokeImmediatelyDataTypes = new List<Type>();

	private ConcurrentDictionary<Type, NetworkDataDelegate> _networkDataGroups = new ConcurrentDictionary<Type, NetworkDataDelegate>();

	private ConcurrentQueue<NetworkData> _networkBuffer = new ConcurrentQueue<NetworkData>();

	private ConcurrentDictionary<InternalEventType, InternalEventsDelegate> _internalDataGroups = new ConcurrentDictionary<InternalEventType, InternalEventsDelegate>();

	private ConcurrentQueue<InternalEventData> _internalBuffer = new ConcurrentQueue<InternalEventData>();

	public EventSystem()
	{
		_invokeImmediatelyDataTypes.Add(typeof(PlayerHitMessage));
	}

	private static EventSystem s_instance;
	internal static EventSystem Instance
	{
		get
		{
			if (s_instance == null)
			{
				s_instance = new EventSystem();
			}

			return s_instance;
		}
	}

	/// <summary>
	/// 	Add listener for custom events.
	/// </summary>
	public static void AddListener(Type group, NetworkDataDelegate function)
	{
		if (Instance._networkDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<Type, NetworkDataDelegate> concurrentDictionary = Instance._networkDataGroups;
			concurrentDictionary[group] = (NetworkDataDelegate)Delegate.Combine(concurrentDictionary[group], function);
		}
		else
		{
			Instance._networkDataGroups[group] = function;
		}
	}

	/// <summary>
	/// 	Add listener for custom events.
	/// </summary>
	public static void AddListener(InternalEventType group, InternalEventsDelegate function)
	{
		if (Instance._internalDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<InternalEventType, InternalEventsDelegate> concurrentDictionary = Instance._internalDataGroups;
			concurrentDictionary[group] = (InternalEventsDelegate)Delegate.Combine(concurrentDictionary[group], function);
		}
		else
		{
			Instance._internalDataGroups[group] = function;
		}
	}

	/// <summary>
	/// 	Remove listener for custom events.
	/// </summary>
	public static void RemoveListener(Type group, NetworkDataDelegate function)
	{
		if (Instance._networkDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<Type, NetworkDataDelegate> concurrentDictionary = Instance._networkDataGroups;
			concurrentDictionary[group] = (NetworkDataDelegate)Delegate.Remove(concurrentDictionary[group], function);
		}
	}

	/// <summary>
	/// 	Remove listener for custom events.
	/// </summary>
	public static void RemoveListener(InternalEventType group, InternalEventsDelegate function)
	{
		if (Instance._internalDataGroups.ContainsKey(group))
		{
			ConcurrentDictionary<InternalEventType, InternalEventsDelegate> concurrentDictionary = Instance._internalDataGroups;
			concurrentDictionary[group] = (InternalEventsDelegate)Delegate.Remove(concurrentDictionary[group], function);
		}
	}

	/// <summary>
	/// 	Execute corresponding code for request.
	/// </summary>
	internal void Invoke(NetworkData data)
	{
		if (_networkDataGroups.ContainsKey(data.GetType()) && _networkDataGroups[data.GetType()] != null)
		{
			if (_invokeImmediatelyDataTypes.Contains(data.GetType()) || Thread.CurrentThread.ManagedThreadId == Server.MainThreadID)
			{
				_networkDataGroups[data.GetType()](data);
			}
			else
			{
				_networkBuffer.Enqueue(data);
			}
		}
	}

	/// <summary>
	/// 	Execute corresponding code for request.
	/// </summary>
	internal void Invoke(InternalEventData data)
	{
		if (_internalDataGroups.ContainsKey(data.Type) && _internalDataGroups[data.Type] != null)
		{
			if (Thread.CurrentThread.ManagedThreadId == Server.MainThreadID)
			{
				_internalDataGroups[data.Type](data);
			}
			else
			{
				_internalBuffer.Enqueue(data);
			}
		}
		else
		{
			Dbg.Error("Cannot invoke ", data.Type, data);
		}
	}

	/// <summary>
	/// 	Execute code for requests stored in queue.
	/// </summary>
	internal void InvokeQueuedData()
	{
		while (_networkBuffer.Count > 0)
		{
			try
			{
				if (_networkBuffer.TryDequeue(out var data2) && _networkDataGroups.TryGetValue(data2.GetType(), out var ndd))
				{
					ndd(data2);
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}
		}
		while (_internalBuffer.Count > 0)
		{
			try
			{
				if (_internalBuffer.TryDequeue(out var data) && _internalDataGroups.TryGetValue(data.Type, out var ied))
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
}
