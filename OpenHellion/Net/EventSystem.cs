using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroGravity;
using ZeroGravity.Network;

namespace OpenHellion.Net;

public class EventSystem
{

	private static readonly List<Type> _invokeImmediatelyDataTypes = new List<Type>();

	private static readonly ConcurrentDictionary<Type, Action<NetworkData>> _networkDataListeners = new ConcurrentDictionary<Type, Action<NetworkData>>();
	private static readonly ConcurrentDictionary<Type, Func<NetworkData, Task<NetworkData>>> _syncRequestListeners = new();

	private static readonly ConcurrentQueue<NetworkData> _networkBuffer = new ConcurrentQueue<NetworkData>();

	public EventSystem()
	{
		_invokeImmediatelyDataTypes.Add(typeof(PlayerHitMessage));
	}

	/// <summary>
	/// 	Add listener for custom events.
	/// </summary>
	public static void AddListener<T>(Action<NetworkData> function)
	{
		if (_networkDataListeners.ContainsKey(typeof(T)))
		{
			_networkDataListeners[typeof(T)] += function;
		}
		else
		{
			_networkDataListeners[typeof(T)] = function;
		}
	}

	/// <summary>
	/// 	Add listener for sync requests.
	/// </summary>
	public static void AddSyncRequestListener<T>(Func<NetworkData, Task<NetworkData>> function)
	{
		if (_syncRequestListeners.ContainsKey(typeof(T)))
		{
			_syncRequestListeners[typeof(T)] += function;
		}
		else
		{
			_syncRequestListeners[typeof(T)] = function;
		}
	}

	/// <summary>
	/// 	Remove listener for custom events.
	/// </summary>
	public static void RemoveListener<T>(Action<NetworkData> function)
	{
		if (_networkDataListeners.ContainsKey(typeof(T)))
		{
			_networkDataListeners[typeof(T)] -= function;
		}
	}

	/// <summary>
	/// 	Remove listener for sync events.
	/// </summary>
	public static void RemoveSyncRequestListener<T, TResult>(Func<T, Task<TResult>> function)
	{
		if (_syncRequestListeners.ContainsKey(typeof(T)))
		{
			_syncRequestListeners[typeof(T)] -= function as Func<NetworkData, Task<NetworkData>>;
		}
	}

	/// <summary>
	/// 	Execute corresponding code for request.
	/// </summary>
	internal static void Invoke(NetworkData data)
	{
		if (_networkDataListeners.ContainsKey(data.GetType()) && _networkDataListeners[data.GetType()] != null)
		{
			if (_invokeImmediatelyDataTypes.Contains(data.GetType()) || Environment.CurrentManagedThreadId == Server.MainThreadId)
			{
				_networkDataListeners[data.GetType()](data);
			}
			else
			{
				_networkBuffer.Enqueue(data);
			}
		}
		else
		{
			Debug.LogWarningFormat("Got message with type {0} with no registered listener.", data.GetType());
		}
	}

	internal static Task<NetworkData> InvokeSyncRequest(NetworkData data)
	{
		if (_syncRequestListeners.ContainsKey(data.GetType()))
		{
			return _syncRequestListeners[data.GetType()](data);
		}
		else
		{
			Debug.LogWarningFormat("Got sync request with type {0} with no registered listener.", data.GetType());
			return null;
		}
	}

	/// <summary>
	/// 	Execute code for requests stored in queue.
	/// </summary>
	internal static void InvokeQueuedData()
	{
		while (_networkBuffer.Count > 0)
		{
			if (_networkBuffer.TryDequeue(out var data2) && _networkDataListeners.TryGetValue(data2.GetType(), out var networkDataDelegate))
			{
				networkDataDelegate(data2);
			}
		}
	}
}
