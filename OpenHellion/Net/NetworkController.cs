using System;
using System.Collections.Generic;
using System.Linq;
using OpenHellion.Net.Message.MainServer;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Net;

public static class NetworkController
{
	private static readonly GsConnection GameConnection;

	internal static readonly Dictionary<long, int> Clients = new();

	private static StatusConnectionListener _statusPortConnectionListener;

	public static string ServerId = null;

	static NetworkController()
	{
		GameConnection = new GsConnection();
		EventSystem.AddListener(typeof(LogInRequest), LogInRequestListener);
		EventSystem.AddListener(typeof(LogOutRequest), LogOutRequestListener);
	}

	public static void DisconnectClient(long guid)
	{
		if (Clients.ContainsKey(guid))
		{
			GameConnection.Disconnect(Clients[guid]);
			Clients.Remove(guid);

			Debug.Log("Disconnecting user", guid);
		}
		else
		{
			Debug.Error("Tried to disconnect client that isn't registered as connected", guid);
		}
	}

	/// <summary>
	/// 	Disconnect all currently connected clients.<br />
	/// 	Stops their connections, logging them out, and removes them from our list of active clients.
	/// </summary>
	public static void DisconnectAllClients()
	{
		GameConnection.DisconnectAll();
		Clients.Clear();
	}

	public static void Tick()
	{
		if (GameConnection != null)
		{
			GameConnection.Tick();
		}
		else
		{
			Debug.Warning("Tried to tick before game connection has been created.");
		}
	}

	public static void SendCharacterSpawnToOtherPlayers(Player spawnedPlayer)
	{
		if (!Clients.ContainsKey(spawnedPlayer.GUID))
		{
			return;
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		res.Data.Add(spawnedPlayer.GetSpawnResponseData(null));
		foreach (Player player in GetAllPlayers())
		{
			if (player != null && player != spawnedPlayer && player.IsAlive && player.EnvironmentReady && player.IsSubscribedTo(spawnedPlayer, checkParent: true))
			{
				SendToGameClient(player.GUID, res);
			}
		}
	}

	public static void AddCharacterSpawnsToResponse(Player pl, ref SpawnObjectsResponse res)
	{
		if (pl == null)
		{
			return;
		}
		foreach (Player other in Server.Instance.AllPlayers)
		{
			if (pl != other && other.IsAlive && pl.IsSubscribedTo(other, checkParent: true))
			{
				res.Data.Add(other.GetSpawnResponseData(pl));
			}
		}
	}

	private static void LogInRequestListener(NetworkData data)
	{
		LogInRequest req = data as LogInRequest;
		try
		{
			System.Diagnostics.Debug.Assert(req != null, nameof(req) + " != null");
			Debug.Log("Received login request for player with id", req.PlayerId);

			if (Clients.Count >= Server.Instance.MaxPlayers)
			{
				Debug.Error("Maximum number of players exceeded.", req.ServerID, ServerId);
				SendToGameClient(req.Sender, new LogInResponse
				{
					Response = ResponseResult.Error
				});
				return;
			}

			if (req.ClientHash != Server.CombinedHash)
			{
				Debug.Info("Client/server hash mismatch.", req.ClientHash, Server.CombinedHash);
				SendToGameClient(req.Sender, new LogInResponse
				{
					Response = ResponseResult.ClientVersionError
				});
				return;
			}

			// Also has the added benefit of blocking players from joining non-nakama servers.
			if (req.ServerID != ServerId)
			{
				Debug.Info("LogInRequest server ID doesn't match this server ID.", req.ServerID, ServerId);
				SendToGameClient(req.Sender, new LogInResponse
				{
					Response = ResponseResult.Error
				});
				return;
			}

			// Check if player id is valid.
			if (!Guid.TryParse(req.PlayerId, out _))
			{
				Debug.Info("Player id isn't valid.", req.ServerID, ServerId);
				SendToGameClient(req.Sender, new LogInResponse
				{
					Response = ResponseResult.Error
				});
				return;
			}

			long guid = GUIDFactory.PlayerIdToGuid(req.PlayerId);
			if (PatchClient(guid, req.Sender))
			{
				Server.Instance.LoginPlayer(guid, req.PlayerId, req.CharacterData);
			}
			else
			{
				Debug.Error("Could not patch client.");
			}
		}
		catch (Exception ex)
		{
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});

			Debug.Log(ex.Message, ex.StackTrace);
		}
	}

	public static void Start()
	{
		_statusPortConnectionListener = new StatusConnectionListener();
		_statusPortConnectionListener.Start(Server.StatusPort);
		GameConnection.Start(Server.GamePort);
	}

	public static void SendToGameClient(long clientId, NetworkData data)
	{
		if (Clients.TryGetValue(clientId, out var client))
		{
			GameConnection.Send(client, data);
		}
		else
		{
			Debug.Error("Tried to send data to nonexistent client", clientId);
		}
	}

	private static void LogOutRequestListener(NetworkData data)
	{
		if (Clients.ContainsKey(data.Sender))
		{
			LogOutRequest lor = data as LogOutRequest;
			System.Diagnostics.Debug.Assert(lor != null, nameof(lor) + " != null");
			LogOutPlayer(lor.Sender);
			GameConnection.ClearEverythingAndSend(Clients[lor.Sender], new LogOutResponse
			{
				Sender = 0L,
				Response = ResponseResult.Success
			});
		}
		else
		{
			Debug.Error("Error when logging out player. No client is connected with id", data.Sender);
		}
	}

	public static void LogOutPlayer(long guid)
	{
		if (Clients.TryGetValue(guid, out var client))
		{
			GameConnection.GetPlayer(client).LogoutDisconnectReset();
		}
		else
		{
			Debug.Error("Trying to log out player failed", guid);
		}
	}

	/// <summary>
	/// 	Patch a client by properly adding a guid.<br />
	/// 	A partial client is created when connected, but it doesn't have the proper guid.
	/// </summary>
	private static bool PatchClient(long guid, long temporaryId)
	{
		// If client already exists on server, disconnect it.
		if (Clients.ContainsKey(guid))
		{
			Debug.Error("Disconnecting client because it already is listed as a client.");
			try
			{
				DisconnectClient(guid);
				return false;
			}
			catch (Exception ex)
			{
				Debug.Error("Couldn't disconnect client when patching:", ex);
				return false;
			}
		}

		// Convert sender into a proper client.
		if (Clients.TryGetValue(temporaryId, out int connectionId))
		{
			Clients.Add(guid, connectionId);
			Clients.Remove(temporaryId);
			Debug.Log("Converted client shell into proper client.");
			return true;
		}

		Debug.Error("Failed to patch new client. Temporary id not found.");

		return false;
	}

	// Create a bare client without guid and a player.
	// Has to be here because m_Clients shouldn't be exposed.
	internal static void AddBareClient(int connectionId)
	{
		long temporaryId = -1L;
		while (Clients.ContainsKey(temporaryId))
		{
			temporaryId--;
		}

		Clients.Add(temporaryId, connectionId);
		GameConnection.AddBareClient(connectionId);
	}

	/// <summary>
	/// 	Get a player with a specified guid.
	/// </summary>
	public static Player GetPlayer(long guid)
	{
		if (Clients.TryGetValue(guid, out var client))
		{
			return GameConnection.GetPlayer(client);
		}

		return null;
	}

	/// <summary>
	/// 	Get a list of all the players on the server.
	/// </summary>
	public static Player[] GetAllPlayers()
	{
		return GameConnection.GetAllPlayers();
	}

	public static void ConnectPlayer(Player player)
	{
		if (!player.PlayerReady || !player.EnvironmentReady)
		{
			player.Initialize = true;
		}

		Debug.Log("Connecting player", player.Name, player.GUID);
		if (Clients.ContainsKey(player.GUID))
		{
			player.ConnectToNetworkController();
			GameConnection.SetPlayer(Clients[player.GUID], player);

			LogInResponse lir = new LogInResponse
			{
				GUID = player.FakeGuid,
				Data = new CharacterData
				{
					Name = player.Name,
					Gender = player.Gender,
					HairType = player.HairType,
					HeadType = player.HeadType
				},
				ServerTime = Server.Instance.SolarSystem.CurrentTime,
				IsAlive = player.IsAlive,
				CanContinue = player.AuthorizedSpawnPoint != null,
				DebrisFields = Server.Instance.GetDebrisFieldsDetails(),
				ItemsIngredients = StaticData.ItemsIngredients,
				Quests = StaticData.QuestsData,
				ExposureRange = StaticData.SolarSystem.ExposureRange,
				VesselExposureValues = StaticData.SolarSystem.VesselExposureValues,
				PlayerExposureValues = StaticData.SolarSystem.PlayerExposureValues,
				VesselDecayRateMultiplier = Server.VesselDecayRateMultiplier
			};

			if (!player.IsAlive)
			{
				lir.SpawnPointsList = Server.Instance.GetAvailableSpawnPoints(player);
			}

			Debug.Log("Sent login response.");

			SendToGameClient(player.GUID, lir);
		}
		else
		{
			Debug.Error("Client list doesn't contain player", player.Name);
		}
	}

	/// <summary>
	/// 	Send a message to all clients.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public static void SendToAllClients(NetworkData data, long skipPlayerGuid = -1L)
	{
		GameConnection.SendToAll(data, skipPlayerGuid);
	}

	/// <summary>
	/// 	Send a message to all clients subscribed to a space object.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public static void SendToClientsSubscribedTo(NetworkData data, long skipPlayerGuid = -1L, params SpaceObject[] spaceObjects)
	{
		if (spaceObjects.Length == 0)
		{
			return;
		}
		foreach (Player player in from m in GetAllPlayers()
			where m != null && m.GUID != skipPlayerGuid
			select m)
		{
			if (player.IsAlive && player.EnvironmentReady)
			{
				if (spaceObjects.Any((SpaceObject m) => m != null && player.IsSubscribedTo(m, checkParent: false)))
				{
					SendToGameClient(player.GUID, data);
				}
			}
			else if (!player.EnvironmentReady && data is ShipStatsMessage message && player.IsSubscribedTo(message.GUID))
			{
				player.MessagesReceivedWhileLoading.Enqueue(message);
			}
		}
	}

	public static void SendToClientsSubscribedToParents(NetworkData data, SpaceObject spaceObject, long skipPlayerGuid = -1L, int depth = 4)
	{
		List<SpaceObject> parents = new List<SpaceObject>
		{
			spaceObject
		};

		SpaceObject tmpParent = spaceObject.Parent;
		while (tmpParent != null && depth > 0)
		{
			parents.Add(tmpParent);
			tmpParent = tmpParent.Parent;
			depth--;
		}
		SendToClientsSubscribedTo(data, skipPlayerGuid, parents.ToArray());
	}

	private static void OnApplicationQuit()
	{
		DisconnectAllClients();
		_statusPortConnectionListener.Stop();
		GameConnection.Stop();
	}

	// Second part of disconnecting. Called by OnDisconnect in ConnectionGame.
	// Has to be here because _client shouldn't be exposed.
	internal static void OnDisconnect(int connectionId)
	{
		// Get a single guid from the array's values, and remove the guid from this array.
		if (Clients.ContainsValue(connectionId))
		{
			long guid = Clients.FirstOrDefault(entry => entry.Value == connectionId).Key;
			Clients.Remove(guid);
		}
		else
		{
			Debug.Error("Tried to disconnect client not in client list.");
		}
	}

	/// <summary>
	/// 	Simple check to see if a client is connected.
	/// </summary>
	public static bool ContainsClient(long guid)
	{
		return Clients.ContainsKey(guid);
	}
}
