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
	private ConnectionGame _gameConnection;

	private Dictionary<long, int> _clients = new();

	private ConnectionGameStatusListener statusPortConnectionListener;

	public static string ServerID = null;

	private static NetworkController s_instance;
	public static NetworkController Instance
	{
		get
		{
			if (s_instance == null)
			{
				Dbg.Info("Creating new network controller instance.");
				s_instance = new NetworkController();
				return s_instance;
			}

			return s_instance;
		}
	}

	public NetworkController()
	{
		_gameConnection = new ConnectionGame();
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
			RemoveClient(guid);

			_gameConnection.Disconnect(_clients[guid]);
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
		Dbg.Log("Executing log in request.");

		if (Instance.CurrentOnlinePlayers() >= Server.Instance.MaxPlayers)
		{
			Dbg.Warning("Maximum number of players exceeded.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}

#if false
		// TODO: Ignoring this for now.
		if (req.ClientHash != Server.CombinedHash)
		{
			Dbg.Warning("Server/client hash mismatch.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.ClientVersionError
			});
			return;
		}
#endif

#if !HELLION_SP
		if (req.ServerID != ServerID)
		{
			Dbg.Warning("LogInRequest server ID doesn't match this server ID.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}
#endif
		if (req.Password == null)
		{
			req.Password = "";
		}

		// Password check.
		if (req.Password != Server.Instance.ServerPassword)
		{
			Dbg.Warning("LogInRequest server password doesn't match this server's password.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.WrongPassword
			});
			return;
		}

		// Check if player id is valid.
		if (!Guid.TryParse(req.PlayerId, out _))
		{
			Dbg.Warning("Player id isn't valid.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}

		// TODO: Guid could probably be replaced with player id.
		long guid = GUIDFactory.PlayerIdToGuid(req.PlayerId);
		if (PatchClient(guid, req.Sender))
		{
			Server.Instance.LoginPlayer(guid, req.PlayerId, req.NativeId, req.CharacterData);
		}
		else
		{
			Dbg.Error("Could not patch client.");
		}
	}

	public void Start()
	{
		statusPortConnectionListener = new ConnectionGameStatusListener();
		statusPortConnectionListener.Start(Server.StatusPort);
		_gameConnection.Start(Server.GamePort);
	}

	public void SendToGameClient(long clientID, NetworkData data)
	{
		if (_clients.ContainsKey(clientID))
		{
			_gameConnection.Send(_clients[clientID], data);
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
		if (_clients.ContainsKey(guid))
		{
			_gameConnection.GetPlayer(_clients[guid]).LogoutDisconnectReset();
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
	internal bool PatchClient(long guid, long sender)
	{
		// If client already exists on server, disconnect it.
		if (_clients.ContainsKey(guid))
		{
			try
			{
				DisconnectClient(guid);
				DisconnectClient(sender);
				return false;
			}
			catch (Exception)
			{
				int? sc = null;
				long? usedId = null;
				if (_clients.ContainsKey(guid))
				{
					DisconnectClient(guid);
					sc = _clients[guid];
					usedId = guid;
				}

				if (_clients.ContainsKey(sender))
				{
					sc = _clients[sender];
					usedId = sender;
				}

				if (!sc.HasValue || !usedId.HasValue)
				{
					return false;
				}

				_gameConnection.PatchClient(sc.Value, guid);

				_clients.Add(guid, sc.Value);
				_clients.Remove(usedId.Value);
			}
		}

		// Convert sender into a proper client.
		long? tempId = _clients.Values.Single(entry => entry == (int) sender);
		if (tempId.HasValue)
		{
			// Sender is connection id.
			_gameConnection.PatchClient((int) sender, guid);

			_clients.Add(guid, (int) sender);
			_clients.Remove(tempId.Value);
		}

		return true;
	}

	// Create a bare client without guid and a player.
	// Has to be here because _client shouldn't be exposed.
	internal void AddBareClient(int connectionId)
	{
		long tempID = -1L;
		while (_clients.ContainsKey(tempID))
		{
			tempID--;
		}

		_clients.Add(tempID, connectionId);
		_gameConnection.AddBareClient(connectionId, tempID);
	}

	/// <summary>
	/// 	Remove all references to a client.<br />
	/// 	This also disconnects the client's player, but does not disconnect the client. <br />
	/// 	Used by the disconnect function.
	/// </summary>
	public void RemoveClient(long guid)
	{
		if (_clients.ContainsKey(guid))
		{
			_gameConnection.RemoveClient(_clients[guid]);
			_clients.Remove(guid);
		}
		else
		{
			Dbg.Error("Tried to remove non-existent client with guid", guid);
		}
	}

	/// <summary>
	/// 	Get a player with a specified guid.
	/// </summary>
	public Player GetPlayer(long guid)
	{
		if (_clients.ContainsKey(guid))
		{
			return _gameConnection.GetPlayer(_clients[guid]);
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
		if (_clients.ContainsKey(player.GUID))
		{
			player.ConnectToNetworkController();
			_gameConnection.SetPlayer(_clients[player.GUID], player);
			LogInResponse lir = new LogInResponse();
			lir.GUID = player.FakeGuid;
			lir.Data = new CharacterData
			{
				Name = player.Name,
				Gender = player.Gender,
				HairType = player.HairType,
				HeadType = player.HeadType
			};
			lir.ServerTime = Server.Instance.SolarSystem.CurrentTime;
			lir.IsAlive = player.IsAlive;
			lir.CanContinue = player.AuthorizedSpawnPoint != null;
			if (!player.IsAlive)
			{
				lir.SpawnPointsList = Server.Instance.GetAvailableSpawnPoints(player);
			}
			lir.DebrisFields = Server.Instance.GetDebrisFieldsDetails();
			lir.ItemsIngredients = StaticData.ItemsIngredients;
			lir.Quests = StaticData.QuestsData;
			lir.ExposureRange = StaticData.SolarSystem.ExposureRange;
			lir.VesselExposureValues = StaticData.SolarSystem.VesselExposureValues;
			lir.PlayerExposureValues = StaticData.SolarSystem.PlayerExposureValues;
			lir.VesselDecayRateMultiplier = Server.VesselDecayRateMultiplier;
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
				if (spaceObjects.Count((SpaceObject m) => m != null && player.IsSubscribedTo(m, checkParent: false)) > 0)
				{
					SendToGameClient(player.GUID, data);
				}
			}
			else if (!player.EnvironmentReady && data is ShipStatsMessage && player.IsSubscribedTo((data as ShipStatsMessage).GUID))
			{
				player.MessagesReceivedWhileLoading.Enqueue(data as ShipStatsMessage);
			}
		}
	}

	public void SendToClientsSubscribedToParents(NetworkData data, SpaceObject spaceObject, long skipPlalerGUID = -1L, int depth = 4)
	{
		List<SpaceObject> parents = new List<SpaceObject>();
		parents.Add(spaceObject);
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
		statusPortConnectionListener.Stop();
		_gameConnection.Stop();
	}

	// Second part of disconnecting. Called by OnDisconnect in ConnectionGame.
	// Has to be here because _client shouldn't be exposed.
	internal void OnDisconnect(int connectionId)
	{
		// Get guid by searching through the values.
		if (_clients.ContainsValue(connectionId))
		{
			long guid = _clients.Values.Single(entry => entry == connectionId);
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
