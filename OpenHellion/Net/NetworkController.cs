using System;
using System.Collections.Generic;
using System.Linq;
using OpenHellion.Networking.Message.MainServer;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Networking;

public class NetworkController
{
	private GSConnection _gameConnection;

	private readonly Dictionary<long, int> _clients = new();

	private StatusConnectionListener _statusPortConnectionListener;

	public static string ServerId = null;

	private static NetworkController _instance;
	public static NetworkController Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = new NetworkController();
				return _instance;
			}

			return _instance;
		}
	}

	private NetworkController()
	{
		_gameConnection = new GSConnection();
		EventSystem.AddListener(typeof(LogInRequest), LogInRequestListener);
		EventSystem.AddListener(typeof(LogOutRequest), LogOutRequestListener);
	}

	public short CurrentOnlinePlayers()
	{
		return (short)_clients.Count;
	}

	public void DisconnectClient(long guid)
	{
		if (_clients.ContainsKey(guid))
		{
			_gameConnection.Disconnect(_clients[guid]);
			_clients.Remove(guid);

			Dbg.Log("Disconnecting user", guid);
		}
		else
		{
			Dbg.Error("Tried to disconnect client that isn't registered as connected", guid);
		}
	}

	/// <summary>
	/// 	Disconnect all currently connected clients.<br />
	/// 	Stops their connections, logging them out, and removes them from our list of active clients.
	/// </summary>
	public void DisconnectAllClients()
	{
		_gameConnection.DisconnectAll();
		_clients.Clear();
	}

	public void Tick()
	{
		if (_gameConnection != null)
		{
			_gameConnection.Tick();
		}
		else
		{
			Dbg.Warning("Tried to tick before game connection has been created.");
		}
	}

	public void SendCharacterSpawnToOtherPlayers(Player spawnedPlayer)
	{
		if (!_clients.ContainsKey(spawnedPlayer.GUID))
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

	public void AddCharacterSpawnsToResponse(Player pl, ref SpawnObjectsResponse res)
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

	public void LogInRequestListener(NetworkData data)
	{
		LogInRequest req = data as LogInRequest;
		Dbg.Log("Recieved login request for player with PlayerId", req.PlayerId);

		if (Instance.CurrentOnlinePlayers() >= Server.Instance.MaxPlayers)
		{
			Dbg.Warning("Maximum number of players exceeded.", req.ServerID, ServerId);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}

		if (req.ClientHash != Server.CombinedHash)
		{
			Dbg.Info("Server/client hash mismatch.", req.ServerID, ServerId);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.ClientVersionError
			});
			return;
		}

		// Also has the added benefit of blocking players from joining non-nakama servers.
		if (req.ServerID != ServerId)
		{
			Dbg.Info("LogInRequest server ID doesn't match this server ID.", req.ServerID, ServerId);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}

		// Check if player id is valid.
		if (!Guid.TryParse(req.PlayerId, out _))
		{
			Dbg.Info("Player id isn't valid.", req.ServerID, ServerId);
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
			Dbg.Error("Could not patch client.");
		}
	}

	public void Start()
	{
		_statusPortConnectionListener = new StatusConnectionListener();
		_statusPortConnectionListener.Start(Server.StatusPort);
		_gameConnection.Start(Server.GamePort);
	}

	public void SendToGameClient(long clientID, NetworkData data)
	{
		if (_clients.TryGetValue(clientID, out var client))
		{
			_gameConnection.Send(client, data);
		}
		else
		{
			Dbg.Error("Tried to send data to nonexistent client", clientID);
		}
	}

	public void LogOutRequestListener(NetworkData data)
	{
		if (_clients.ContainsKey(data.Sender))
		{
			LogOutRequest lor = data as LogOutRequest;
			LogOutPlayer(lor.Sender);
			_gameConnection.ClearEverythingAndSend(_clients[lor.Sender], new LogOutResponse
			{
				Sender = 0L,
				Response = ResponseResult.Success
			});
		}
		else
		{
			Dbg.Error("Error when logging out player. No client is connected with id", data.Sender);
		}
	}

	public void LogOutPlayer(long guid)
	{
		if (_clients.TryGetValue(guid, out var client))
		{
			_gameConnection.GetPlayer(client).LogoutDisconnectReset();
		}
		else
		{
			Dbg.Error("Trying to log out player failed", guid);
		}
	}

	/// <summary>
	/// 	Patch a client by properly adding a guid.<br />
	/// 	A partial client is created when connected, but it doesn't have the proper guid.
	/// </summary>
	// TODO: This is stupid
	internal bool PatchClient(long guid, long connectionId)
	{
		// If client already exists on server, disconnect it.
		if (_clients.ContainsKey(guid))
		{
			try
			{
				DisconnectClient(guid);
				return false;
			}
			catch (Exception ex)
			{
				Dbg.Error("Couldn't disconnect client when patching:", ex);
				return false;
			}
		}

		// Convert sender into a proper client.
		long? temporaryId = _clients.FirstOrDefault(entry => entry.Value == (int) connectionId).Key;
		if (temporaryId.HasValue)
		{
			_clients.Remove(temporaryId.Value);
			_clients.Add(guid, (int) connectionId);

			Dbg.Log("Converted client shell into proper client.");
		}

		return true;
	}

	// Create a bare client without guid and a player.
	// Has to be here because m_Clients shouldn't be exposed.
	internal void AddBareClient(int connectionId)
	{
		long temporaryID = -1L;
		while (_clients.ContainsKey(temporaryID))
		{
			temporaryID--;
		}

		_clients.Add(temporaryID, connectionId);
		_gameConnection.AddBareClient(connectionId);
	}

	/// <summary>
	/// 	Get a player with a specified guid.
	/// </summary>
	public Player GetPlayer(long guid)
	{
		if (_clients.TryGetValue(guid, out var client))
		{
			return _gameConnection.GetPlayer(client);
		}

		return null;
	}

	/// <summary>
	/// 	Get a list of all the players on the server.
	/// </summary>
	public Player[] GetAllPlayers()
	{
		return _gameConnection.GetAllPlayers();
	}

	public void ConnectPlayer(Player player)
	{
		if (!player.PlayerReady || !player.EnvironmentReady)
		{
			player.Initialize = true;
		}

		Dbg.Log("Connecting player", player.Name, player.GUID);
		if (_clients.ContainsKey(player.GUID))
		{
			player.ConnectToNetworkController();
			_gameConnection.SetPlayer(_clients[player.GUID], player);

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

			Dbg.Log("Sent login response.");

			SendToGameClient(player.GUID, lir);
		}
		else
		{
			Dbg.Error("Client list doesn't contain player", player.Name);
		}
	}

	/// <summary>
	/// 	Send a message to all clients.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public void SendToAllClients(NetworkData data, long skipPlayerGUID = -1L)
	{
		_gameConnection.SendToAll(data, skipPlayerGUID);
	}

	/// <summary>
	/// 	Send a message to all clients subscribed to a space object.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public void SendToClientsSubscribedTo(NetworkData data, long skipPlalerGUID = -1L, params SpaceObject[] spaceObjects)
	{
		if (spaceObjects.Length == 0)
		{
			return;
		}
		foreach (Player player in from m in GetAllPlayers()
			where m != null && m.GUID != skipPlalerGUID
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

	public void SendToClientsSubscribedToParents(NetworkData data, SpaceObject spaceObject, long skipPlalerGUID = -1L, int depth = 4)
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
		SendToClientsSubscribedTo(data, skipPlalerGUID, parents.ToArray());
	}

	private void OnApplicationQuit()
	{
		DisconnectAllClients();
		_statusPortConnectionListener.Stop();
		_gameConnection.Stop();
	}

	// Second part of disconnecting. Called by OnDisconnect in ConnectionGame.
	// Has to be here because _client shouldn't be exposed.
	internal void OnDisconnect(int connectionId)
	{
		// Get a single guid from the array's values, and remove the guid from this array.
		if (_clients.ContainsValue(connectionId))
		{
			long guid = _clients.FirstOrDefault(entry => entry.Value == connectionId).Key;
			_clients.Remove(guid);
		}
		else
		{
			Dbg.Error("Tried to disconnect client not in client list.");
		}
	}

	/// <summary>
	/// 	Simple check to see if a client is connected.
	/// </summary>
	public bool ContainsClient(long guid)
	{
		return _clients.ContainsKey(guid);
	}
}
