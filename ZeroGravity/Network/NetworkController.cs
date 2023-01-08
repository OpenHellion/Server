using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using ZeroGravity.Objects;

namespace ZeroGravity.Network;

public class NetworkController
{
	public class Client
	{
		public long ClientGUID;

		public Socket TcpClientSocket;

		public Player Player = null;

		public GameClientThread Thread = null;

		public Client(Socket soc, GameClientThread th)
		{
			TcpClientSocket = soc;
			Thread = th;
		}
	}

	private ConcurrentDictionary<long, Client> clientList = new ConcurrentDictionary<long, Client>();

	public Dictionary<long, Client> ClientList = new Dictionary<long, Client>();

	private GameClientConnectionListener gameClientConnectionListener;

	private StatusPortConnectionListener statusPortConnectionListener;

	public string MainServerAddres = "188.166.144.65";

	public int MainServerPort = 6001;

	private MainServerThreads mainServerThreads;

	public string ServerID = "";

	public EventSystem EventSystem;

	public NetworkController()
	{
		EventSystem = new EventSystem();
		mainServerThreads = new MainServerThreads();
		EventSystem.AddListener(typeof(LogInRequest), LogInRequestListener);
		EventSystem.AddListener(typeof(LogOutRequest), LogOutRequestListener);
	}

	public short CurrentOnlinePlayers()
	{
		return (short)ClientList.Count;
	}

	public void SendDestroyPlayerMessage(long guid, long fakeGUID)
	{
		foreach (KeyValuePair<long, Client> client in ClientList)
		{
			if (client.Value.Player != null && client.Value.Player.GUID != guid)
			{
				DestroyObjectMessage dom = new DestroyObjectMessage();
				dom.ObjectType = SpaceObjectType.Player;
				dom.ID = fakeGUID;
				SendToGameClient(client.Value.Player.GUID, dom);
			}
		}
	}

	public void DisconnectClient(long guid)
	{
		if (ClientList.ContainsKey(guid))
		{
			DisconnectClient(ClientList[guid]);
		}
	}

	public void DisconnectClient(Client cl)
	{
		if (cl.Player != null)
		{
			cl.Player.LogoutDisconnectReset();
			cl.Player.DiconnectFromNetworkContoller();
			cl.Thread.Stop();
			if (ClientList.ContainsKey(cl.Player.GUID))
			{
				RemoveClient(cl.Player.GUID);
			}
		}
		else
		{
			cl.Thread.Stop();
			if (ClientList.ContainsKey(cl.ClientGUID))
			{
				RemoveClient(cl.ClientGUID);
			}
		}
	}

	public void DisconnectAllClients()
	{
		foreach (long clientID in ClientList.Keys)
		{
			DisconnectClient(clientID);
		}
	}

	public void SendCharacterSpawnToOtherPlayers(Player spawnedPlayer)
	{
		if (!ClientList.ContainsKey(spawnedPlayer.GUID))
		{
			return;
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		res.Data.Add(spawnedPlayer.GetSpawnResponseData(null));
		foreach (KeyValuePair<long, Client> client in ClientList)
		{
			Player player = client.Value.Player;
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
		if (Server.Instance.NetworkController.CurrentOnlinePlayers() >= Server.Instance.MaxPlayers)
		{
			Dbg.Warning("Maximum number of players exceeded.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}
		if (req.ClientHash != Server.CombinedHash)
		{
			Dbg.Warning("Server/client hash mismatch.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.Error
			});
			return;
		}

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
		if (req.Password != Server.Instance.ServerPassword)
		{
			Dbg.Warning("LogInRequest server password doesn't match this server's password.", req.ServerID, ServerID);
			SendToGameClient(req.Sender, new LogInResponse
			{
				Response = ResponseResult.WrongPassword
			});
			return;
		}
		long guid = GUIDFactory.SteamIdToGuid(req.SteamId);
		if (ClientList.ContainsKey(guid))
		{
			try
			{
				if (ClientList.ContainsKey(guid))
				{
					Server.Instance.NetworkController.DisconnectClient(ClientList[guid]);
				}
				if (ClientList.ContainsKey(req.Sender))
				{
					Server.Instance.NetworkController.DisconnectClient(ClientList[req.Sender]);
				}
				return;
			}
			catch (Exception)
			{
				if (ClientList.ContainsKey(guid))
				{
					DisconnectClient(guid);
				}
				if (ClientList.ContainsKey(req.Sender))
				{
					ClientList[req.Sender].ClientGUID = guid;
				}
				AddClient(guid, ClientList[req.Sender]);
				RemoveClient(req.Sender);
				Server.Instance.LoginPlayer(guid, req.SteamId, req.CharacterData);
				return;
			}
		}
		if (ClientList.ContainsKey(req.Sender))
		{
			ClientList[req.Sender].ClientGUID = guid;
		}
		AddClient(guid, ClientList[req.Sender]);
		RemoveClient(req.Sender);
		Server.Instance.LoginPlayer(guid, req.SteamId, req.CharacterData);
	}

	public void Start()
	{
		gameClientConnectionListener = new GameClientConnectionListener();
		gameClientConnectionListener.Start(Server.GamePort);
		statusPortConnectionListener = new StatusPortConnectionListener();
		statusPortConnectionListener.Start(Server.StatusPort);
	}

	public void SendToMainServer(NetworkData data)
	{
		mainServerThreads.Send(data);
	}

	public Client AddClient(Socket c, GameClientThread thr)
	{
		Client cl = new Client(c, thr);
		long tempID = -1L;
		try
		{
			while (ClientList.ContainsKey(tempID))
			{
				tempID--;
			}
		}
		finally
		{
			cl.ClientGUID = tempID;
			AddClient(tempID, cl);
		}
		return cl;
	}

	private void AddClient(long id, Client client)
	{
		clientList[id] = client;
		ClientList = clientList.ToDictionary((KeyValuePair<long, Client> k) => k.Key, (KeyValuePair<long, Client> v) => v.Value);
	}

	private void RemoveClient(long sender)
	{
		clientList.TryRemove(sender, out var _);
		ClientList = clientList.ToDictionary((KeyValuePair<long, Client> k) => k.Key, (KeyValuePair<long, Client> v) => v.Value);
	}

	public void SendToGameClient(long clientID, NetworkData data)
	{
		if (ClientList.ContainsKey(clientID))
		{
			data.Sender = 0L;
			ClientList[clientID].Thread.Send(data);
		}
	}

	public void LogOutRequestListener(NetworkData data)
	{
		if (ClientList.ContainsKey(data.Sender))
		{
			LogOutRequest lor = data as LogOutRequest;
			LogOutPlayer(lor.Sender);
			ClientList[data.Sender].Thread.ClearEverytingAndSend(new LogOutResponse
			{
				Sender = 0L,
				Response = ResponseResult.Success
			});
		}
	}

	public void LogOutPlayer(long GUID)
	{
		if (ClientList.ContainsKey(GUID) && ClientList[GUID].Player != null)
		{
			ClientList[GUID].Player.LogoutDisconnectReset();
		}
	}

	public void ConnectPlayer(Player player, bool doLogin)
	{
		if (!player.PlayerReady || !player.EnvironmentReady)
		{
			player.Initialize = true;
		}
		if (ClientList.ContainsKey(player.GUID))
		{
			ClientList[player.GUID].Player = player;
			ClientList[player.GUID].Player.ConnectToNetworkController();
			LogInResponse lir = new LogInResponse();
			lir.ID = player.FakeGuid;
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
	}

	public void SendToAllClients(NetworkData data, long skipPlayerGUID = -1L)
	{
		foreach (KeyValuePair<long, Client> client in ClientList)
		{
			Player player = client.Value.Player;
			if (player != null && player.IsAlive && player.EnvironmentReady && player.GUID != skipPlayerGUID)
			{
				SendToGameClient(client.Value.Player.GUID, data);
			}
		}
	}

	public void SendToClientsSubscribedToSenderFirst(NetworkData data, long senderGUID, params SpaceObject[] spaceObjects)
	{
		if (spaceObjects.Length == 0)
		{
			return;
		}
		Player player = ClientList.Select((KeyValuePair<long, Client> m) => m.Value.Player).FirstOrDefault((Player m) => m.GUID == senderGUID);
		if (player != null)
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
		SendToClientsSubscribedTo(data, senderGUID, spaceObjects);
	}

	public void SendToClientsSubscribedTo(NetworkData data, long skipPlalerGUID = -1L, params SpaceObject[] spaceObjects)
	{
		if (spaceObjects.Length == 0)
		{
			return;
		}
		foreach (Player player in from m in ClientList
			select m.Value.Player into m
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
		statusPortConnectionListener.Stop();
		gameClientConnectionListener.Stop();
		foreach (KeyValuePair<long, Client> client in ClientList)
		{
			client.Value.Thread.Stop();
			if (client.Value.Player != null)
			{
				client.Value.Player.LogoutDisconnectReset();
				client.Value.Player.DiconnectFromNetworkContoller();
			}
		}
		ClientList.Clear();
	}

	public long GetClientGuid(Client c)
	{
		foreach (KeyValuePair<long, Client> kv in ClientList)
		{
			if (kv.Value == c)
			{
				return kv.Key;
			}
		}
		throw new Exception("Client not found");
	}
}
