// GsConnection.cs
//
// Copyright (C) 2023, OpenHellion contributors
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
using System.IO;
using System.Linq;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Net;

/// <summary>
/// 	Handles connections to the clients.<br />
/// 	This works as an abstraction layer between the controller and the networking library.
/// 	Most functions in this method is abstracted by the <c>NetworkController</c>
/// </summary>
internal class GsConnection
{

	private readonly Dictionary<int, Player> _clientConnections = new();

	private Telepathy.Server _server;

	internal void Start(int port)
	{
		Telepathy.Log.Info = Debug.Info;
		Telepathy.Log.Warning = Debug.Warning;
		Telepathy.Log.Error = Debug.Error;

		_server = new(80000)
		{
			OnConnected = OnConnected,
			OnData = OnData,
			OnDisconnected = OnDisconnected,
			SendQueueLimit = 1000,
			ReceiveQueueLimit = 1000
#if DEBUG
			,SendTimeout = 0
			,ReceiveTimeout = 0
#endif
		};

		_server.Start(port);

		Debug.Log("Started server game thread.");
	}

	/// <summary>
	/// 	Send network data to a client.
	/// </summary>
	internal void Send(int connectionId, NetworkData data)
	{
		try
		{
			// Package data.
			ArraySegment<byte> binary = new(Serializer.Package(data));

			// Send data to server.
			bool success = _server.Send(connectionId, binary);

			if (!success)
			{
				Debug.Log("Failed to send data to a client.", connectionId);
			}
		}
		catch (ArgumentNullException)
		{
			Debug.Error("Serialized data buffer is null", data.GetType().ToString(), data);
		}
		catch (Exception ex)
		{
			Debug.Error("Error when sending data", ex.Message, ex.StackTrace);
		}
	}

	// Request to send to all clients.
	internal void SendToAll(NetworkData data, long skipPlayerGuid = -1L)
	{
		foreach (KeyValuePair<int, Player> pl in _clientConnections)
		{
			Player player = pl.Value;
			if (player != null && player.IsAlive && player.EnvironmentReady && player.GUID != skipPlayerGuid)
			{
				Send(pl.Key, data);
			}
		}
	}

	// Intends to clear all data in the queue to send a request, but we don't have access to the queue.
	// TODO: Fix this.
	internal void ClearEverythingAndSend(int connectionId, NetworkData data)
	{
		Send(connectionId, data);
	}

	// Disconnect a client with the provided id.
	internal void Disconnect(int connectionId)
	{
		if (_clientConnections.ContainsKey(connectionId))
		{
			// If there is a player, make sure it has been disconnected.
			Player player = _clientConnections[connectionId];
			player?.LogoutDisconnectReset();
			player?.DiconnectFromNetworkContoller();

			// Remove it.
			_clientConnections.Remove(connectionId);
		}
		else
		{
			Debug.Error("Tried to remove non-existent player with connection id", connectionId);
		}

		_server.Disconnect(connectionId);
	}

	// Disconnects all clients.
	internal void DisconnectAll()
	{
		// Loop through all client and do the neccecary actions for them to exit.
		foreach (KeyValuePair<int, Player> pl in _clientConnections)
		{
			_server.Disconnect(pl.Key);
			if (pl.Value != null)
			{
				pl.Value.LogoutDisconnectReset();
				pl.Value.DiconnectFromNetworkContoller();
			}
		}

		// Clean.
		_clientConnections.Clear();
	}

	internal void Tick()
	{
		_server.Tick(50);
	}

	internal void Stop()
	{
		NetworkController.DisconnectAllClients();
		_server.Stop();
	}

	// Create a bare client with a temporary id.
	// Partial implementation of the corresponding function in NetworkController.
	internal void AddBareClient(int connectionId)
	{
		_clientConnections.Add(connectionId, null);
	}

	internal void SetPlayer(int connectionId, Player player)
	{
		if (_clientConnections.ContainsKey(connectionId))
		{
			_clientConnections[connectionId] = player;
		}
		else
		{
			Debug.Error("Error setting player for client", connectionId);
		}
	}

	internal Player GetPlayer(int connectionId)
	{
		if (_clientConnections.ContainsKey(connectionId) && _clientConnections[connectionId] != null)
		{
			return _clientConnections[connectionId];
		}
		else
		{
			Debug.Error("Error getting player for client", connectionId);
		}

		return null;
	}

	internal Player[] GetAllPlayers()
	{
		return _clientConnections.Values.ToArray();
	}

	private void OnConnected(int connectionId)
	{
		string ipAddress = _server.GetClientAddress(connectionId);

		Debug.Log("Client connected", connectionId, ipAddress);

		try
		{
			NetworkController.AddBareClient(connectionId);
		}
		catch (Exception ex)
		{
			Debug.Exception(ex);
		}
	}

	private void OnData(int connectionId, ArraySegment<byte> message)
	{
		try
		{
			NetworkData networkData = Serializer.Unpackage(new MemoryStream(message.ToArray()));
			if (networkData != null)
			{
				if (_clientConnections[connectionId] != null)
				{
					networkData.Sender = _clientConnections[connectionId].GUID;
				}
				else
				{
					networkData.Sender = NetworkController.Clients.First(entry => entry.Value == connectionId).Key;
				}

				EventSystem.Instance.Invoke(networkData);
			}
		}
		catch (Exception ex)
		{
			Debug.Exception(ex);
			NetworkController.DisconnectClient(connectionId);
		}
	}

	private void OnDisconnected(int connectionId)
	{
		if (_clientConnections.TryGetValue(connectionId, out Player player))
		{
			player?.RemovePlayerFromTrigger();
			_clientConnections.Remove(connectionId);
			NetworkController.OnDisconnect(connectionId);
		}

		Debug.Info("Client disconnected", Server.IsRunning, connectionId);
	}
}
