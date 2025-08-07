// GameTransport.cs
//
// Copyright (C) 2024, OpenHellion contributors
//
// Inspiration taken from WatsonTcp.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OpenHellion.IO;
using ProtoBuf;
using ZeroGravity.Network;

namespace OpenHellion.Net;

/// <summary>
/// 	Lightweight single-connection game transport with framing.
/// </summary>
/// <remarks>
/// 	Largely decoupled from the program, but it does contain som references to <c>EventSystem</c>
/// 	to invoke received messages. Might move these into callbacks, but it really isn't necessary.
/// 	Needs TLS support. Depends upon <c>ProtoSerialiser</c> and <c>NetworkData</c>.
/// </remarks>
internal sealed class GameTransport
{
	private const int TIMEOUT_MS = 4000;

	private const int MAX_MESSAGE_SIZE = 16000000;

	private readonly Dictionary<long, ConnectionData> _connections = new();

	private Socket _server;

	private readonly Func<NetworkStream, long[], int, Task<long>> _onConnected;

	private readonly Action<long> _onDisconnected;

	private readonly Func<int> _maxConnections;

	private readonly CancellationTokenSource _mainCancellationToken = new CancellationTokenSource();

	internal int Connections
	{
		get
		{
			return _connections.Count;
		}
	}

	struct ConnectionData
	{
		internal Socket socket;
		internal NetworkStream stream;
		internal CancellationTokenSource cancellationToken;
		internal Action<NetworkData> syncResponseReceivedEvent;
	}

	internal GameTransport(Func<NetworkStream, long[], int, Task<long>> onConnected, Action<long> onDisconnected, Func<int> maxConnections)
	{
		_onConnected = onConnected;
		_onDisconnected = onDisconnected;
		_maxConnections = maxConnections;
	}

	internal void Start(int port)
	{
		_server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
		{
			Blocking = true,
		};
		_server.Bind(new IPEndPoint(IPAddress.Any, port));
		_server.Listen();

		Task.Run(() => AcceptConnections(_mainCancellationToken.Token));
	}

	internal async Task AcceptConnections(CancellationToken token)
	{
		Debug.Log("Started looking for connections.");
		while (true)
		{
			token.ThrowIfCancellationRequested();
			await Task.Delay(5, token);
			try
			{
				Socket handler = await _server.AcceptAsync(token);
				if (_connections.Count >= _maxConnections())
				{
					Debug.LogWarning("Maximum number of players exceeded.");
					return;
				}

				Debug.LogFormat("Received connection from {0}.", handler.RemoteEndPoint.ToString());

				var stream = new NetworkStream(handler, true);
				long guid = await _onConnected(stream, _connections.Keys.ToArray(), MAX_MESSAGE_SIZE);

				// TODO: There may be a chance someone could get -1 as guid.
				if (guid is -1)
				{
					Debug.LogWarning("Got connection with guid of -1.");
					try
					{
						handler.Shutdown(SocketShutdown.Both);
					}
					finally
					{
						stream.Close();
					}
					return;
				}

				if (_connections.ContainsKey(guid))
				{
					Debug.LogWarning("Got connection with guid already logged in.");
					try
					{
						handler.Shutdown(SocketShutdown.Both);
					}
					finally
					{
						stream.Close();
					}
					return;
				}

				var cancelToken = CancellationTokenSource.CreateLinkedTokenSource(token);
				var connection = new ConnectionData()
				{
					socket = handler,
					stream = stream,
					cancellationToken = cancelToken
				};

				new Thread(() => ListenerThread(guid, connection))
				{
					IsBackground = true
				}.Start();

				_connections.Add(guid, connection);
				Debug.LogFormat("Storing new connection with id: {0}.", _connections.Count);
			}
			catch (TaskCanceledException)
			{
				break;
			}
		}

		Debug.Log("Stopped looking for connections.");
	}

	private async void ListenerThread(long guid, ConnectionData data)
	{
		Debug.Log("Started network listener for client", guid);

		while (true)
		{
			try
			{
				if (data.stream.DataAvailable)
				{
					NetworkData networkData = await ProtoSerialiser.Unpack(data.stream, MAX_MESSAGE_SIZE, data.cancellationToken.Token);
					if (networkData != null)
					{
						networkData.Sender = guid;

						if (networkData.SyncRequest)
						{
							NetworkData res = await EventSystem.InvokeSyncRequest(networkData);
							res.ConversationGuid = networkData.ConversationGuid;
							res.SyncResponse = true;
							await SendAsyncInternal(guid, res).ConfigureAwait(false);
						}
						else if (networkData.SyncResponse)
						{
							data.syncResponseReceivedEvent(networkData);
						}
						else if (DateTime.UtcNow <= networkData.ExpirationUtc) // If message hasn't expired
						{
							EventSystem.Invoke(networkData);
						}
						else
						{
							Debug.LogWarningFormat("Received expired message from client {0}: {1}.", guid, networkData.GetType());
						}
					}
				}
			}
			catch (IOException)
			{
				Debug.Log("Socket terminated, disconnecting client.");
				DisconnectInternal(guid);
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (ArgumentException ex)
			{
				Debug.LogException(ex);
			}
		}
	}

	/// <summary>
	/// 	Send network data to a client.
	/// </summary>
	/// <param name="guid">Guid of client to send to.</param>
	/// <param name="data">The data to send.</param>
	internal async Task SendAsyncInternal(long guid, NetworkData data)
	{
		try
		{
			if (_connections.TryGetValue(guid, out var connectionData))
			{
				data.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(TIMEOUT_MS);
				var packedData = await ProtoSerialiser.Pack(data);
				await connectionData.stream.WriteAsync(packedData).ConfigureAwait(false);
			}
		}
		catch (IOException)
		{
			Debug.Log("Socket terminated, disconnecting client.");
			DisconnectInternal(guid);
		}
	}

	/// <summary>
	/// 	Use request/response-like communication with async support.
	/// </summary>
	/// <param name="guid">Guid of client to send to.</param>
	/// <param name="data">The data to send.</param>
	internal async Task<NetworkData> SendReceiveAsyncInternal(long guid, NetworkData data)
	{
		try
		{
			if (_connections.TryGetValue(guid, out var connectionData))
			{
				data.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(TIMEOUT_MS);
				data.SyncRequest = true;
				var packedData = await ProtoSerialiser.Pack(data);

				NetworkData response = null;
				CancellationTokenSource responseCancel = new();
				void responseHandler(NetworkData responseData)
				{
					if (data.ConversationGuid == responseData.ConversationGuid)
					{
						response = responseData;
						responseCancel.Cancel();
					}
				}
				connectionData.syncResponseReceivedEvent += responseHandler;

				await connectionData.stream.WriteAsync(packedData).ConfigureAwait(false);

				await Task.Delay(TIMEOUT_MS, responseCancel.Token);
				connectionData.syncResponseReceivedEvent -= responseHandler;

				if (response != null)
				{
					return response;
				}
				else
				{
					throw new TimeoutException("A response to a synchronous request was not received within the timeout window.");
				}
			}
		}
		catch (IOException)
		{
			Debug.Log("Socket terminated, disconnecting client.");
			DisconnectInternal(guid);
		}

		return null;
	}

	// Request to send to all clients.
	internal async Task SendToAllAsyncInternal(NetworkData data, long skipPlayerGuid = -1L)
	{
		if (_connections.Count == 0) return;
		data.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(TIMEOUT_MS);
		var packedData = await ProtoSerialiser.Pack(data);

		await Parallel.ForEachAsync(_connections, async (connection, _) =>
		{
			try
			{
				if (connection.Key == skipPlayerGuid) return;
				await connection.Value.stream.WriteAsync(packedData, _mainCancellationToken.Token).ConfigureAwait(false);
			}
			catch (IOException)
			{
				Debug.Log("Socket terminated, disconnecting client.");
				DisconnectInternal(connection.Key);
			}
		});
	}

	internal async Task PrioritySendAsyncInternal(long guid, NetworkData data)
	{
		try
		{
			if (_connections.TryGetValue(guid, out var handler))
			{

				data.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(TIMEOUT_MS);
				var packedData = await ProtoSerialiser.Pack(data);
				await handler.stream.FlushAsync().ConfigureAwait(false);
				await handler.stream.WriteAsync(packedData).ConfigureAwait(false);
			}
			else
			{
				Debug.LogWarning("Priority send to disconnected client.");
			}
		}
		catch (IOException)
		{
			Debug.Log("Socket terminated, disconnecting client.");
			Debugger.Break();
			DisconnectInternal(guid);
		}
	}

	internal bool IsClientConnected(long guid)
	{
		return _connections.ContainsKey(guid);
	}

	internal long[] GetConnectionsGUIDAsync()
	{
		return _connections.Keys.ToArray();
	}

	// Disconnect a client with the provided id.
	internal void DisconnectInternal(long guid)
	{
		_onDisconnected(guid);
		if (_connections.TryGetValue(guid, out ConnectionData connection))
		{
			try
			{
				connection.socket.Shutdown(SocketShutdown.Both);
			}
			finally
			{
				connection.stream.Close();
			}
			connection.cancellationToken.Cancel();
			_connections.Remove(guid);
		}
	}

	// Disconnects all clients.
	internal void DisconnectAll()
	{
		foreach (var guid in _connections.Keys)
		{
			DisconnectInternal(guid);
		}
	}

	internal void StopInternal()
	{
		_mainCancellationToken.Cancel();
		DisconnectAll();
		_server.Close();
	}
}
