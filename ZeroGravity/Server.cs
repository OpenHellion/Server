using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BulletSharp;
using OpenHellion.IO;
using OpenHellion.Net;
using OpenHellion.Net.Message.MainServer;
using ZeroGravity.BulletPhysics;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;

namespace ZeroGravity;

public class Server
{
	public class DynamicObjectsRespawn
	{
		public DynamicObjectSceneData Data;

		public SpaceObject Parent;

		public AttachPointDetails ApDetails;

		public double Timer;

		public double RespawnTime;

		public float MaxHealth = -1f;

		public float MinHealth = -1f;
	}

	public class SpawnPointInviteData
	{
		public ShipSpawnPoint SpawnPoint;

		public double InviteTimer = 300.0;
	}

	public static class NameGenerator
	{
		private static DateTime _lastClearDate = DateTime.UtcNow;

		private static readonly Dictionary<GameScenes.SceneId, int> DailySpawnCount = new Dictionary<GameScenes.SceneId, int>();

		private static readonly List<char> MonthCodes = new List<char>
		{
			'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
			'K', 'L'
		};

		private static readonly Dictionary<GameScenes.SceneId, string> ShipNaming = new Dictionary<GameScenes.SceneId, string>
		{
			{
				GameScenes.SceneId.AltCorp_LifeSupportModule,
				"LSM-AC:HE3/"
			},
			{
				GameScenes.SceneId.ALtCorp_PowerSupply_Module,
				"PSM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_AirLock,
				"AM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Cargo_Module,
				"CBM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Command_Module,
				"CM-AC:HE3/"
			},
			{
				GameScenes.SceneId.AltCorp_DockableContainer,
				"IC-AC:HE2/"
			},
			{
				GameScenes.SceneId.AltCorp_CorridorIntersectionModule,
				"CTM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Corridor45TurnModule,
				"CLM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Corridor45TurnRightModule,
				"CRM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_CorridorVertical,
				"CSM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_CorridorModule,
				"CIM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_StartingModule,
				"OUTPOST "
			},
			{
				GameScenes.SceneId.AltCorp_Shuttle_SARA,
				"AC-ARG HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_CrewQuarters_Module,
				"CQM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_SolarPowerModule,
				"SPM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_FabricatorModule,
				"FM-AC:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Shuttle_CECA,
				"Steropes:HE1/"
			},
			{
				GameScenes.SceneId.Generic_Debris_Spawn1,
				"Small Debris:HE1/"
			},
			{
				GameScenes.SceneId.Generic_Debris_Spawn2,
				"Large Debris:HE1/"
			},
			{
				GameScenes.SceneId.Generic_Debris_Spawn3,
				"Medium Debris:HE1/"
			},
			{
				GameScenes.SceneId.Generic_Debris_Outpost001,
				"Doomed outpost:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_PatchModule,
				"Bulkhead:HE1/"
			},
			{
				GameScenes.SceneId.SOE_Location002,
				"SOE Derelict:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Secure_Module,
				"CM-SDS:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_Destroyed_Shuttle_CECA,
				"Steropes:HE1/"
			},
			{
				GameScenes.SceneId.AltCorp_CorridorIntersection_MKII,
				"CTM-SDS:HE1/"
			},
			{
				GameScenes.SceneId.AlrCorp_Corridor_MKII,
				"CIM-SDS:HE1/"
			}
		};

		private static readonly List<GameScenes.SceneId> Derelicts = new List<GameScenes.SceneId>
		{
			GameScenes.SceneId.Generic_Debris_Corridor001,
			GameScenes.SceneId.Generic_Debris_Corridor002,
			GameScenes.SceneId.Generic_Debris_JuncRoom001,
			GameScenes.SceneId.Generic_Debris_JuncRoom002
		};

		public static string GenerateObjectRegistration(SpaceObjectType type, CelestialBody parentCelestialBody, GameScenes.SceneId sceneId)
		{
			string name = "";
			string dailySpawnDigits = "000";
			if (type == SpaceObjectType.Ship && !Derelicts.Contains(sceneId))
			{
				if (ShipNaming.TryGetValue(sceneId, out var value))
				{
					name += value;
				}
				else
				{
					Debug.LogWarning("No name tag for ship", sceneId);
					name = name + type + MathHelper.RandomNextInt().ToString("X");
				}
			}
			else
			{
				string parentBody;
				if (parentCelestialBody != null)
				{
					string plnt = ((CelestialBodyGUID)parentCelestialBody.GUID).ToString();
					parentBody = plnt.Substring(0, System.Math.Min(3, plnt.Length)).ToUpper();
				}
				else
				{
					parentBody = "HEL";
				}
				switch (type)
				{
				case SpaceObjectType.Ship:
					name = name + "DERELICT " + parentBody + "-";
					dailySpawnDigits = "00";
					break;
				case SpaceObjectType.Asteroid:
					name = name + "ASTEROID " + parentBody + "-";
					break;
				}
			}
			if ((DateTime.UtcNow - _lastClearDate).TotalDays > 1.0)
			{
				_lastClearDate = DateTime.UtcNow;
				DailySpawnCount.Clear();
			}
			if (DailySpawnCount.ContainsKey(sceneId))
			{
				DailySpawnCount[sceneId]++;
			}
			else
			{
				DailySpawnCount.Add(sceneId, 1);
			}
			return name + DateTime.UtcNow.Day.ToString("00") + MonthCodes[DateTime.UtcNow.Month - 1] + DailySpawnCount[sceneId].ToString(dailySpawnDigits);
		}

		public static string GenerateStationRegistration()
		{
			return "STATION";
		}

		public static GameScenes.SceneId GetSceneId(string text)
		{
			foreach (GameScenes.SceneId scene in Enum.GetValues(typeof(GameScenes.SceneId)))
			{
				if (scene.ToString().StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
				{
					return scene;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneId, string> kv2 in ShipNaming)
			{
				if (kv2.Value.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
				{
					return kv2.Key;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneId, string> kv in ShipNaming)
			{
				if (kv.Value.Contains(text, StringComparison.CurrentCultureIgnoreCase))
				{
					return kv.Key;
				}
			}
			return GameScenes.SceneId.None;
		}
	}

	public const double RcsThrustMultiplier = 1.0;

	public const double RcsRotationMultiplier = 1.0;

	public const double CelestialBodyRadiusMultiplier = 1.0;

	public static Properties Properties;

	public static string ConfigDir = "";

	public static double PersistenceSaveInterval = 900.0;

	private static bool _cleanStart;

	public static bool Restart;

	public static bool CleanRestart = false;

	public const double CelestialBodyDeathDistance = 10000.0;

	private readonly ConcurrentDictionary<long, SpaceObject> _spaceObjects = new();

	private readonly ConcurrentDictionary<long, Player> _players = new();

	private ConcurrentBag<Player> _playersToAdd = new ConcurrentBag<Player>();

	private ConcurrentBag<Player> _playersToRemove = new ConcurrentBag<Player>();

	private readonly ConcurrentDictionary<long, SpaceObjectVessel> _vessels = new();

	private readonly ConcurrentDictionary<long, DynamicObject> _updateableDynamicObjects = new();

	private readonly List<UpdateTimer> _timersToRemove = new List<UpdateTimer>();

	private readonly List<UpdateTimer> _timers = new List<UpdateTimer>();

	public readonly SolarSystem SolarSystem;

	private static Server _serverInstance;

	public static bool IsRunning;

	private long _numberOfTicks = 64L;

	public static int GamePort = 6004;

	public static int StatusPort = 6005;

	public static bool SavePersistenceDataOnShutdown;

	public bool WorldInitialized;

	private readonly DateTime _serverStartTime = DateTime.UtcNow;

	private float _solarSystemStartTime = -1f;

	public static int MaxNumberOfSaveFiles = 10;

	private IpAddressRange[] _adminIpAddressRanges = [];

	private readonly Dictionary<string, SpawnPointInviteData> _spawnPointInvites = [];

	public static readonly AutoResetEvent MainLoopEnded = new AutoResetEvent(initialState: false);

	private bool _mainLoopStarted;

	public readonly List<DebrisField> DebrisFields = new List<DebrisField>();

	public static double VesselDecayRateMultiplier = 1.0;

	public static double VesselDecayGracePeriod;

	public static double VesselExplosionRadiusMultiplier = 1.0;

	public static double VesselExplosionDamageMultiplier = 1.0;

	public static double VesselCollisionDamageMultiplier = 1.0;

	public static double SelfDestructExplosionRadiusMultiplier = 1.0;

	public static double SelfDestructExplosionDamageMultiplier = 1.0;

	public static double DebrisVesselExplosionRadiusMultiplier = 1.0;

	public static double DebrisVesselExplosionDamageMultiplier = 1.0;

	public static double DebrisVesselCollisionDamageMultiplier = 1.0;

	public static double ActivateRepairPointChanceMultiplier = 1.0;

	public const double DamageUpgradePartChanceMultiplier = 1.0;

	public static float StartingSetDespawnTimeSec = 900f;

	private bool _updateDataInSeparateThread;

	public static int JunkItemsCleanupScope = 1;

	public static double JunkItemsTimeToLive = 3600.0;

	public static double JunkItemsCleanupInterval = 900.0;

	public static bool CanWarpThroughCelestialBodies;

	public static double MaxAngularVelocityPerAxis = 300.0;

	private static List<string> _serverAdmins = new List<string>();

	public static double ServerRestartTimeSec = -1.0;

	public static DateTime RestartTime;

	private double _timeToRestart = 1800.0;

	public readonly List<DeathMatchArenaController> DeathMatchArenaControllers = new List<DeathMatchArenaController>();

	public readonly DoomedShipController DoomedShipController = new DoomedShipController();

	private const double MovementMessageSendInterval = 1;

	private static double _movementMessageTimer;

	public static volatile int MainThreadId;

	public readonly BulletPhysicsController PhysicsController;

	private double _persistenceSaveTimer;

	private bool _printDebugObjects;

	private static readonly uint NetworkDataHash = ClassHasher.GetClassHashCode(typeof(NetworkData));

	private static readonly uint SceneDataHash = ClassHasher.GetClassHashCode(typeof(ISceneData));

	public static readonly uint CombinedHash = NetworkDataHash * SceneDataHash;

	private bool _manualSave;

	private string _manualSaveFileName;

	private SaveFileAuxData _manualSaveAuxData;

	private bool _updatingShipSystems;

	public readonly ConcurrentDictionary<long, VesselDataUpdate> VesselsDataUpdate = new ConcurrentDictionary<long, VesselDataUpdate>();

	public List<DynamicObjectsRespawn> DynamicObjectsRespawnList = new List<DynamicObjectsRespawn>();

	private double _tickMilliseconds;

	public double DeltaTime;

	private DateTime _lastTime;

	public const double SpawnPointInviteTimer = 300.0;

	private static string _loadPersistenceFromFile;

	public ImmutableList<SpaceObjectVessel> AllVessels => [.. _vessels.Values];

	public ImmutableList<Player> AllPlayers => [.. _players.Values];

	public static Server Instance => _serverInstance;

	public TimeSpan RunTime => DateTime.UtcNow - _serverStartTime;

	public static double SolarSystemTime => Instance.SolarSystem.CurrentTime;

	public bool DoesObjectExist(long guid)
	{
		return _spaceObjects.ContainsKey(guid);
	}

	public SpaceObject GetObject(long guid)
	{
		if (_spaceObjects.TryGetValue(guid, out var o))
		{
			return o;
		}
		return null;
	}

	/// <summary>
	/// 	Get a player with a guid.
	/// </summary>
	public Player GetPlayer(long guid)
	{
		_players.TryGetValue(guid, out Player player);
		return player;
	}

	/// <summary>
	/// 	Gets a player from a specified player id.
	/// </summary>
	public Player GetPlayerFromPlayerId(string playerId)
	{
		return GetPlayer(GUIDFactory.PlayerIdToGuid(playerId));
	}

	public SpaceObjectVessel GetVessel(long guid)
	{
		return _vessels.TryGetValue(guid, out var vessel) ? vessel : null;
	}

	public Item GetItem(long guid)
	{
		if (_spaceObjects.TryGetValue(guid, out SpaceObject value) && value is DynamicObject obj)
		{
			return obj.Item;
		}
		return null;
	}

	public void Add(Player player)
	{
		if (_mainLoopStarted)
		{
			_playersToAdd.Add(player);
			return;
		}
		_players[player.Guid] = player;
		_spaceObjects[player.FakeGuid] = player;
	}

	public void Add(SpaceObjectVessel vessel)
	{
		if (vessel is null)
		{
			Debug.LogError("Tried to add null vessel to vessels and objects list.");
			return;
		}

		_vessels[vessel.Guid] = vessel;
		_spaceObjects[vessel.Guid] = vessel;
	}

	public void Add(DynamicObject dynamicObject)
	{
		_spaceObjects[dynamicObject.Guid] = dynamicObject;
		if (dynamicObject.Item is IUpdateable)
		{
			_updateableDynamicObjects[dynamicObject.Guid] = dynamicObject;
		}
	}

	public void Add(Corpse corpse)
	{
		_spaceObjects[corpse.Guid] = corpse;
	}

	public void Remove(Player player)
	{
		if (_mainLoopStarted)
		{
			_playersToRemove.Add(player);
			return;
		}
		_players.TryRemove(player.Guid, out _);
		_spaceObjects.TryRemove(player.FakeGuid, out _);
	}

	public void Remove(SpaceObjectVessel vessel)
	{
		_vessels.TryRemove(vessel.Guid, out _);
		_spaceObjects.TryRemove(vessel.Guid, out _);
	}

	public void Remove(DynamicObject dobj)
	{
		_spaceObjects.TryRemove(dobj.Guid, out _);
		_updateableDynamicObjects.TryRemove(dobj.Guid, out _);
	}

	public void Remove(Corpse corpse)
	{
		_spaceObjects.TryRemove(corpse.Guid, out _);
	}

	public Server()
	{
		MainThreadId = Environment.CurrentManagedThreadId;
		IsRunning = true;
		_serverInstance = this;
		PhysicsController = new BulletPhysicsController();
		SolarSystem = new SolarSystem();
		LoadServerSettings();
		Console.Title = "(id: " + (NetworkController.ServerId == null ? "Not yet assigned" : string.Concat(NetworkController.ServerId)) + ")";
		Stopwatch stopWatch = new Stopwatch();
		stopWatch.Start();
		Thread.Sleep(1);
		stopWatch.Stop();
		long maxTicks = (long)(1000.0 / stopWatch.Elapsed.TotalMilliseconds);
		Debug.UnformattedMessage(string.Format("Game port: {4}\nStatus port: {5}\nStart date: {0}\nServer ticks: {1}{3}\nMax server ticks (not precise): {2}\nCombined hash: {6}", DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss.ffff"), _numberOfTicks, maxTicks, _numberOfTicks > maxTicks ? " WARNING: Server ticks is larger than max tick" : "", GamePort, StatusPort, CombinedHash));
		StaticData.LoadData();
	}

	/// <summary>
	/// 	Gets the arguments provided to the program and sets the properties accordingly. Only executed at the start of the program.
	/// </summary>
	public static void InitProperties(string[] args)
	{
		// Parse arguments.
		string gPort = null;
		string sPort = null;
		string randomShips = null;
		for (int i = 0; i < args.Length; i++)
		{
			switch(args[i].ToLower()) {
				case "-configdir":
					i++;

					if (i >= args.Length)
					{
						Debug.LogError("-configdir was not supplied a path.");
						break;
					}

					ConfigDir = args[i];
					if (!ConfigDir.EndsWith("/"))
					{
						ConfigDir += "/";
					}
				break;
				case "-clean":
				case "-noload":
					_cleanStart = true;
				break;
				case "-load":
					i++;

					if (i >= args.Length)
					{
						Debug.LogError("-load was not supplied a path.");
						break;
					}

					_loadPersistenceFromFile = args[i];
				break;
				case "-randomships":
					i++;

					if (i >= args.Length)
					{
						Debug.LogError("-randomships was not supplied a number.");
						break;
					}

					randomShips = args[++i];
				break;
				case "-gport":
					i++;

					if (i >= args.Length)
					{
						Debug.LogError("-gport was not supplied a port.");
						break;
					}

					gPort = args[++i];
				break;
				case "-sport":
					i++;

					if (i >= args.Length)
					{
						Debug.LogError("-sport was not supplied a port.");
						break;
					}

					sPort = args[++i];
				break;
			}
		}

		// Set properties.
		Properties = new Properties(ConfigDir + "GameServer.ini");
		if (gPort != null)
		{
			Properties.SetProperty("game_port", gPort);
		}
		if (sPort != null)
		{
			Properties.SetProperty("status_port", sPort);
		}
		if (randomShips != null)
		{
			Properties.SetProperty("spawn_random_ships_count", randomShips);
		}
	}

	private void LoadServerSettings()
	{
		Properties.GetProperty("server_tick_count", ref _numberOfTicks);
		Properties.GetProperty("game_port", ref GamePort);
		Properties.GetProperty("status_port", ref StatusPort);
		Properties.GetProperty("http_key", ref MainServerConnection.HttpKey);
		Properties.GetProperty("main_server_ip", ref MainServerConnection.IpAddress);
		Properties.GetProperty("main_server_port", ref MainServerConnection.Port);
		string admins = "";
		Properties.GetProperty("server_admins", ref admins);
		string[] adminsArray = admins.Split(',');
		_serverAdmins = adminsArray.Where((string m) => m != "").ToList();
		Properties.GetProperty("solar_system_time", ref _solarSystemStartTime);
		Properties.GetProperty("save_interval", ref PersistenceSaveInterval);
		Properties.GetProperty("max_players", ref NetworkController.MaxPlayers);
		Properties.GetProperty("number_of_save_files", ref MaxNumberOfSaveFiles);
		MaxNumberOfSaveFiles = MathHelper.Clamp(MaxNumberOfSaveFiles, 1, 100);
		Properties.GetProperty("vessel_decay_rate_multiplier", ref VesselDecayRateMultiplier);
		Properties.GetProperty("vessel_decay_grace_period", ref VesselDecayGracePeriod);
		Properties.GetProperty("vessel_explosion_radius_multiplier", ref VesselExplosionRadiusMultiplier);
		Properties.GetProperty("vessel_explosion_damage_multiplier", ref VesselExplosionDamageMultiplier);
		Properties.GetProperty("self_destruct_explosion_radius_multiplier", ref SelfDestructExplosionRadiusMultiplier);
		Properties.GetProperty("self_destruct_explosion_damage_multiplier", ref SelfDestructExplosionDamageMultiplier);
		Properties.GetProperty("vessel_collision_damage_multiplier", ref VesselCollisionDamageMultiplier);
		Properties.GetProperty("debris_vessel_explosion_radius_multiplier", ref DebrisVesselExplosionRadiusMultiplier);
		Properties.GetProperty("debris_vessel_explosion_damage_multiplier", ref DebrisVesselExplosionDamageMultiplier);
		Properties.GetProperty("debris_vessel_collision_damage_multiplier", ref DebrisVesselCollisionDamageMultiplier);
		Properties.GetProperty("activate_repair_point_chance_multiplier", ref ActivateRepairPointChanceMultiplier);
		Properties.GetProperty("starting_set_despawn_time", ref StartingSetDespawnTimeSec);
		Properties.GetProperty("server_restart_time", ref ServerRestartTimeSec);
		Properties.GetProperty("update_data_in_separate_thread", ref _updateDataInSeparateThread);
		Properties.GetProperty("junk_items_cleanup_scope", ref JunkItemsCleanupScope);
		Properties.GetProperty("junk_items_time_to_live", ref JunkItemsTimeToLive);
		Properties.GetProperty("junk_items_cleanup_interval", ref JunkItemsCleanupInterval);
		Properties.GetProperty("can_warp_through_celestial_bodies", ref CanWarpThroughCelestialBodies);
		Properties.GetProperty("max_angular_velocity_per_axis", ref MaxAngularVelocityPerAxis);
		Properties.GetProperty("arena_ship_respawn_timer", ref SpaceObjectVessel.ArenaRescueTime);
		Properties.GetProperty("print_debug_objects", ref _printDebugObjects);
		Properties.GetProperty("spawn_manager_print_categories", ref SpawnManager.Settings.PrintCategories);
		Properties.GetProperty("spawn_manager_print_spawn_rules", ref SpawnManager.Settings.PrintSpawnRules);
		Properties.GetProperty("spawn_manager_print_item_attach_points", ref SpawnManager.Settings.PrintItemAttachPoints);
		Properties.GetProperty("spawn_manager_print_item_type_ids", ref SpawnManager.Settings.PrintItemTypeIDs);
	}

	public async Task<Player> GetOrCreateConnectedPlayerAsync(long guid, string playerId, CharacterData characterData)
	{
		if (!WorldInitialized)
		{
			await SpawnManager.Initialize();;
		}

		Player player = GetPlayer(guid);
		if (player is null)
		{
			Debug.Log("Creating new player for client with guid:", guid);

			player = await Player.CreatePlayerAsync(guid, Vector3D.Zero, QuaternionD.Identity, characterData.Name, playerId, characterData.Gender, characterData.HeadType, characterData.HairType);
			Add(player);
		}

		if (_serverAdmins.Contains(player.PlayerId) || _serverAdmins.Contains("*"))
		{
			player.IsAdmin = true;
		}

		return player;
	}

	private async Task ResetSpawnPointsForPlayerAsync(Player pl, Ship skipShip)
	{
		if (!pl.PlayerId.IsNullOrEmpty() && _spawnPointInvites.ContainsKey(pl.PlayerId))
		{
			await ClearSpawnPointInvitation(pl.PlayerId);
		}
		foreach (SpaceObjectVessel ves in _vessels.Values)
		{
			if (ves is Ship ship && ship != skipShip)
			{
				await ship.ResetSpawnPointsForPlayer(pl, sendStatsMessage: true);
			}
		}
	}

	private async Task<bool> AddPlayerToShipAsync(Player pl, SpawnSetupType setupType, long shipId)
	{
		if (shipId == 0L && setupType == SpawnSetupType.None)
		{
			return false;
		}
		Ship foundShip = null;
		ShipSpawnPoint foundSpawnPoint = null;
		switch (setupType)
		{
			case SpawnSetupType.Continue:
				if (pl.AuthorizedSpawnPoint == null)
				{
					return false;
				}
				foundShip = pl.AuthorizedSpawnPoint.Ship;
				foundSpawnPoint = pl.AuthorizedSpawnPoint;
				break;
			case SpawnSetupType.None:
				if (shipId > 0 && _spawnPointInvites.TryGetValue(pl.PlayerId, out SpawnPointInviteData inviteData) && inviteData.SpawnPoint.Ship.Guid == shipId)
				{
					foundShip = GetVessel(shipId) as Ship;
					foundSpawnPoint = foundShip != null ? inviteData.SpawnPoint : null;
					if (foundSpawnPoint != null)
					{
						await ResetSpawnPointsForPlayerAsync(pl, null);
					}
				}
				break;
			default:
				await ResetSpawnPointsForPlayerAsync(pl, null);
				foundShip = await SpawnManager.SpawnStartingSetup(setupType.ToString());
				if (foundShip != null)
				{
					foundSpawnPoint = SpawnManager.SetStartingSetupSpawnPoints(foundShip, pl);
				}
				if (foundShip == null || foundSpawnPoint == null)
				{
					Debug.LogError("FAILED TO SPAWN STARTING SETUP", pl.Guid, foundShip?.Guid ?? -1);
				}
				break;
		}

		if (foundShip != null && foundSpawnPoint != null)
		{
			foundSpawnPoint.Player = pl;
			if (foundSpawnPoint.Executor != null)
			{
				foundSpawnPoint.IsPlayerInSpawnPoint = true;
			}
			await foundSpawnPoint.AuthorizePlayerToSpawnPoint(pl, sendMessage: true);
			pl.Parent = foundSpawnPoint.Ship;
			pl.SetSpawnPoint(foundSpawnPoint);
			pl.SubscribeTo(foundSpawnPoint.Ship.MainVessel);
			return true;
		}
		return false;
	}

	public async Task RemovePlayerAsync(Player player)
	{
		if (player.Parent != null)
		{
			if (player.Parent.ObjectType == SpaceObjectType.PlayerPivot)
			{
				await (player.Parent as Pivot).Destroy();
			}
			if (player.Parent.ObjectType is SpaceObjectType.Ship or SpaceObjectType.Asteroid)
			{
				(player.Parent as SpaceObjectVessel).RemovePlayerFromCrew(player, checkDetails: true);
			}
			else if (player.Parent.ObjectType != SpaceObjectType.Station)
			{
			}
		}
		Remove(player);
	}

	public List<SpawnPointDetails> GetAvailableSpawnPoints(Player pl)
	{
		List<SpawnPointDetails> retVal = new();
		if (pl.PlayerId != null && _spawnPointInvites.TryGetValue(pl.PlayerId, out var invite))
		{
			ShipSpawnPoint sp = invite.SpawnPoint;
			SpawnPointDetails spd = new SpawnPointDetails
			{
				Name = sp.Ship.FullName,
				IsPartOfCrew = false,
				SpawnPointParentID = sp.Ship.Guid,
				PlayersOnShip = new List<string>()
			};
			foreach (Player item in sp.Ship.VesselCrew)
			{
				spd.PlayersOnShip.Add(item.Name);
			}
			retVal.Add(spd);
		}
		return retVal;
	}

	private async void LatencyTestListener(NetworkData data)
	{
		await NetworkController.SendAsync(data.Sender, data);
	}

	private async Task Start()
	{
		EventSystem.AddSyncRequestListener<PlayerSpawnRequest>(PlayerSpawnRequestListener);
		EventSystem.AddSyncRequestListener<AvailableSpawnPointsRequest>(AvailableSpawnPointsRequestListener);
		EventSystem.AddListener<PlayerRespawnRequest>(PlayerRespawnRequestListener);
		EventSystem.AddListener<SpawnObjectsRequest>(SpawnObjectsRequestListener);
		EventSystem.AddListener<SubscribeToObjectsRequest>(SubscribeToSpaceObjectListener);
		EventSystem.AddListener<UnsubscribeFromObjectsRequest>(UnsubscribeFromSpaceObjectListener);
		EventSystem.AddListener<TextChatMessage>(TextChatMessageListener);
		EventSystem.AddListener<TransferResourceMessage>(TransferResourcesMessageListener);
		EventSystem.AddListener<FabricateItemMessage>(FabricateItemMessageListener);
		EventSystem.AddListener<CancelFabricationMessage>(CancelFabricationMessageListener);
		EventSystem.AddListener<PlayersOnServerRequest>(PlayersOnServerRequestListener);
		EventSystem.AddListener<RepairItemMessage>(RepairMessageListener);
		EventSystem.AddListener<RepairVesselMessage>(RepairMessageListener);
		EventSystem.AddListener<HurtPlayerMessage>(HurtPlayerMessageListener);
		EventSystem.AddListener<ConsoleMessage>(ConsoleMessageListener);
		EventSystem.AddListener<ExplosionMessage>(ExplosionMessageListener);
		EventSystem.AddListener<LatencyTestMessage>(LatencyTestListener);
		EventSystem.AddListener<ServerShutDownMessage>(ServerShutDownMessageListener);
		EventSystem.AddListener<NameTagMessage>(NameTagMessageListener);

		try
		{
			RegisterServerResponse response = await MainServerConnection.Send<RegisterServerResponse>(new RegisterServerRequest
			{
				AuthToken = Properties.GetProperty<string>("auth_key"),
				Location = RegionInfo.CurrentRegion.EnglishName,
				GamePort = GamePort,
				StatusPort = StatusPort,
				Hash = CombinedHash
			});

			NetworkController.ServerId = response.ServerId;
			_adminIpAddressRanges = response.AdminIpAddressRanges;
		}
		catch
		{
			Debug.LogError("Could not connect to main server. Check if ip, port and auth key is correct.");
			IsRunning = false;
			return;
		}

		Console.Title = " (id: " + (NetworkController.ServerId == null ? "Not yet assigned" : string.Concat(NetworkController.ServerId)) + ")";
		Debug.UnformattedMessage("r\n\tServer ID: " + NetworkController.ServerId + "\r\n");
	}

	public void TransferResourcesMessageListener(NetworkData data)
	{
		var message = data as TransferResourceMessage;
		SpaceObjectVessel fromVessel = Instance.GetVessel(message.FromVesselGuid);
		SpaceObjectVessel toVessel = Instance.GetVessel(message.ToVesselGuid);
		if (message.FromLocationType == ResourceLocationType.ResourcesTransferPoint)
		{
			ICargo toCargo2 = null;
			if (message.ToLocationType == ResourceLocationType.CargoBay)
			{
				toCargo2 = toVessel.CargoBay;
			}
			else if (message.ToLocationType == ResourceLocationType.ResourceTank)
			{
				toCargo2 = toVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(message.ToVesselGuid, message.ToInSceneID));
			}
			DynamicObject dobj2 = fromVessel.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item is
			{
				AttachPointID: not null
			} && m.Item.AttachPointID.InSceneID == message.FromInSceneID);
			if (dobj2 is { Item: ICargo item } && toCargo2 != null)
			{
				if (message.ToLocationType != 0)
				{
					TransferResources(item, message.FromCompartmentID, toCargo2, message.ToCompartmentID, message.ResourceType, message.Quantity);
				}
				else
				{
					VentResources(item, message.FromCompartmentID, message.ResourceType, message.Quantity);
				}
			}
			return;
		}
		if (message.ToLocationType == ResourceLocationType.ResourcesTransferPoint)
		{
			ICargo fromCargo2 = null;
			if (message.FromLocationType == ResourceLocationType.CargoBay)
			{
				fromCargo2 = fromVessel.CargoBay;
			}
			else if (message.FromLocationType == ResourceLocationType.ResourceTank)
			{
				fromCargo2 = fromVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(message.FromVesselGuid, message.FromInSceneID));
			}
			DynamicObject dobj = toVessel.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item is
			{
				AttachPointID: not null
			} && m.Item.AttachPointID.InSceneID == message.ToInSceneID);
			if (dobj is { Item: ICargo item } && fromCargo2 != null)
			{
				if (message.ToLocationType != 0)
				{
					TransferResources(fromCargo2, message.FromCompartmentID, item, message.ToCompartmentID, message.ResourceType, message.Quantity);
				}
				else
				{
					VentResources(item, message.FromCompartmentID, message.ResourceType, message.Quantity);
				}
			}
			return;
		}
		ICargo fromCargo = null;
		if (message.FromLocationType == ResourceLocationType.CargoBay)
		{
			fromCargo = fromVessel.CargoBay;
		}
		else if (message.FromLocationType == ResourceLocationType.Refinery)
		{
			fromCargo = fromVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(message.FromVesselGuid, message.FromInSceneID)) as SubSystemRefinery;
		}
		else if (message.FromLocationType == ResourceLocationType.Fabricator)
		{
			fromCargo = fromVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(message.FromVesselGuid, message.FromInSceneID)) as SubSystemFabricator;
		}
		else if (message.FromLocationType == ResourceLocationType.ResourceTank)
		{
			fromCargo = fromVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(message.FromVesselGuid, message.FromInSceneID));
		}
		ICargo toCargo = null;
		if (message.ToLocationType == ResourceLocationType.CargoBay)
		{
			toCargo = toVessel.CargoBay;
		}
		else if (message.ToLocationType == ResourceLocationType.Refinery)
		{
			toCargo = toVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(message.ToVesselGuid, message.ToInSceneID)) as SubSystemRefinery;
		}
		else if (message.ToLocationType == ResourceLocationType.Fabricator)
		{
			toCargo = toVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(message.ToVesselGuid, message.ToInSceneID)) as SubSystemFabricator;
		}
		else if (message.ToLocationType == ResourceLocationType.ResourceTank)
		{
			toCargo = toVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(message.ToVesselGuid, message.ToInSceneID));
		}
		else if (message.ToLocationType == ResourceLocationType.None)
		{
			VentResources(fromCargo, message.FromCompartmentID, message.ResourceType, message.Quantity);
			return;
		}
		if (fromCargo != null && toCargo != null)
		{
			TransferResources(fromCargo, message.FromCompartmentID, toCargo, message.ToCompartmentID, message.ResourceType, message.Quantity);
		}
	}

	public void TransferResources(ICargo fromCargo, short fromCompartmentId, ICargo toCargo, short toCompartmentId, ResourceType resourceType, float quantity)
	{
		CargoCompartmentData fromCompartment = fromCargo.Compartments.Find((CargoCompartmentData m) => m.ID == fromCompartmentId);
		CargoCompartmentData toCompartment = toCargo.Compartments.Find((CargoCompartmentData m) => m.ID == toCompartmentId);
		if (fromCompartment == null || toCompartment == null || (toCompartment.AllowedResources.Count > 0 && !toCompartment.AllowedResources.Contains(resourceType)) || (toCompartment.AllowOnlyOneType && toCompartment.Resources.Count > 0 && toCompartment.Resources[0].ResourceType != resourceType))
		{
			return;
		}
		float qty = quantity;
		CargoResourceData res = fromCompartment.Resources.Find((CargoResourceData m) => m != null && m.ResourceType == resourceType);
		if (res == null)
		{
			return;
		}
		float available = res.Quantity;
		if (!(available <= float.Epsilon))
		{
			if (qty > available)
			{
				qty = available;
			}
			float free = toCompartment.Capacity - toCompartment.Resources.Sum((CargoResourceData m) => m.Quantity);
			if (qty > free)
			{
				qty = free;
			}
			fromCargo.ChangeQuantityByAsync(fromCompartmentId, resourceType, 0f - qty);
			toCargo.ChangeQuantityByAsync(toCompartmentId, resourceType, qty);
		}
	}

	private void VentResources(ICargo fromCargo, short fromCompartmentId, ResourceType resourceType, float quantity)
	{
		CargoCompartmentData fromCompartment = fromCargo.Compartments.Find((CargoCompartmentData m) => m.ID == fromCompartmentId);
		if (fromCompartment == null)
		{
			return;
		}
		float qty = quantity;
		CargoResourceData res = fromCompartment.Resources.Find((CargoResourceData m) => m != null && m.ResourceType == resourceType);
		if (res != null)
		{
			float available = res.Quantity;
			if (qty > available)
			{
				qty = available;
			}
			fromCargo.ChangeQuantityByAsync(fromCompartmentId, resourceType, 0f - qty);
		}
	}

	private void FabricateItemMessageListener(NetworkData data)
	{
		var message = data as FabricateItemMessage;
		SpaceObjectVessel vessel = GetVessel(message.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(message.ID) is SubSystemFabricator fabricator)
		{
			fabricator.Fabricate(message.ItemType);
		}
	}

	private async void CancelFabricationMessageListener(NetworkData data)
	{
		var message = data as CancelFabricationMessage;
		SpaceObjectVessel vessel = GetVessel(message.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(message.ID) is SubSystemFabricator fabricator)
		{
			await fabricator.Cancel(message.CurrentItemOnly);
		}
	}

	private async void RepairMessageListener(NetworkData data)
	{
		Player pl = GetPlayer(data.Sender);
		Item rTool = pl?.PlayerInventory.HandsSlot.Item;
		if (rTool is not RepairTool rt)
		{
			return;
		}
		if (data is RepairVesselMessage rvm)
		{
			await rt.RepairVessel(rvm.ID);
		}
		else if (data is RepairItemMessage rim)
		{
			if (rim.GUID > 0)
			{
				await rt.RepairItem(rim.GUID);
			}
			else
			{
				await rt.ConsumeFuel(rt.RepairAmount * rt.FuelConsumption);
			}
		}
	}

	private async void HurtPlayerMessageListener(NetworkData data)
	{
		var message = data as HurtPlayerMessage;
		Player pl = GetPlayer(message.Sender);
		await pl.Stats.TakeDamage(message.Duration, message.Damage);
	}

	private async void ConsoleMessageListener(NetworkData data)
	{
		var message = data as ConsoleMessage;
		Player player = GetPlayer(message.Sender);
		if (player.IsAdmin)
		{
			await ProcessConsoleCommand(message.Text, player);
		}
	}

	private async void TextChatMessageListener(NetworkData data)
	{
		var message = data as TextChatMessage;
		Player player = _players[message.Sender];
		message.GUID = player.FakeGuid;
		message.Name = player.Name;
		if (message.MessageText.Length > 250)
		{
			message.MessageText = message.MessageText.Substring(0, 250);
		}
		if (message.Local)
		{
			Vector3D playerGlobalPos = player.Parent.Position + player.Position;
			{
				foreach (Player pl in _players.Values)
				{
					if ((pl.Parent.Position + pl.Position - playerGlobalPos).SqrMagnitude < 1000000.0 && pl != player)
					{
						await NetworkController.SendAsync(pl.Guid, message);
					}
				}
				return;
			}
		}
		await NetworkController.SendToAllAsync(message, message.Sender);
	}

	private async Task ProcessConsoleCommand(string cmd, Player player)
	{
		try
		{
			if (!player.IsAdmin)
			{
				return;
			}
			string[] parts = cmd.Split(' ');
			for (int i = 0; i < parts.Length && i != 2; i++)
			{
				parts[i] = parts[i].ToLower();
			}
			SpaceObject parent = player.Parent;
			if (parts[0] == "refill" && parts.Length == 1)
			{
				Item handsItem = player.PlayerInventory.HandsSlot.Item;
				if (handsItem is ICargo cargo)
				{
					foreach (CargoCompartmentData ccd in cargo.Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
					{
						using List<CargoResourceData>.Enumerator enumerator2 = ccd.Resources.GetEnumerator();
						if (enumerator2.MoveNext())
						{
							CargoResourceData r = enumerator2.Current;
							await cargo.ChangeQuantityByAsync(ccd.ID, r.ResourceType, ccd.Capacity);
						}
					}
				}
				if (player.PlayerInventory.CurrOutfit != null)
				{
					foreach (InventorySlot os in player.PlayerInventory.CurrOutfit.InventorySlots.Values)
					{
						Item item2 = os.Item;
						if (item2 is not ICargo cargo1)
						{
							continue;
						}
						foreach (CargoCompartmentData ccd2 in cargo1.Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
						{
							using List<CargoResourceData>.Enumerator enumerator5 = ccd2.Resources.GetEnumerator();
							if (enumerator5.MoveNext())
							{
								await cargo1.ChangeQuantityByAsync(ccd2.ID, enumerator5.Current.ResourceType, ccd2.Capacity);
							}
						}
					}
				}
				if (parent is not SpaceObjectVessel vessel)
				{
					return;
				}
				foreach (ResourceContainer rc in vessel.MainDistributionManager.GetResourceContainers())
				{
					foreach (CargoCompartmentData ccd3 in rc.Compartments)
					{
						foreach (CargoResourceData crd in ccd3.Resources)
						{
							await rc.ChangeQuantityByAsync(ccd3.ID, crd.ResourceType, ccd3.Capacity);
						}
					}
				}
				{
					foreach (GeneratorCapacitor cap in (from m in vessel.MainDistributionManager.GetGenerators()
						where m is GeneratorCapacitor
						select m).Cast<GeneratorCapacitor>())
					{
						cap.Capacity = cap.MaxCapacity;
					}
					return;
				}
			}
			if (parts[0] == "spawn" && parts.Length is 2 or 3)
			{
				Vector3D spawnItemPosition = player.LocalPosition + player.LocalRotation * Vector3D.Forward;
				if (parts[1].ToLower() == "corpse")
				{
					Corpse corpse = new Corpse(player)
					{
						LocalPosition = player.LocalPosition + player.LocalRotation * Vector3D.Forward
					};
					await NetworkController.SendToClientsSubscribedTo(new SpawnObjectsResponse
					{
						Data =
						[
							new SpawnCorpseResponseData
							{
								GUID = corpse.Guid,
								Details = corpse.GetDetails()
							}
						]
					}, -1L, player.Parent);
					return;
				}
				int tier = 1;
				if (parts.Length == 3 && !int.TryParse(parts[2], out tier))
				{
					tier = 1;
				}
				List<InventorySlot> inventorySlots = [player.PlayerInventory.OutfitSlot];
				if (player.PlayerInventory.CurrOutfit != null)
				{
					inventorySlots.AddRange(player.PlayerInventory.CurrOutfit.InventorySlots.Values.Where((InventorySlot m) => m.SlotType == InventorySlot.Type.Equip));
				}
				inventorySlots.Add(player.PlayerInventory.HandsSlot);
				if (player.PlayerInventory.CurrOutfit != null)
				{
					inventorySlots.AddRange(player.PlayerInventory.CurrOutfit.InventorySlots.Values.Where((InventorySlot m) => m.SlotType == InventorySlot.Type.General));
				}
				foreach (DynamicObjectData dod in StaticData.DynamicObjectsDataList.Values)
				{
					if (!dod.PrefabPath.ToLower().EndsWith(parts[1].ToLower()))
					{
						continue;
					}
					if (dod.ItemType == ItemType.GenericItem)
					{
						InventorySlot slot = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.GenericItem));
						await DynamicObject.SpawnDynamicObject(dod.ItemType, (dod.DefaultAuxData as GenericItemData).SubType, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot, refill: true);
					}
					else if (dod.ItemType == ItemType.MachineryPart)
					{
						InventorySlot slot2 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						await DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, (dod.DefaultAuxData as MachineryPartData).PartType, parent, -1, spawnItemPosition, null, null, tier, slot2, refill: true);
					}
					else
					{
						InventorySlot slot3 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(dod.ItemType));
						await DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot3, refill: true);
					}
					return;
				}
				foreach (ItemType v in Enum.GetValues(typeof(ItemType)))
				{
					if (v.ToString().Contains(parts[1], StringComparison.CurrentCultureIgnoreCase))
					{
						InventorySlot slot4 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(v));
						await DynamicObject.SpawnDynamicObject(v, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot4, refill: true);
						return;
					}
				}
				foreach (GenericItemSubType v4 in Enum.GetValues(typeof(GenericItemSubType)))
				{
					if (v4.ToString().Contains(parts[1], StringComparison.CurrentCultureIgnoreCase))
					{
						InventorySlot slot5 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.GenericItem));
						await DynamicObject.SpawnDynamicObject(ItemType.GenericItem, v4, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot5, refill: true);
						return;
					}
				}
				foreach (MachineryPartType v5 in Enum.GetValues(typeof(MachineryPartType)))
				{
					if (v5.ToString().Contains(parts[1], StringComparison.CurrentCultureIgnoreCase))
					{
						InventorySlot slot6 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						await DynamicObject.SpawnDynamicObject(ItemType.MachineryPart, GenericItemSubType.None, v5, parent, -1, spawnItemPosition, null, null, tier, slot6, refill: true);
						return;
					}
				}
				{
					foreach (GameScenes.SceneId v6 in Enum.GetValues(typeof(GameScenes.SceneId)))
					{
						if (v6.ToString().Contains(parts[1], StringComparison.CurrentCultureIgnoreCase))
						{
							string tag = parts.Length == 3 ? parts[2] : "";
							if (GameScenes.Ranges.IsShip(v6))
							{
								tag = tag + (tag == "" || tag.EndsWith(";") ? "" : ";") + "_RescueVessel";
							}
							Vector3D offset;
							if (parent is SpaceObjectVessel vessel6)
							{
								offset = QuaternionD.LookRotation(vessel6.Forward, vessel6.Up) * (player.LocalPosition + player.LocalRotation * QuaternionD.Euler(0f - player.MouseLook, 0.0, 0.0) * Vector3D.Forward * 25.0);
							}
							else
							{
								offset = player.LocalPosition + player.LocalRotation * Vector3D.Forward * 50.0;
							}
							if (GameScenes.Ranges.IsAsteroid(v6))
							{
								Asteroid asteroid = Asteroid.CreateNewAsteroid(v6, "", -1L, [parent is SpaceObjectVessel vessel ? vessel.MainVessel.Guid : parent.Guid], null, offset * 10.0, null, null, tag, checkPosition: false);
								asteroid.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()).Normalized * 6.0;
							}
							else
							{
								Ship ship2 = await Ship.CreateNewShip(v6, "", -1L, new List<long> { parent is SpaceObjectVessel vessel ? vessel.MainVessel.Guid : parent.Guid }, null, offset, null, null, tag, checkPosition: false);
								ship2.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()).Normalized * 1.0;
							}
							break;
						}
					}
					return;
				}
			}
			if (parts[0] == "selfdestruct" && parts.Length == 2)
			{
				if (parent is SpaceObjectVessel vessel && int.TryParse(parts[1], out var time2))
				{
					vessel.SelfDestructTimer = new SelfDestructTimer(vessel, time2);
				}
				return;
			}
			if (parts[0] == "hitme" && parts.Length == 1)
			{
				double distance = 500.0;
				double velocity = 50.0;
				double radius2 = 10.0;
				Vector3D offset3 = parent is not SpaceObjectVessel vessel ? player.LocalRotation * Vector3D.Forward * distance : QuaternionD.LookRotation(vessel.Forward, vessel.Up) * player.LocalRotation * Vector3D.Forward * distance;
				Ship ship3 = await Ship.CreateNewShip(GameScenes.SceneId.AltCorp_CorridorModule, "", -1L, [parent.Guid], null, offset3, null, null, checkPosition: false);
				Vector3D thrust2 = -offset3.Normalized * velocity;
				Vector3D offset4 = new Vector3D(MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5) * radius2 * 2.0;
				ship3.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()) * 50.0;
				ship3.Orbit.InitFromStateVectors(ship3.Orbit.Parent, ship3.Orbit.Position + offset4, ship3.Orbit.Velocity + thrust2, Instance.SolarSystem.CurrentTime, areValuesRelative: false);
				return;
			}
			if (parts[0] == "setmain" && parts.Length == 1 && parent is SpaceObjectVessel objectVessel)
			{
				objectVessel.SetMainVessel();
				return;
			}
			if (parts[0] == "pressurize" && parts.Length == 1 && parent is SpaceObjectVessel)
			{
				if (player.CurrentRoom != null)
				{
					player.CurrentRoom.CompoundRoom.AirPressure = 1f;
					player.CurrentRoom.CompoundRoom.AirQuality = 1f;
				}
				return;
			}
			if (parts[0] == "airquality" && parts.Length == 2 && parent is SpaceObjectVessel)
			{
				if (player.CurrentRoom != null && float.TryParse(parts[1], out var aq))
				{
					player.CurrentRoom.CompoundRoom.AirQuality = aq;
				}
				return;
			}
			if (parts[0] == "vent" && parts.Length == 1 && parent is SpaceObjectVessel)
			{
				if (player.CurrentRoom != null)
				{
					player.CurrentRoom.CompoundRoom.AirPressure = 0f;
				}
				return;
			}
			if (parts[0] == "torpedo")
			{
				Vector3D offset2 = player.LocalPosition + player.LocalRotation * QuaternionD.Euler(0f - player.MouseLook, 0.0, 0.0) * Vector3D.Forward * 25.0;
				Vector3D direction = (player.LocalRotation * QuaternionD.Euler(0f - player.MouseLook, 0.0, 0.0) * Vector3D.Forward).Normalized;
				long guid = player.Parent.Guid;
				if (parent is SpaceObjectVessel vessel7)
				{
					offset2 = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * (offset2 - vessel7.VesselData.CollidersCenterOffset.ToVector3D());
					direction = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * direction;
					guid = vessel7.MainVessel.Guid;
				}
				Ship ship = await Ship.CreateNewShip(GameScenes.SceneId.AltCorp_DockableContainer, "PHTORP MK4", -1L, new List<long> { guid }, null, offset2, null, null, checkPosition: false);
				ship.Forward = direction;
				Vector3D thrust = direction * 80.0;
				ship.Orbit.InitFromStateVectors(ship.Orbit.Parent, ship.Orbit.Position, ship.Orbit.Velocity + thrust, Instance.SolarSystem.CurrentTime, areValuesRelative: false);
				await ship.SetHealthAsync(5f);
				ship.VesselData.VesselName = "Torpedo";
				ship.Mass = 5000000.0;
				ship.MaxHealth = 50000f;
				if (parts.Length == 2)
				{
					if (float.TryParse(parts[1], out var time))
					{
						ship.SelfDestructTimer = new SelfDestructTimer(ship, time);
					}
				}
				else
				{
					ship.SelfDestructTimer = new SelfDestructTimer(ship, 60f);
				}
				return;
			}
			if ((parts[0] == "countships" || parts[0] == "countitems") && parts.Length is 1 or 2)
			{
				double radius = 2000.0;
				string msg = "";
				List<SpaceObjectVessel> artificialBodies;
				if (parts.Length == 1 || (parts.Length == 2 && double.TryParse(parts[1], out radius)))
				{
					artificialBodies = (from m in SolarSystem.GetArtificialBodieslsInRange(parent as ArtificialBody, radius)
						where m is SpaceObjectVessel && m != parent
						select m as SpaceObjectVessel).ToList();
				}
				else
				{
					CelestialBodyData cbd = StaticData.SolarSystem.CelestialBodies.FirstOrDefault((CelestialBodyData m) => m.Name.ToLower() == parts[1].ToLower());
					if (cbd == null)
					{
						cbd = StaticData.SolarSystem.CelestialBodies.FirstOrDefault((CelestialBodyData m) => m.Name.Contains(parts[1], StringComparison.CurrentCultureIgnoreCase));
						if (cbd == null)
						{
							return;
						}
					}
					CelestialBody cb = SolarSystem.GetCelestialBodies().FirstOrDefault((CelestialBody m) => m.GUID == cbd.GUID);
					radius = cb.Orbit.GravityInfluenceRadius;
					artificialBodies = (from m in SolarSystem.GetArtificialBodieslsInRange(cb, Vector3D.Zero, radius)
						where m is SpaceObjectVessel
						select m as SpaceObjectVessel).ToList();
					msg = cbd.Name + ", r=" + (int)(radius / 1000.0) + "km";
				}
				Dictionary<string, int> count = new Dictionary<string, int>();
				if (parts[0] == "countships")
				{
					foreach (SpaceObjectVessel ves2 in artificialBodies)
					{
						string name2 = ves2.SceneID.ToString();
						if (!count.ContainsKey(name2))
						{
							count[name2] = 0;
						}
						count[name2]++;
					}
				}
				else if (parts[0] == "countitems")
				{
					foreach (SpaceObjectVessel ves in artificialBodies)
					{
						foreach (Item item in from m in _spaceObjects.Values
							where m is DynamicObject dynamicObject && dynamicObject.Parent == ves && dynamicObject.Item != null
							select (m as DynamicObject).Item)
						{
							string name = item.TypeName;
							if (!count.ContainsKey(name))
							{
								count[name] = 0;
							}
							count[name]++;
						}
					}
				}
				foreach (KeyValuePair<string, int> kv in count)
				{
					msg = msg + (msg == "" ? "" : "\r\n") + kv.Key + ": " + kv.Value;
				}
				await NetworkController.SendAsync(player.Guid, new ConsoleMessage
				{
					Text = msg
				});
				return;
			}
			if (parts[0] == "station" && parts.Length == 2)
			{
				await StationBlueprint.AssembleStation(parts[1], "JsonStation", "JsonStation", null, parent.Guid);
				return;
			}
			if (parts[0] == "collision" && parts.Length == 2)
			{
				if (player.Parent is not SpaceObjectVessel vessel5)
				{
					return;
				}

				vessel5.MainVessel.RigidBody.CollisionFlags = parts[1] == "0" ? CollisionFlags.NoContactResponse : CollisionFlags.None;
				{
					foreach (SpaceObjectVessel v3 in vessel5.MainVessel.AllDockedVessels)
					{
						v3.RigidBody.CollisionFlags = parts[1] == "0" ? CollisionFlags.NoContactResponse : CollisionFlags.None;
					}
					return;
				}
			}
			if (parts[0] == "god")
			{
				if (parts.Length == 2)
				{
					player.Stats.GodMode = parts[1] != "0";
				}
				await NetworkController.SendAsync(player.Guid, new ConsoleMessage
				{
					Text = "God mode: " + (player.Stats.GodMode ? "ON" : "OFF")
				});
				return;
			}
			if (parts[0] == "teleport" && parts.Length == 2)
			{
				ArtificialBody target = null;
				Player p = _players.Values.FirstOrDefault((Player m) => m.PlayerId == parts[1] || m.Name.ToLower() == parts[1].ToLower());
				if (p is { Parent: ArtificialBody body })
				{
					target = body is not SpaceObjectVessel vessel ? body : vessel.MainVessel;
				}
				else
				{
					SpaceObjectVessel v2 = (from m in SolarSystem.GetArtificialBodies()
						where m is SpaceObjectVessel
						select m as SpaceObjectVessel).FirstOrDefault((SpaceObjectVessel m) => m.FullName.Replace(' ', '_').Contains(parts[1], StringComparison.CurrentCultureIgnoreCase));
					if (v2 != null)
					{
						target = v2.MainVessel;
					}
				}
				ArtificialBody myAb = parent is SpaceObjectVessel spaceObjectVessel ? spaceObjectVessel.MainVessel : parent as ArtificialBody;
				if (target != null && target != myAb)
				{
					await myAb.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
					myAb.Orbit.CopyDataFrom(target.Orbit, SolarSystem.CurrentTime, exactCopy: true);
					if (myAb is Pivot)
					{
						myAb.Orbit.RelativePosition -= player.LocalPosition + player.LocalRotation * Vector3D.Forward * (target.Radius + 100.0);
						myAb.Orbit.RelativeVelocity -= player.LocalVelocity;
					}
					else
					{
						myAb.Orbit.RelativePosition -= myAb.Forward * (target.Radius + 100.0);
					}
					myAb.Orbit.InitFromCurrentStateVectors(SolarSystem.CurrentTime);
					myAb.Orbit.UpdateOrbit();
				}
				return;
			}
			if (parts[0] == "respawn" && parts.Length == 2)
			{
				await SpawnManager.RespawnBlueprintRule(parts[1]);
				return;
			}
			if (parts[0] == "sethealth" && parts.Length == 2)
			{
				if (!float.TryParse(parts[1], out var health))
				{
					return;
				}
				if (player.PlayerInventory.HandsSlot.Item != null)
				{
					player.PlayerInventory.HandsSlot.Item.Health = health;
					await player.PlayerInventory.HandsSlot.Item.DynamicObj.SendStatsToClient();
				}
				else
				{
					if (parent is not SpaceObjectVessel vessel4)
					{
						return;
					}

					foreach (VesselRepairPoint vrp in vessel4.RepairPoints)
					{
						await vrp.SetHealthAsync(vrp.MaxHealth);
					}
					await vessel4.SetHealthAsync(health);
					vessel4.MainVessel.UpdateVesselData();
				}
				return;
			}
			if (parts[0] == "spawnrulescleanup")
			{
				ArtificialBody[] artificialBodies2 = SolarSystem.GetArtificialBodies();
				foreach (ArtificialBody ab in artificialBodies2)
				{
					if (ab is not SpaceObjectVessel vessel || (!vessel.IsInvulnerable && !vessel.IsPartOfSpawnSystem && vessel is not Asteroid))
					{
						continue;
					}
					bool found = false;
					foreach (SpawnRule sr2 in SpawnManager.spawnRules)
					{
						if (sr2.SpawnedVessels.FirstOrDefault((SpaceObjectVessel m) => m.Guid == ab.Guid) != null)
						{
							found = true;
							break;
						}
					}
					if (!found)
					{
						vessel.MarkForDestruction = true;
						SpawnManager.SpawnedVessels.TryRemove(vessel.Guid, out _);
					}
				}
				return;
			}
			if (parts[0] == "unmatchall")
			{
				ArtificialBody[] artificialBodies3 = SolarSystem.GetArtificialBodies();
				foreach (ArtificialBody ab2 in artificialBodies3)
				{
					await ab2.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
				}
				return;
			}
			if (parts[0] == "resetblueprints")
			{
				foreach (Player pl in _players.Values)
				{
					pl.Blueprints = ObjectCopier.DeepCopy(StaticData.DefaultBlueprints);
					await NetworkController.SendAsync(pl.Guid, new UpdateBlueprintsMessage
					{
						Blueprints = pl.Blueprints
					});
				}
				return;
			}
			if (parts[0] == "blink" && parts.Length == 2)
			{
				double.TryParse(parts[1], out var dist);
				if (parent is SpaceObjectVessel vessel3)
				{
					await vessel3.MainVessel.DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
					vessel3.MainVessel.Orbit.RelativePosition += vessel3.Forward * dist * 1000.0;
					vessel3.MainVessel.Orbit.InitFromCurrentStateVectors(SolarSystemTime);
					vessel3.MainVessel.Orbit.UpdateOrbit();
				}
			}
			else if (parts[0] == "whereami" && parts.Length == 1)
			{
				string msg2;
				if (parent is SpaceObjectVessel vessel)
				{
					msg2 = "Inside vessel '" + vessel.VesselData.VesselName + "' near " + (CelestialBodyGUID)vessel.Orbit.Parent.CelestialBody.GUID;
					foreach (SpawnRule sr in SpawnManager.spawnRules)
					{
						if (sr.SpawnedVessels.FirstOrDefault((SpaceObjectVessel m) => m == vessel) != null)
						{
							msg2 = msg2 + ", spawn rule '" + sr.Name + "'";
						}
					}
				}
				else
				{
					if (parent is not Pivot pivot)
					{
						return;
					}
					msg2 = "Near " + pivot.Orbit.Parent.CelestialBody;
				}
				await NetworkController.SendAsync(player.Guid, new ConsoleMessage
				{
					Text = msg2
				});
			}
			else
			{
				if (parts[0] != "restock" || parts.Length != 1 || parent is not SpaceObjectVessel vessel)
				{
					return;
				}
				List<SpaceObjectVessel> list = [vessel.MainVessel];
				{
					foreach (SpaceObjectVessel vessel2 in list.Concat(vessel.MainVessel.AllDockedVessels))
					{
						foreach (VesselAttachPoint ap in vessel2.AttachPoints.Values.Where((VesselAttachPoint m) => m.Item == null))
						{
							ItemType it = ap.ItemTypes.OrderBy((ItemType m) => MathHelper.RandomNextDouble()).FirstOrDefault();
							GenericItemSubType gt = GenericItemSubType.None;
							MachineryPartType mt = MachineryPartType.None;
							if (it == ItemType.None)
							{
								gt = ap.GenericSubTypes.OrderBy((GenericItemSubType m) => MathHelper.RandomNextDouble()).FirstOrDefault();
								if (gt == GenericItemSubType.None)
								{
									mt = ap.MachineryPartTypes.OrderBy((MachineryPartType m) => MathHelper.RandomNextDouble()).FirstOrDefault();
									if (mt == MachineryPartType.None)
									{
										continue;
									}
									it = ItemType.MachineryPart;
								}
								else
								{
									it = ItemType.GenericItem;
								}
							}
							await DynamicObject.SpawnDynamicObject(it, gt, mt, vessel2, ap.InSceneID, null, null, null, MathHelper.RandomRange(1, 5), null, refill: true);
							if (mt != 0)
							{
								vessel2.MainDistributionManager.GetVesselComponentByPartSlot(ap.Item.AttachPointID)?.FitPartToSlot(ap.Item.AttachPointID, (MachineryPart)ap.Item);
							}
						}
					}
				}
			}
		}
		catch (Exception)
		{
			// ignored
		}
	}

	public TextChatMessage SendSystemMessage(SystemMessagesTypes type, Ship sh)
	{
		TextChatMessage tcm = new TextChatMessage
		{
			GUID = -1L,
			Name = "System",
			MessageType = type,
			MessageText = ""
		};
		switch (type)
		{
		case SystemMessagesTypes.DoomedOutpostSpawned:
			if (sh.Orbit.Parent is { CelestialBody: not null })
			{
				TimeSpan t = new TimeSpan(0, 0, (int)sh.TimeToLive);
				tcm.MessageParam = new string[3]
				{
					sh.VesselData.VesselRegistration,
					((CelestialBodyGUID)sh.Orbit.Parent.CelestialBody.GUID).ToString(),
					t.ToString()
				};
			}
			break;
		case SystemMessagesTypes.RestartServerTime:
		{
					string timeLeftToRestart = !(_timeToRestart <= 10.0) ? (_timeToRestart / 60.0).ToString() : _timeToRestart.ToString();
					tcm.MessageParam = new string[2]
			{
				timeLeftToRestart,
				_timeToRestart <= 10.0 ? "seconds" : "minutes"
			};
			break;
		}
		}
		tcm.Local = false;
		return tcm;
	}

	private async Task<NetworkData> PlayerSpawnRequestListener(NetworkData data)
	{
		try
		{
			Debug.Log("Parsing PlayerSpawnRequest.");
			var request = data as PlayerSpawnRequest;
			Player pl = GetPlayer(request.Sender);
			if (pl == null)
			{
				Debug.LogError("Player spawn request error, player is null", request.Sender);
				return new PlayerSpawnResponse() { Status = NetworkData.MessageStatus.Failure };
			}

			pl.MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
			var spawnResponse = new PlayerSpawnResponse();

			bool spawnSuccess;
			if (pl.IsAlive)
			{
				spawnSuccess = pl.Parent is ArtificialBody;
			}
			else
			{
				spawnSuccess = await AddPlayerToShipAsync(pl, request.SpawnSetupType, request.SpawnPointParentId);
				Debug.LogFormat("Adding player ({2}) to ship with id {0}, with setup type {1}. Success? {3}.", request.SpawnPointParentId, request.SpawnSetupType, pl.Guid, spawnSuccess);
			}

			if (spawnSuccess)
			{
				spawnResponse.Status = NetworkData.MessageStatus.Success;
				if (pl.Parent is Pivot { StabilizeToTargetObj: not null } pivot)
				{
					await pivot.DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
				}
				if (pl.Parent != null && !pl.IsSubscribedTo(pl.Parent.Guid))
				{
					pl.SubscribeTo(pl.Parent);
				}

				ArtificialBody parentBody = pl.Parent as ArtificialBody;

				spawnResponse.ParentID = parentBody.Guid;
				spawnResponse.ParentType = parentBody.ObjectType;
				spawnResponse.MainVesselID = parentBody.Guid;

				SpaceObjectVessel mainVessel = parentBody as SpaceObjectVessel;
				if (mainVessel is { IsDocked: true })
				{
					mainVessel = mainVessel.DockedToMainVessel;
					spawnResponse.MainVesselID = mainVessel.Guid;
				}

				ArtificialBody mainAb = mainVessel != null ? mainVessel : parentBody;
				spawnResponse.ParentTransform = new ObjectTransform
				{
					GUID = mainAb.Guid,
					Type = mainAb.ObjectType,
					Forward = mainAb.Forward.ToFloatArray(),
					Up = mainAb.Up.ToFloatArray()
				};

				if (parentBody != null)
				{
					List<SpaceObjectVessel> nearbyVessels = [.. from m in SolarSystem.GetArtificialBodieslsInRange(parentBody, 10000.0)
													where m is SpaceObjectVessel
													select m as SpaceObjectVessel];
					if (parentBody is SpaceObjectVessel parentVessel)
					{
						nearbyVessels.Add(parentVessel);
					}

					HashSet<GameScenes.SceneId> allNearbyVessels = [];
					foreach (SpaceObjectVessel vessel in nearbyVessels)
					{
						allNearbyVessels.Add(vessel.SceneID);
						foreach (SpaceObjectVessel dockedVessels in vessel.AllDockedVessels)
						{
							allNearbyVessels.Add(dockedVessels.SceneID);
						}
					}
					spawnResponse.Scenes = [.. allNearbyVessels];
				}

				if (mainVessel is Ship mainShip)
				{
					spawnResponse.DockedVessels = mainShip.GetDockedVesselsData();
					spawnResponse.VesselData = mainShip.VesselData;
					spawnResponse.VesselObjects = mainShip.GetVesselObjects();
				}
				else if (mainVessel is Asteroid asteroid)
				{
					spawnResponse.VesselData = asteroid.VesselData;
					spawnResponse.MiningPoints = asteroid.MiningPoints.Values.Select((AsteroidMiningPoint m) => m.GetDetails()).ToList();
				}

				if (mainAb.Orbit.IsOrbitValid)
				{
					spawnResponse.ParentTransform.Orbit = new OrbitData
					{
						ParentGUID = mainAb.Orbit.Parent.CelestialBody.GUID
					};
					mainAb.Orbit.FillOrbitData(ref spawnResponse.ParentTransform.Orbit);
				}
				else
				{
					spawnResponse.ParentTransform.Realtime = new RealtimeData
					{
						ParentGUID = mainAb.Orbit.Parent.CelestialBody.GUID,
						Position = mainAb.Orbit.RelativePosition.ToArray(),
						Velocity = mainAb.Orbit.Velocity.ToArray()
					};
				}

				if (pl.CurrentSpawnPoint != null
					&& ((pl.CurrentSpawnPoint.IsPlayerInSpawnPoint && pl.CurrentSpawnPoint.Ship == pl.Parent)
					|| (pl.CurrentSpawnPoint.Type == SpawnPointType.SimpleSpawn && pl.CurrentSpawnPoint.Executor == null && !pl.IsAlive)))
				{
					spawnResponse.SpawnPointID = pl.CurrentSpawnPoint.SpawnPointID;
				}
				else
				{
					spawnResponse.CharacterTransform = new CharacterTransformData
					{
						LocalPosition = pl.LocalPosition.ToFloatArray(),
						LocalRotation = pl.LocalRotation.ToFloatArray()
					};
				}

				var playerObjects = pl.DynamicObjects.Values.Select(dobj => dobj.GetDetails()).ToList();
				spawnResponse.DynamicObjects = playerObjects;

				if (pl.AuthorizedSpawnPoint != null)
				{
					spawnResponse.HomeGUID = pl.AuthorizedSpawnPoint.Ship.MainVessel.Guid;
				}

				if (ServerRestartTimeSec > 0.0)
				{
					spawnResponse.TimeUntilServerRestart = (RestartTime - DateTime.UtcNow).TotalSeconds;
				}

				spawnResponse.Quests = [.. pl.Quests.Select((Quest m) => m.GetDetails())];
				spawnResponse.Blueprints = pl.Blueprints;
				spawnResponse.NavMapDetails = pl.NavMapDetails;
				spawnResponse.Health = pl.Health;
				spawnResponse.IsAdmin = pl.IsAdmin;
			}
			else
			{
				spawnResponse.Status = NetworkData.MessageStatus.Failure;
			}
			await SolarSystem.SendMovementMessageToPlayer(pl);

			Debug.Log("Spawn Request handled");
			return spawnResponse;
		}
		catch (Exception ex)
		{
			Debug.LogException(ex);
			return new PlayerSpawnResponse() { Status = NetworkData.MessageStatus.Failure };
		}
	}

	private void PlayerRespawnRequestListener(NetworkData data)
	{
	}

	private async void SpawnObjectsRequestListener(NetworkData data)
	{
		var request = data as SpawnObjectsRequest;
		Player pl = GetPlayer(request.Sender);
		if (pl == null)
		{
			return;
		}
		var response = new SpawnObjectsResponse();

		foreach (long guid in request.GUIDs)
		{
			SpaceObject spaceObject = GetObject(guid);
			if (spaceObject is not null && (spaceObject is not SpaceObjectVessel vessel || vessel.IsMainVessel))
			{
				response.Data.Add(spaceObject.GetSpawnResponseData(pl));
			}
		}

		await NetworkController.SendAsync(request.Sender, response);
	}

	private async Task UpdateDynamicObjectsRespawnTimers(double deltaTime)
	{
		List<DynamicObjectsRespawn> toRemove = new List<DynamicObjectsRespawn>();
		foreach (DynamicObjectsRespawn dos2 in DynamicObjectsRespawnList)
		{
			if (dos2.Timer > 0.0)
			{
				dos2.Timer -= deltaTime;
			}
			else
			{
				toRemove.Add(dos2);
			}
		}
		if (toRemove.Count <= 0)
		{
			return;
		}
		foreach (DynamicObjectsRespawn dos in toRemove)
		{
			if (dos.Parent is SpaceObjectVessel && dos.Parent.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item is
			    {
				    AttachPointID: not null
			    } && dos.ApDetails != null && m.Item.AttachPointID.InSceneID == dos.ApDetails.InSceneID) != null)
			{
				dos.Timer = dos.RespawnTime;
				continue;
			}
			DynamicObjectsRespawnList.Remove(dos);
			if (_vessels.ContainsKey(dos.Parent.Guid))
			{
				DynamicObject dobj = await DynamicObject.CreateDynamicObjectAsync(dos.Data, dos.Parent, -1L);
				if (dos.Data.AttachPointInSceneId > 0 && dobj.Item != null)
				{
					dobj.Item.SetAttachPoint(dos.ApDetails);
				}
				dobj.APDetails = dos.ApDetails;
				dobj.RespawnTime = dos.Data.SpawnSettings.Length != 0 ? dos.Data.SpawnSettings[0].RespawnTime : -1f;
				if (dobj.Item is not null && dos.MaxHealth >= 0f && dos.MinHealth >= 0f)
				{
					IDamageable idmg = dobj.Item;
					idmg.Health = (int)(idmg.MaxHealth * MathHelper.Clamp(MathHelper.RandomRange(dos.MinHealth, dos.MaxHealth), 0f, 1f));
				}
				SpawnObjectsResponse res = new SpawnObjectsResponse();
				res.Data.Add(dobj.GetSpawnResponseData(null));
				await NetworkController.SendToClientsSubscribedTo(res, -1L, dos.Parent);
			}
		}
		toRemove.Clear();
	}

	private async Task UpdateData(double deltaTime)
	{
		SolarSystem.UpdateTime(deltaTime);
		await SolarSystem.UpdatePositions();
		PhysicsController.Update();
		await UpdateDynamicObjectsRespawnTimers(deltaTime);
		await UpdateObjectTimers(deltaTime);
		UpdatePlayerInvitationTimers(deltaTime);

		_movementMessageTimer += deltaTime;
		if (_movementMessageTimer >= MovementMessageSendInterval)
		{
			_movementMessageTimer = 0.0;
			await SolarSystem.SendMovementMessage();
		}

		if (!VesselsDataUpdate.IsEmpty)
		{
			await NetworkController.SendToAllAsync(new UpdateVesselDataMessage
			{
				VesselsDataUpdate = VesselsDataUpdate.Values.ToList()
			});
			VesselsDataUpdate.Clear();
		}
	}

	public async Task RemoveWorldObjects()
	{
		Debug.LogInfo("REMOVING ALL WORLD OBJECTS");
		NetworkController.DisconnectAllClients();
		_players.Clear();
		_spaceObjects.Clear();
		ArtificialBody[] artificialBodies = Instance.SolarSystem.GetArtificialBodies();
		await Parallel.ForEachAsync(artificialBodies, async (ab, ct) =>
		{
			if (ab is Ship ship)
			{
				await ship.Destroy();
			}
			else if (ab is Asteroid asteroid)
			{
				await asteroid.Destroy();
			}
			else
			{
				Instance.SolarSystem.RemoveArtificialBody(ab);
			}
		});
		_vessels.Clear();
		WorldInitialized = false;
	}

	public async Task DestroyArtificialBody(ArtificialBody ab, bool destroyChildren = true, bool vesselExploded = false)
	{
		if (ab == null)
		{
			return;
		}
		if (ab is SpaceObjectVessel ves)
		{
			if (destroyChildren && ves.AllDockedVessels.Count > 0)
			{
				await Parallel.ForEachAsync(ves.AllDockedVessels, async (child, ct) =>
				{
					await DestroyArtificialBody(child, destroyChildren: false, vesselExploded);
				});
			}
			await Parallel.ForEachAsync(ves.VesselCrew, async (pl, ct) =>
			{
				await pl.KillPlayer(HurtType.Shipwreck, createCorpse: false);
			});
			if (vesselExploded)
			{
				await ves.DamageVesselsInExplosionRadius();
				await NetworkController.SendToClientsSubscribedTo(new DestroyVesselMessage
				{
					GUID = ves.Guid
				}, -1L, ves);
			}
			await ves.Destroy();
		}
		else
		{
			await ab.Destroy();
		}
	}

	public async Task UpdateObjectTimers(double deltaTime)
	{
		HashSet<SpaceObjectVessel> destroyVessels = null;
		foreach (SpaceObjectVessel vessel in AllVessels)
		{
			await vessel.UpdateTimers(deltaTime);
			if (vessel is not Ship || !(vessel.Health < float.Epsilon))
			{
				continue;
			}
			if (vessel.DockedToVessel == null && vessel.DockedVessels.Count == 0)
			{
				if (destroyVessels == null)
				{
					destroyVessels = new HashSet<SpaceObjectVessel>();
				}
				destroyVessels.Add(vessel.DockedToMainVessel is not null ? vessel.DockedToMainVessel : vessel);
			}
			else
			{
				vessel.UndockAll();
			}
		}
		if (destroyVessels != null)
		{
			await Parallel.ForEachAsync(destroyVessels, async (dv, ct) =>
			{
				await DestroyArtificialBody(dv, destroyChildren: true, vesselExploded: true);
			});
		}
		foreach (DynamicObject dobj in _updateableDynamicObjects.Values)
		{
			await (dobj.Item as IUpdateable).Update(deltaTime);
		}
		await Parallel.ForEachAsync(AllPlayers, async (pl, ct) =>
		{
			await pl.UpdateTimers(deltaTime);
		});
		foreach (DebrisField df in DebrisFields)
		{
			df.SpawnFragments();
		}
	}

	private void PrintObjectsDebug(double time)
	{
		Debug.LogInfo("Server stats, objects", _spaceObjects.Count, "players", _players.Count, "vessels", _vessels.Count, "artificial bodies", SolarSystem.ArtificialBodiesCount);
	}

	public async Task MainLoop()
	{
		SolarSystem.InitializeData();
		InitializeDebrisFields();
		if (_cleanStart || PersistenceSaveInterval < 0.0 || (_cleanStart = !await Persistence.Load(_loadPersistenceFromFile)))
		{
			if (_solarSystemStartTime < 0.0)
			{
				SolarSystem.CalculatePositionsAfterTime(MathHelper.RandomRange(86400.0, 5256000.0));
			}
			else
			{
				SolarSystem.CalculatePositionsAfterTime(_solarSystemStartTime);
			}

			if (!WorldInitialized)
			{
				await SpawnManager.Initialize();
				WorldInitialized = true;
			}
		}
		else
		{
			WorldInitialized = true;
		}
		await Start();
		NetworkController.Start();
		_tickMilliseconds = System.Math.Floor(1000.0 / _numberOfTicks);
		_lastTime = DateTime.UtcNow;
		if (ServerRestartTimeSec > 0.0)
		{
			RestartTime = DateTime.UtcNow.AddSeconds(ServerRestartTimeSec);
			SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, ServerAutoRestartTimer);
		}
		DoomedShipController.SubscribeToTimer();
		bool hadSleep = true;
		DateTime lastServerTickedWithoutSleepTime = DateTime.MinValue;
		if (_printDebugObjects)
		{
			SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_hr, PrintObjectsDebug);
		}
		SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, UpdateShipSystemsTimer);
		_mainLoopStarted = true;

		new Thread(StartMainLoopWatcher).Start();
		while (IsRunning)
		{
			DateTime currentTime = DateTime.UtcNow;
			TimeSpan span = currentTime - _lastTime;
			if (span.TotalMilliseconds >= _tickMilliseconds)
			{
				AddRemovePlayers();
				if (_printDebugObjects && !hadSleep && (currentTime - lastServerTickedWithoutSleepTime).TotalSeconds > 60.0)
				{
					Debug.LogInfoFormat("Server ticked without sleep. Time span ms {0}. Tick ms {1}. Objects {2}. Players {3}. Vessels {4}. Artificial bodies {5}.", (int)span.TotalMilliseconds, _tickMilliseconds, _spaceObjects.Count, _players.Count, _vessels.Count, SolarSystem.ArtificialBodiesCount);
					lastServerTickedWithoutSleepTime = currentTime;
				}
				hadSleep = false;
				DeltaTime = span.TotalSeconds;
				await UpdateData(DeltaTime);
				_lastTime = currentTime;
				foreach (UpdateTimer timer2 in _timers)
				{
					timer2.AddTime(DeltaTime);
				}
				if (_timersToRemove.Count > 0)
				{
					foreach (UpdateTimer timer in _timersToRemove)
					{
						if (timer.OnTick == null)
						{
							_timers.Remove(timer);
						}
					}
					_timersToRemove.Clear();
				}
				await SpawnManager.UpdateTimers(DeltaTime);
				if (PersistenceSaveInterval > 0.0 || _manualSave)
				{
					_persistenceSaveTimer += span.TotalSeconds;
					if (_persistenceSaveTimer >= PersistenceSaveInterval || _manualSave)
					{
						_persistenceSaveTimer = 0.0;
						Persistence.Save(_manualSaveFileName, _manualSaveAuxData);
					}
					_manualSave = false;
					_manualSaveFileName = null;
					_manualSaveAuxData = null;
				}
			}
			else
			{
				hadSleep = true;
				await Task.Delay((int)(_tickMilliseconds - span.TotalMilliseconds));
			}
		}

		// Shutting down...
		Debug.Log("Main game loop ended; Shutting down server...");

		NetworkController.Stop();

		if (SavePersistenceDataOnShutdown)
		{
			Persistence.Save();
		}

		MainLoopEnded.Set();
		if (ParentProcess.FileName == "GameServerWatchdog.exe" && !ParentProcess.GetParentProcess().HasExited)
		{
			Restart = false;
		}

		if (Restart)
		{
			RestartServer(CleanRestart);
		}
		else
		{
			try
			{
				await MainServerConnection.Send(new UnregisterServer()
				{
					GamePort = GamePort,
					StatusPort = StatusPort,
					ServerId = NetworkController.ServerId
				});
			}
			catch
			{
				// Ignored
			}
		}
	}

	private void StartMainLoopWatcher()
	{
		double lastSolarSystemTime = SolarSystemTime;
		bool logEvent = true;
		while (IsRunning)
		{
			if (SolarSystemTime - lastSolarSystemTime > 5.0)
			{
				if (logEvent)
				{
					Debug.LogWarning("Main loop stuck for more than 5 sec.");
				}
				logEvent = false;
			}
			else
			{
				lastSolarSystemTime = SolarSystemTime;
				logEvent = true;
			}
			Thread.Sleep(1000);
		}
	}

	private void AddRemovePlayers()
	{
		foreach (Player player2 in _playersToAdd)
		{
			_players[player2.Guid] = player2;
			_spaceObjects[player2.FakeGuid] = player2;
		}
		_playersToAdd = new ConcurrentBag<Player>();
		foreach (Player player in _playersToRemove)
		{
			_players.TryRemove(player.Guid, out _);
			_spaceObjects.TryRemove(player.FakeGuid, out _);
		}
		_playersToRemove = new ConcurrentBag<Player>();
	}

	public static void RestartServer(bool clean)
	{
		string fileName = "";
		string arguments = "";
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		foreach (string arg in commandLineArgs)
		{
			if (fileName == "")
			{
				fileName = arg;
			}
			else if (arg.ToLower() != "-noload" && arg.ToLower() != "-clean")
			{
				arguments = arguments + arg + " ";
			}
		}
		if (clean)
		{
			arguments += " -noload";
		}
		Process.Start(fileName, arguments);
		Process.GetCurrentProcess().Kill();
	}

	private void ToggleLockDoor(Ship ship, short inSceneId, bool isLocked)
	{
		VesselDockingPort port = ship.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == inSceneId);
		if (port == null)
		{
			throw new KeyNotFoundException("Could not find door with id: " + inSceneId);
		}
		int[] doorsIDs = port.DoorsIDs;
		for (int i = 0; i < doorsIDs.Length; i++)
		{
			short id = (short)doorsIDs[i];
			Door d = ship.Doors.Find((Door m) => m.ID.InSceneID == id);
			if (d != null)
			{
				d.IsLocked = isLocked;
			}
		}
	}

	private void OpenDoor(Ship ship, short inSceneId, int newState)
	{
		ship.SceneTriggerExecutors.Find((SceneTriggerExecutor m) => m.InSceneID == inSceneId)?.ChangeState(0L, new SceneTriggerExecutorDetails
		{
			InSceneID = inSceneId,
			NewStateID = newState,
			CurrentStateID = 1,
			IsImmediate = true,
			IsFail = false,
			PlayerThatActivated = 0L
		});
	}

	private async void SubscribeToSpaceObjectListener(NetworkData data)
	{
		var message = data as SubscribeToObjectsRequest;
		Player player = GetPlayer(message.Sender);
		if (player == null)
		{
			return;
		}
		await Parallel.ForEachAsync(message.GUIDs, async (guid, ct) =>
		{
			SpaceObject so = GetObject(guid);
			if (so != null)
			{
				player.SubscribeTo(so);
				await NetworkController.SendAsync(message.Sender, so.GetInitializeMessage());
				if (so is ArtificialBody)
				{
					player.UpdateArtificialBodyMovement.Add(so.Guid);
				}
			}
		});
	}

	private void UnsubscribeFromSpaceObjectListener(NetworkData data)
	{
		var req = data as UnsubscribeFromObjectsRequest;
		foreach (long guid in req.GUIDs)
		{
			SpaceObject so = GetObject(guid);
			if (so == null)
			{
				break;
			}
			if (_players.TryGetValue(req.Sender, out var player))
			{
				player.UnsubscribeFrom(so);
			}
		}
	}

	public void SubscribeToTimer(UpdateTimer.TimerStep step, UpdateTimer.TimeStepDelegate del)
	{
		UpdateTimer timer = _timers.Find((UpdateTimer x) => x.Step == step);
		if (timer == null)
		{
			timer = new UpdateTimer(step);
			_timers.Add(timer);
		}
		UpdateTimer updateTimer = timer;
		updateTimer.OnTick = (UpdateTimer.TimeStepDelegate)Delegate.Combine(updateTimer.OnTick, del);
	}

	public void UnsubscribeFromTimer(UpdateTimer.TimerStep step, UpdateTimer.TimeStepDelegate del)
	{
		UpdateTimer timer = _timers.Find((UpdateTimer x) => x.Step == step);
		if (timer != null)
		{
			timer.OnTick = (UpdateTimer.TimeStepDelegate)Delegate.Remove(timer.OnTick, del);
			if (timer.OnTick == null)
			{
				_timersToRemove.Add(timer);
			}
		}
	}

	public async void PlayersOnServerRequestListener(NetworkData data)
	{
		var req = data as PlayersOnServerRequest;
		SpaceObjectVessel ves = null;
		if (req.SpawnPointID != null)
		{
			ves = GetVessel(req.SpawnPointID.VesselGUID);
		}
		else if (req.SecuritySystemID != null)
		{
			ves = GetVessel(req.SecuritySystemID.VesselGUID);
		}
		if (ves == null)
		{
			return;
		}

		// Create request.
		PlayersOnServerResponse res = new PlayersOnServerResponse();
		if (req.SpawnPointID != null)
		{
			// If we are spawning on a spawnpoint.
			if (ves.SpawnPoints.Find(m => m.SpawnPointID == req.SpawnPointID.InSceneID) == null)
			{
				return;
			}
			res.SpawnPointID = new VesselObjectID(req.SpawnPointID.VesselGUID, req.SpawnPointID.InSceneID);

			res.PlayersOnServer = new List<PlayerOnServerData>();
			foreach (Player pl in NetworkController.GetAllConnectedPlayers())
			{
				if (!pl.PlayerId.IsNullOrEmpty())
				{
					res.PlayersOnServer.Add(new PlayerOnServerData
					{
						PlayerId = pl.PlayerId,
						Name = pl.Name,
						AlreadyHasInvite = _spawnPointInvites.ContainsKey(pl.PlayerId)
					});
				}
				if (pl.PlayerId.IsNullOrEmpty())
				{
					Debug.LogError("Player ID is null or empty", pl.Guid, pl.Name);
				}
			}
		}
		else
		{
			// If we are not spawning on a spawnpoint.
			if (req.SecuritySystemID == null)
			{
				return;
			}
			res.SecuritySystemID = new VesselObjectID(req.SecuritySystemID.VesselGUID, 0);

			res.PlayersOnServer = new List<PlayerOnServerData>();
			foreach (Player pl in NetworkController.GetAllConnectedPlayers())
			{
				if (!pl.PlayerId.IsNullOrEmpty())
				{
					res.PlayersOnServer.Add(new PlayerOnServerData
					{
						PlayerId = pl.PlayerId,
						Name = pl.Name,
						AlreadyHasInvite = false
					});
				}
				if (pl.PlayerId.IsNullOrEmpty())
				{
					Debug.LogError("Player ID is null or empty", pl.Guid, pl.Name);
				}
			}
		}

		await NetworkController.SendAsync(req.Sender, res);
	}

	public Task<NetworkData> AvailableSpawnPointsRequestListener(NetworkData data)
	{
		var request = data as AvailableSpawnPointsRequest;
		Player pl = GetPlayer(request.Sender);
		if (pl != null)
		{
			return Task.FromResult<NetworkData>(new AvailableSpawnPointsResponse
			{
				SpawnPoints = GetAvailableSpawnPoints(pl)
			});
		}

		return Task.FromResult<NetworkData>(null);
	}

	public void ServerShutDownMessageListener(NetworkData data)
	{
	}

	private async void NameTagMessageListener(NetworkData data)
	{
		var message = data as NameTagMessage;
		SpaceObjectVessel vessel = GetVessel(message.ID.VesselGUID);
		await NetworkController.SendToClientsSubscribedTo(message, -1L, vessel);
		try
		{
			vessel.NameTags.Find((NameTagData m) => m.InSceneID == message.ID.InSceneID).NameTagText = message.NameTagText;
		}
		catch
		{
		}
	}

	public bool IsAddressAutorized(string address)
	{
		try
		{
			if (address is "127.0.0.1" or "localhost")
			{
				return true;
			}
			byte[] bytes = System.Net.IPAddress.Parse(address).GetAddressBytes();
			Array.Reverse(bytes);
			uint addr = BitConverter.ToUInt32(bytes, 0);
			IpAddressRange[] adminIpAddressRanges = _adminIpAddressRanges;
			foreach (IpAddressRange range in adminIpAddressRanges)
			{
				byte[] sBytes = System.Net.IPAddress.Parse(range.StartAddress).GetAddressBytes();
				Array.Reverse(sBytes);
				byte[] eBytes = System.Net.IPAddress.Parse(range.EndAddress).GetAddressBytes();
				Array.Reverse(eBytes);
				uint start = BitConverter.ToUInt32(sBytes, 0);
				uint end = BitConverter.ToUInt32(eBytes, 0);
				if (addr >= start && addr <= end)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public async void UpdatePlayerInvitationTimers(double deltaTime)
	{
		if (_spawnPointInvites.Count == 0)
		{
			return;
		}
		List<string> removedKeys = null;
		foreach (KeyValuePair<string, SpawnPointInviteData> item in _spawnPointInvites)
		{
			item.Value.InviteTimer -= deltaTime;
			if (item.Value.InviteTimer <= 0.0)
			{
				if (removedKeys == null)
				{
					removedKeys = new List<string>();
				}
				removedKeys.Add(item.Key);
			}
		}
		if (removedKeys is not { Count: > 0 })
		{
			return;
		}
		foreach (string key in removedKeys)
		{
			await ClearSpawnPointInvitation(key);
		}
	}

	public async Task ClearSpawnPointInvitation(string playerId)
	{
		if (_spawnPointInvites.ContainsKey(playerId))
		{
			await _spawnPointInvites[playerId].SpawnPoint.SetInvitation("", "", true);
			_spawnPointInvites.Remove(playerId);
		}
	}

	public async Task CreateSpawnPointInvitation(ShipSpawnPoint sp, string playerId, string playerName)
	{
		_spawnPointInvites.Add(playerId, new SpawnPointInviteData
		{
			SpawnPoint = sp,
			InviteTimer = SpawnPointInviteTimer
		});
		await sp.SetInvitation(playerId, playerName, true);
	}

	public async Task<bool> PlayerInviteChanged(ShipSpawnPoint sp, string invitedPlayerId, string invitedPlayerName, Player sender)
	{
		if (!invitedPlayerId.IsNullOrEmpty())
		{
			if (_spawnPointInvites.ContainsKey(invitedPlayerId) && _spawnPointInvites[invitedPlayerId].SpawnPoint == sp && sp.InvitedPlayerId == invitedPlayerId)
			{
				return false;
			}
			if (_spawnPointInvites.ContainsKey(invitedPlayerId))
			{
				await ClearSpawnPointInvitation(invitedPlayerId);
			}
			if (!sp.InvitedPlayerId.IsNullOrEmpty() && _spawnPointInvites.ContainsKey(sp.InvitedPlayerId))
			{
				await ClearSpawnPointInvitation(sp.InvitedPlayerId);
			}
			await CreateSpawnPointInvitation(sp, invitedPlayerId, invitedPlayerName);
			return true;
		}
		if (!sp.InvitedPlayerId.IsNullOrEmpty())
		{
			if (_spawnPointInvites.ContainsKey(sp.InvitedPlayerId))
			{
				await ClearSpawnPointInvitation(sp.InvitedPlayerId);
			}
			else
			{
				await sp.SetInvitation("", "", sendMessage: true);
			}
			return true;
		}
		return false;
	}

	private async void ServerAutoRestartTimer(double time)
	{
		DateTime currentTime = DateTime.UtcNow;
		if (currentTime.AddSeconds(_timeToRestart) > RestartTime)
		{
			if ((RestartTime - currentTime).TotalSeconds >= _timeToRestart - 2.0)
			{
				await NetworkController.SendToAllAsync(SendSystemMessage(SystemMessagesTypes.RestartServerTime, null), -1L);
			}
			if (_timeToRestart == 1800.0)
			{
				_timeToRestart = 900.0;
			}
			else if (_timeToRestart == 900.0)
			{
				_timeToRestart = 300.0;
			}
			else if (_timeToRestart == 300.0)
			{
				_timeToRestart = 60.0;
			}
			else if (_timeToRestart == 60.0)
			{
				_timeToRestart = 10.0;
			}
			else if (_timeToRestart <= 10.0)
			{
				_timeToRestart -= 1.0;
			}
			else
			{
				_timeToRestart -= 300.0;
			}
		}
		if (currentTime > RestartTime)
		{
			IsRunning = false;
			Restart = true;
			SavePersistenceDataOnShutdown = true;
			RestartTime = currentTime.AddSeconds(ServerRestartTimeSec);
		}
	}

	private async void UpdateShipSystemsTimer(double time)
	{
		if (_updatingShipSystems)
		{
			return;
		}
		_updatingShipSystems = true;
		var vessels = _vessels.Values.ToArray();
		foreach (SpaceObjectVessel vessel in vessels)
		{
			if (vessel is null) continue;
			await vessel.UpdateVesselSystems();
			vessel.DecayGraceTimer = MathHelper.Clamp(vessel.DecayGraceTimer - time, 0.0, double.MaxValue);
			if (vessel.DecayGraceTimer <= double.Epsilon)
			{
				await vessel.ChangeHealthBy(
					(float)((0f - vessel.ExposureDamage) * VesselDecayRateMultiplier * time), null,
					VesselRepairPoint.Priority.Internal, force: false, VesselDamageType.Decay, time);
			}
		}


		foreach (DebrisField debrisField in DebrisFields)
		{
			await debrisField.CheckVessels(time);
		}
		_updatingShipSystems = false;
	}

	public List<DebrisFieldDetails> GetDebrisFieldsDetails()
	{
		List<DebrisFieldDetails> list = new List<DebrisFieldDetails>();
		foreach (DebrisField df in DebrisFields)
		{
			list.Add(df.GetDetails());
		}
		return list;
	}

	private void InitializeDebrisFields()
	{
		Debug.Log("Initialising debris fields...");
		string dir = !ConfigDir.IsNullOrEmpty() && Directory.Exists(ConfigDir + "Data") ? ConfigDir : "";
		List<DebrisField.DebrisFieldData> list = JsonSerialiser.Load<List<DebrisField.DebrisFieldData>>(dir + "Data/DebrisFields.json");
		foreach (DebrisField.DebrisFieldData dfd in list)
		{
			if (dfd.DamageVesselChance > 0f && dfd.Damage.Max >= dfd.Damage.Min && dfd.Damage.Max > 0f)
			{
				DebrisFields.Add(new DebrisField(dfd));
			}
		}
	}

	private async void ExplosionMessageListener(NetworkData data)
	{
		var message = data as ExplosionMessage;
		Item item = GetItem(message.ItemGUID);
		if (item == null)
		{
			return;
		}
		HashSet<long> affectedObjects = new HashSet<long>();
		if (message.AffectedGUIDs != null && message.AffectedGUIDs.Length != 0)
		{
			Vector3D itemPos = item.DynamicObj.Position;
			long[] affectedGuiDs = message.AffectedGUIDs;
			foreach (long affectedGuid in affectedGuiDs)
			{
				SpaceObject tmpSp = Instance.GetObject(affectedGuid);
				if ((tmpSp.Position - item.DynamicObj.Position).Magnitude < item.ExplosionRadius * 1.5f && affectedObjects.Add(tmpSp.Guid))
				{
					if (tmpSp is Player player)
					{
						await player.Stats.TakeDamage(HurtType.Explosion, item.ExplosionDamage);
					}
					if (tmpSp is DynamicObject { Item: not null } dynamicObject)
					{
						await dynamicObject.Item.TakeDamage(new Dictionary<TypeOfDamage, float> { { item.ExplosionDamageType, item.ExplosionDamage } });
					}
				}
			}
		}
		if (item.ExplosionDamageType == TypeOfDamage.Impact && item.DynamicObj.Parent is SpaceObjectVessel parentVessel && affectedObjects.Add(parentVessel.Guid))
		{
			List<VesselRepairPoint> list = null;
			if (message.RepairPointIDs != null)
			{
				list = new List<VesselRepairPoint>();
				VesselObjectID[] repairPointIDs = message.RepairPointIDs;
				foreach (VesselObjectID rpid in repairPointIDs)
				{
					SpaceObjectVessel vessel = Instance.GetVessel(rpid.VesselGUID);
					if (vessel != null)
					{
						list.Add(parentVessel.RepairPoints.Find((VesselRepairPoint m) => m.ID.InSceneID == rpid.InSceneID));
					}
				}
			}
			if (await parentVessel.ChangeHealthBy(0f - item.ExplosionDamage, list, VesselRepairPoint.Priority.None, force: false, VesselDamageType.GrenadeExplosion) != 0f)
			{
				ShipCollisionMessage scm = new ShipCollisionMessage
				{
					CollisionVelocity = 0f,
					ShipOne = parentVessel.Guid,
					ShipTwo = -1L
				};
				await NetworkController.SendToClientsSubscribedTo(scm, -1L, parentVessel);
			}
		}

		Extensions.Invoke(async () =>
		{
			await item.DestroyItem();
		}, 1.0);
	}
}
