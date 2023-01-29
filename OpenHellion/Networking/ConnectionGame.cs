// ConnectionGame.cs
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
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Networking;

/// <summary>
/// 	Handles connections to the clients.<br />
/// 	This works as an abstraction layer between the controller and the networking library.
/// 	Most funcions in this method is absracted by the <c>NetworkController</c>
/// </summary>
internal class ConnectionGame
{
	public class Client
	{
		public long ClientGUID;

		public Player Player = null;
	}

	private Dictionary<int, Client> _clientConnections = new();

	private Telepathy.Server _server;

	internal void Start(int port)
	{
		Telepathy.Log.Info = Dbg.Info;
		Telepathy.Log.Warning = Dbg.Warning;
		Telepathy.Log.Error = Dbg.Error;

		_server = new(30000)
		{
			OnConnected = OnConnected,
			OnData = OnData,
			OnDisconnected = OnDisconnected,
			SendQueueLimit = 1000,
			ReceiveQueueLimit = 1000
		};

		_server.SendQueueLimit = 1000;
		_server.ReceiveQueueLimit = 1000;

		_server.Start(Server.GamePort);

		Dbg.Log("Started server game thread.");
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
				Dbg.Log("Failed to send data to a client.", connectionId);
			}
		}
		catch (ArgumentNullException)
		{
			Dbg.Error("Serialized data buffer is null", data.GetType().ToString(), data);
		}
		catch (Exception ex)
		{
			Dbg.Error("Error when sending data", ex.Message, ex.StackTrace);
		}
	}

	// Request to send to all clients.
	internal void SendToAll(NetworkData data, long skipPlayerGUID = -1L)
	{
		foreach (KeyValuePair<int, Client> client in _clientConnections)
		{
			Player player = client.Value.Player;
			if (player != null && player.IsAlive && player.EnvironmentReady && player.GUID != skipPlayerGUID)
			{
				Send(client.Key, data);
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
			_server.Disconnect(connectionId);

		}
		else
		{
			Dbg.Error("Tried to disconnect client with wrong id.");
		}
	}

	// Disconnects all clients.
	internal void DisconnectAll()
	{
		// Loop through all client and do the neccecary actions for them to exit.
		foreach (KeyValuePair<int, Client> client in _clientConnections)
		{
			_server.Disconnect(client.Key);
			if (client.Value.Player != null)
			{
				client.Value.Player.LogoutDisconnectReset();
				client.Value.Player.DiconnectFromNetworkContoller();
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
		NetworkController.Instance.DisconnectAllClients();
		_server.Stop();
	}

	// Add guid to an already existing client.
	internal void PatchClient(int connectionId, long guid)
	{
		if (_clientConnections.ContainsKey(connectionId))
		{
			_clientConnections[connectionId].ClientGUID = guid;
		}
		else
		{
			Dbg.Error("Trying to add client to connections with id failed", connectionId, guid);
		}
	}

	// Create a bare client with a temporary id.
	// Partial implementation of the corresponding function in NetworkController.
	internal void AddBareClient(int connectionId, long tempId)
	{
		Client cl = new Client();
		cl.ClientGUID = tempId;
		_clientConnections.Add(connectionId, cl);
	}

	// Remove client and disconnect player, but doesn't disconnect the client from the server.
	// Used by the disconnect function.
	internal void RemoveClient(int connectionId)
	{
		if (_clientConnections.ContainsKey(connectionId))
		{
			// If there is a player, make sure it has been disconnected.
			ConnectionGame.Client cl = _clientConnections[connectionId];
			cl.Player?.LogoutDisconnectReset();
			cl.Player?.DiconnectFromNetworkContoller();

			// Remove it.
			_clientConnections.Remove(connectionId);
		}
		else
		{
			Dbg.Error("Tried to remove non-existent client with connection id", connectionId);
		}
	}

	internal void SetPlayer(int connectionId, Player player)
	{
		if (_clientConnections.ContainsKey(connectionId))
		{
			_clientConnections[connectionId].Player = player;
		}
		else
		{
			Dbg.Error("Error setting player for client", connectionId);
		}
	}

	internal Player GetPlayer(int connectionId)
	{
		if (_clientConnections.ContainsKey(connectionId) && _clientConnections[connectionId].Player != null)
		{
			return _clientConnections[connectionId].Player;
		}
		else
		{
			Dbg.Error("Error getting player for client", connectionId);
		}

		return null;
	}

	internal Player[] GetAllPlayers()
	{
		List<Player> list = new();
		foreach (Client client in _clientConnections.Values)
		{
			if (client.Player != null)
			{
				list.Add(client.Player);
			}
		}

		return list.ToArray();
	}

	private void OnConnected(int connectionId)
	{
		string ipAddress = _server.GetClientAddress(connectionId);
#if HELLION_SP
		if (!ipAddress.Contains("127.0.0.1"))
		{
			Dbg.Error("Non-local client with ip", ipAddress, "tried to connect to server.");
			_server.Disconnect(connectionId);
			return;
		}
#endif

		Dbg.Info("Client connected", Server.IsRunning, connectionId, ipAddress);

		try
		{
			NetworkController.Instance.AddBareClient(connectionId);
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	private void OnData(int connectionId, ArraySegment<byte> message)
	{
		try
		{
			NetworkData networkData = Serializer.Unpackage(new MemoryStream(message.Array));
			if (networkData != null)
			{
				if (_clientConnections[connectionId].Player != null)
				{
					networkData.Sender = _clientConnections[connectionId].Player.GUID;
				}
				else
				{
					networkData.Sender = connectionId;
				}

				EventSystem.Instance.Invoke(networkData);
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
			NetworkController.Instance.DisconnectClient(connectionId);
		}
	}

	private void OnDisconnected(int connectionId)
	{
		if (_clientConnections.TryGetValue(connectionId, out Client client))
		{
			client.Player?.RemovePlayerFromTrigger();
			_clientConnections.Remove(connectionId);
			NetworkController.Instance.OnDisconnect(connectionId);
		}

		Dbg.Info("Client disconnected", Server.IsRunning, connectionId);
	}
}
