using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using OpenHellion.IO;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Net;

public static class NetworkController
{
	private static readonly GameTransport _transport;

	private static StatusConnectionListener _statusPortConnectionListener;

	public static string ServerId = null;

	public static int MaxPlayers = 100; // TODO: Really just a wrapper.

	static NetworkController()
	{
		_transport = new GameTransport(OnClientConnected, OnDisconnected, GetMaxPlayers);
		EventSystem.AddListener<LogOutRequest>(LogOutRequestListener);
	}

	public static async Task SendCharacterSpawnToOtherPlayers(Player spawnedPlayer)
	{
		if (!IsPlayerConnected(spawnedPlayer.Guid))
		{
			return;
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		res.Data.Add(spawnedPlayer.GetSpawnResponseData(null));
		foreach (Player player in GetAllConnectedPlayers())
		{
			if (player != null && player != spawnedPlayer && player.IsAlive && player.EnvironmentReady && player.IsSubscribedTo(spawnedPlayer, checkParent: true))
			{
				await Send(player.Guid, res);
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

	private static async Task<long> OnClientConnected(NetworkStream stream, long[] otherConnections)
	{
		try
		{
			var loginData = await ProtoSerialiser.Unpack(stream) as LogInRequest;
			if (loginData is null)
			{
				Debug.LogError("Connected client did not send loginrequest on connect.", loginData.ToString());
				return -1;
			}

			Debug.Log("Received login request for player with id", loginData.PlayerId);
#if !DEBUG
			if (loginData.ClientHash != Server.CombinedHash)
			{
				Debug.LogInfo("Client/server hash mismatch.", loginData.ClientHash, Server.CombinedHash);
				var logInResponse = new LogInResponse
				{
					SyncResponse = true,
					ConversationGuid = loginData.ConversationGuid,
					Status = NetworkData.MessageStatus.Failure
				};
				await stream.WriteAsync(await ProtoSerialiser.Pack(logInResponse)).ConfigureAwait(false);
				return -1;
			}

			// Also has the added benefit of blocking players from joining non-nakama servers.
			if (loginData.ServerID != ServerId)
			{
				Debug.LogInfo("LogInRequest server ID doesn't match this server ID.", loginData.ServerID, ServerId);
				var logInResponse = new LogInResponse
				{
					SyncResponse = true,
					ConversationGuid = loginData.ConversationGuid,
					Status = NetworkData.MessageStatus.Failure
				};
				await stream.WriteAsync(await ProtoSerialiser.Pack(logInResponse)).ConfigureAwait(false);
				return -1;
			}
#endif

			// Check if player id is valid.
			// TODO: Verify playerid with Nakama.
			if (!Guid.TryParse(loginData.PlayerId, out _))
			{
				Debug.LogInfo("Player id isn't valid.", loginData.ServerID, ServerId);
				var logInResponse = new LogInResponse
				{
					SyncResponse = true,
					ConversationGuid = loginData.ConversationGuid,
					Status = NetworkData.MessageStatus.Failure
				};
				await stream.WriteAsync(await ProtoSerialiser.Pack(logInResponse)).ConfigureAwait(false);
				return -1;
			}

			long guid = GUIDFactory.PlayerIdToGuid(loginData.PlayerId);
			if (otherConnections.Contains(guid))
			{
				Debug.LogInfoFormat("Client with guid {0} is already connected.", guid);
				var logInResponse = new LogInResponse
				{
					SyncResponse = true,
					ConversationGuid = loginData.ConversationGuid,
					Status = NetworkData.MessageStatus.Failure
				};
				await stream.WriteAsync(await ProtoSerialiser.Pack(logInResponse)).ConfigureAwait(false);
				return -1;
			}

			var player = await Server.Instance.GetOrCreateConnectedPlayerAsync(guid, loginData.PlayerId, loginData.CharacterData);

			if (!player.PlayerReady || !player.EnvironmentReady)
			{
				player.Initialize = true;
			}

			Debug.Log("Connecting player", player.Name, player.Guid);
			player.ConnectToNetworkController();

			var loginResponse = new LogInResponse
			{
				Status = NetworkData.MessageStatus.Success,
				SyncResponse = true,
				ConversationGuid = loginData.ConversationGuid,
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
				loginResponse.SpawnPointsList = Server.Instance.GetAvailableSpawnPoints(player);
			}

			var packedData = await ProtoSerialiser.Pack(loginResponse);
			await stream.WriteAsync(packedData).ConfigureAwait(false);
			Debug.Log("Sent login response.");

			return guid;
		}
		catch (SocketException)
		{
			Debug.Log("Client disconnected when processing login.");
			return -1;
		}
	}

	public static void Start()
	{
		_statusPortConnectionListener = new StatusConnectionListener();
		_statusPortConnectionListener.Start(Server.StatusPort);
		_transport.Start(Server.GamePort);
	}

	private static async void LogOutRequestListener(NetworkData data)
	{
		await _transport.PrioritySendInternal(data.Sender, new LogOutResponse
		{
			Sender = 0L,
			Status = NetworkData.MessageStatus.Success,
		});
		DisconnectClient(data.Sender);
	}


	/// <summary>
	/// 	Get a list of all the players on the server.
	/// </summary>
	public static Player[] GetAllConnectedPlayers()
	{
		return (from guid in _transport.GetConnectionsGUIDAsync() select Server.Instance.GetPlayer(guid)).ToArray();
	}

	public static int GetMaxPlayers()
	{
		return MaxPlayers;
	}

	/// <summary>
	/// 	Send data to a client with specified guid.
	/// </summary>
	/// <param name="guid">Guid of client.</param>
	/// <param name="data">Data to send.</param>
	public static async Task Send(long guid, NetworkData data)
	{
		await _transport.SendInternal(guid, data);
	}

	/// <summary>
	/// 	Use request/response-like communication with async support.
	/// </summary>
	/// <param name="guid">Guid of client to send to.</param>
	/// <param name="data">The data to send.</param>
	public static Task<NetworkData> SendReceiveAsync(long guid, NetworkData data)
	{
		return _transport.SendReceiveAsyncInternal(guid, data);
	}

	/// <summary>
	/// 	Send a message to all clients.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public static async Task SendToAll(NetworkData data, long skipPlayerGuid = -1L)
	{
		await _transport.SendToAllInternal(data, skipPlayerGuid);
	}

	/// <summary>
	/// 	Send a message to all clients subscribed to a space object.<br />
	/// 	You can choose to skip one player.
	/// </summary>
	public static async Task SendToClientsSubscribedTo(NetworkData data, long skipPlayerGuid = -1L, params SpaceObject[] spaceObjects)
	{
		if (spaceObjects.Length == 0)
		{
			return;
		}
		await Parallel.ForEachAsync(from m in GetAllConnectedPlayers() where m != null && m.Guid != skipPlayerGuid select m, async (player, ct) =>
		{
			if (player.IsAlive && player.EnvironmentReady)
			{
				if (spaceObjects.Any((SpaceObject m) => m != null && player.IsSubscribedTo(m, checkParent: false)))
				{
					await Send(player.Guid, data);
				}
			}
			else if (!player.EnvironmentReady && data is ShipStatsMessage message && player.IsSubscribedTo(message.GUID))
			{
				player.MessagesReceivedWhileLoading.Enqueue(message);
			}
		});
	}

	public static async Task SendToClientsSubscribedToParents(NetworkData data, SpaceObject spaceObject, long skipPlayerGuid = -1L, int depth = 4)
	{
		List<SpaceObject> parents = new List<SpaceObject>
		{
			spaceObject
		};

		SpaceObject parent = spaceObject.Parent;
		while (parent != null && depth > 0)
		{
			parents.Add(parent);
			parent = parent.Parent;
			depth--;
		}
		await SendToClientsSubscribedTo(data, skipPlayerGuid, parents.ToArray());
	}

	/// <summary>
	/// 	Simple check to see if a client is connected.
	/// </summary>
	public static bool IsPlayerConnected(long guid)
	{
		return _transport.IsClientConnected(guid);
	}

	/// <summary>
	/// 	Disconnect currently connected client.<br />
	/// 	Terminates their connection and logs them out of the game.
	/// </summary>
	public static void DisconnectClient(long guid)
	{
		_transport.DisconnectInternal(guid);
	}

	/// <summary>
	/// 	Disconnect all currently connected clients.<br />
	/// 	Terminates their connection and logs them out of the game.
	/// </summary>
	public static void DisconnectAllClients()
	{
		_transport.DisconnectAll();
	}

	private static async void OnDisconnected(long guid)
	{
		var player = Server.Instance.GetPlayer(guid);
		await player?.RemovePlayerFromTrigger();
		player?.LogoutDisconnectReset();
		player?.DisconnectFromNetworkController();

		Debug.LogInfo("Player disconnected:", player?.Name, guid);
	}

	public static void Stop()
	{
		DisconnectAllClients();
		_statusPortConnectionListener.Stop();
		_transport.StopInternal();
	}
}
