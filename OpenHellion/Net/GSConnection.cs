// GSConnection.cs
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

namespace OpenHellion.Networking;

/// <summary>
/// 	Handles connections to the clients.<br />
/// 	This works as an abstraction layer between the controller and the networking library.
/// 	Most funcions in this method is absracted by the <c>NetworkController</c>
/// </summary>
internal class GSConnection
{

	private readonly Dictionary<int, Player> m_ClientConnections = new();

	private Telepathy.Server m_Server;

	internal void Start(int port)
	{
		Telepathy.Log.Info = Dbg.Info;
		Telepathy.Log.Warning = Dbg.Warning;
		Telepathy.Log.Error = Dbg.Error;

		m_Server = new(80000)
		{
			OnConnected = OnConnected,
			OnData = OnData,
			OnDisconnected = OnDisconnected,
			SendQueueLimit = 1000,
			ReceiveQueueLimit = 1000
#if DEBUG || HELLION_SP
			,SendTimeout = 0
			,ReceiveTimeout = 0
#endif
		};

		m_Server.Start(port);

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
			bool success = m_Server.Send(connectionId, binary);

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
		foreach (KeyValuePair<int, Player> pl in m_ClientConnections)
		{
			Player player = pl.Value;
			if (player != null && player.IsAlive && player.EnvironmentReady && player.GUID != skipPlayerGUID)
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
		if (m_ClientConnections.ContainsKey(connectionId))
		{
			// If there is a player, make sure it has been disconnected.
			Player player = m_ClientConnections[connectionId];
			player?.LogoutDisconnectReset();
			player?.DiconnectFromNetworkContoller();

			// Remove it.
			m_ClientConnections.Remove(connectionId);
		}
		else
		{
			Dbg.Error("Tried to remove non-existent player with connection id", connectionId);
		}

		m_Server.Disconnect(connectionId);
	}

	// Disconnects all clients.
	internal void DisconnectAll()
	{
		// Loop through all client and do the neccecary actions for them to exit.
		foreach (KeyValuePair<int, Player> pl in m_ClientConnections)
		{
			m_Server.Disconnect(pl.Key);
			if (pl.Value != null)
			{
				pl.Value.LogoutDisconnectReset();
				pl.Value.DiconnectFromNetworkContoller();
			}
		}

		// Clean.
		m_ClientConnections.Clear();
	}

	internal void Tick()
	{
		m_Server.Tick(50);
	}

	internal void Stop()
	{
		NetworkController.Instance.DisconnectAllClients();
		m_Server.Stop();
	}

	// Create a bare client with a temporary id.
	// Partial implementation of the corresponding function in NetworkController.
	internal void AddBareClient(int connectionId)
	{
		m_ClientConnections.Add(connectionId, null);
	}

	internal void SetPlayer(int connectionId, Player player)
	{
		if (m_ClientConnections.ContainsKey(connectionId))
		{
			m_ClientConnections[connectionId] = player;
		}
		else
		{
			Dbg.Error("Error setting player for client", connectionId);
		}
	}

	internal Player GetPlayer(int connectionId)
	{
		if (m_ClientConnections.ContainsKey(connectionId) && m_ClientConnections[connectionId] != null)
		{
			return m_ClientConnections[connectionId];
		}
		else
		{
			Dbg.Error("Error getting player for client", connectionId);
		}

		return null;
	}

	internal Player[] GetAllPlayers()
	{
		return m_ClientConnections.Values.ToList().ToArray();
	}

	private void OnConnected(int connectionId)
	{
		string ipAddress = m_Server.GetClientAddress(connectionId);
#if HELLION_SP
		if (!ipAddress.Contains("127.0.0.1"))
		{
			Dbg.Error("Non-local client with ip", ipAddress, "tried to connect to server.");
			m_Server.Disconnect(connectionId);
			return;
		}
#endif

		Dbg.Log("Client connected", connectionId, ipAddress);

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
				if (m_ClientConnections[connectionId] != null)
				{
					networkData.Sender = m_ClientConnections[connectionId].GUID;
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
		if (m_ClientConnections.TryGetValue(connectionId, out Player player))
		{
			player.RemovePlayerFromTrigger();
			m_ClientConnections.Remove(connectionId);
			NetworkController.Instance.OnDisconnect(connectionId);
		}

		Dbg.Info("Client disconnected", Server.IsRunning, connectionId);
	}
}
