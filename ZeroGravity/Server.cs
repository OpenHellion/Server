using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BulletSharp;
using OpenHellion.Exceptions;
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

		public float WearMultiplier = 1f;
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
					Debug.Warning("No name tag for ship", sceneId);
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
				if (scene.ToString().ToLower().StartsWith(text.ToLower()))
				{
					return scene;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneId, string> kv2 in ShipNaming)
			{
				if (kv2.Value.ToLower().StartsWith(text.ToLower()))
				{
					return kv2.Key;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneId, string> kv in ShipNaming)
			{
				if (kv.Value.ToLower().Contains(text.ToLower()))
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

	private static bool CleanStart;

	public static bool Restart;

	public static bool CleanRestart = false;

	public const double CelestialBodyDeathDistance = 10000.0;

	private readonly Dictionary<long, SpaceObject> _objects = new Dictionary<long, SpaceObject>();

	private readonly Dictionary<long, Player> _players = new Dictionary<long, Player>();

	private ConcurrentBag<Player> _playersToAdd = new ConcurrentBag<Player>();

	private ConcurrentBag<Player> _playersToRemove = new ConcurrentBag<Player>();

	private readonly Dictionary<long, SpaceObjectVessel> _vessels = new Dictionary<long, SpaceObjectVessel>();

	private readonly Dictionary<long, DynamicObject> _updateableDynamicObjects = new Dictionary<long, DynamicObject>();

	private readonly List<UpdateTimer> _timersToRemove = new List<UpdateTimer>();

	private readonly List<UpdateTimer> _timers = new List<UpdateTimer>();

	public readonly SolarSystem SolarSystem;

	private static Server _serverInstance;

	public static bool IsRunning;

	private long _numberOfTicks = 64L;

	public static int GamePort = 6004;

	public static int StatusPort = 6005;

	public static bool SavePersistenceDataOnShutdown;

	private static bool _checkInPassed;

	public bool WorldInitialized;

	private readonly DateTime _serverStartTime = DateTime.UtcNow;

	private float _solarSystemStartTime = -1f;

	public int MaxPlayers = 100;

	public static int MaxNumberOfSaveFiles = 10;

	private IpAddressRange[] _adminIpAddressRanges = new IpAddressRange[0];

	private readonly Dictionary<string, SpawnPointInviteData> _spawnPointInvites = new Dictionary<string, SpawnPointInviteData>();

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

	public static double DamageUpgradePartChanceMultiplier = 1.0;

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

	public List<DeathMatchArenaController> DeathMatchArenaControllers = new List<DeathMatchArenaController>();

	public DoomedShipController DoomedShipController = new DoomedShipController();

	public static double MovementMessageSendInterval = 0.1;

	public static bool ForceMovementMessageSend;

	private static double _movementMessageTimer;

	public static volatile int MainThreadId;

	public BulletPhysicsController PhysicsController;

	public DateTime LastChatMessageTime = DateTime.UtcNow;

	private double _persistenceSaveTimer;

	private bool _printDebugObjects;

	private static readonly uint NetworkDataHash = ClassHasher.GetClassHashCode(typeof(NetworkData));

	private static readonly uint SceneDataHash = ClassHasher.GetClassHashCode(typeof(ISceneData));

	public static readonly uint CombinedHash = NetworkDataHash * SceneDataHash;

	private bool _manualSave;

	private string _manualSaveFileName;

	private SaveFileAuxData _manualSaveAuxData;

	private bool _updatingShipSystems;

	public ConcurrentDictionary<long, VesselDataUpdate> VesselsDataUpdate = new ConcurrentDictionary<long, VesselDataUpdate>();

	public List<DynamicObjectsRespawn> DynamicObjectsRespawnList = new List<DynamicObjectsRespawn>();

	private double _tickMilliseconds;

	public double DeltaTime;

	private DateTime _lastTime;

	public AutoResetEvent UpdateDataFinished = new AutoResetEvent(initialState: false);

	public static List<GameScenes.SceneId> RandomShipSpawnSceneIDs = new List<GameScenes.SceneId>
	{
		GameScenes.SceneId.AltCorp_CorridorModule,
		GameScenes.SceneId.AltCorp_CorridorIntersectionModule,
		GameScenes.SceneId.AltCorp_Corridor45TurnModule,
		GameScenes.SceneId.AltCorp_Shuttle_SARA,
		GameScenes.SceneId.ALtCorp_PowerSupply_Module,
		GameScenes.SceneId.AltCorp_LifeSupportModule,
		GameScenes.SceneId.AltCorp_Cargo_Module,
		GameScenes.SceneId.AltCorp_CorridorVertical,
		GameScenes.SceneId.AltCorp_Command_Module,
		GameScenes.SceneId.AltCorp_Corridor45TurnRightModule,
		GameScenes.SceneId.AltCorp_StartingModule,
		GameScenes.SceneId.AltCorp_AirLock,
		GameScenes.SceneId.Generic_Debris_JuncRoom001,
		GameScenes.SceneId.Generic_Debris_JuncRoom002,
		GameScenes.SceneId.Generic_Debris_Corridor001,
		GameScenes.SceneId.Generic_Debris_Corridor002,
		GameScenes.SceneId.AltCorp_DockableContainer
	};

	public static double SpawnPointInviteTimer = 300.0;

	public static string LoadPersistenceFromFile;

	public Dictionary<long, SpaceObjectVessel>.ValueCollection AllVessels => _vessels.Values;

	public Dictionary<long, Player>.ValueCollection AllPlayers => _players.Values;

	public static Server Instance => _serverInstance;

	public TimeSpan RunTime => DateTime.UtcNow - _serverStartTime;

	public double TickMilliseconds => _tickMilliseconds;

	public static double SolarSystemTime => Instance.SolarSystem.CurrentTime;

	public bool DoesObjectExist(long guid)
	{
		return _objects.ContainsKey(guid);
	}

	public SpaceObject GetObject(long guid)
	{
		if (_objects.TryGetValue(guid, out var o))
		{
			return o;
		}
		return null;
	}

	public SpaceObjectTransferable GetTransferable(long guid)
	{
		if (_objects.ContainsKey(guid) && _objects[guid] is SpaceObjectTransferable)
		{
			return _objects[guid] as SpaceObjectTransferable;
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
		if (_vessels.TryGetValue(guid, out var vessel))
		{
			return vessel;
		}
		return null;
	}

	public Item GetItem(long guid)
	{
		if (_objects.ContainsKey(guid) && _objects[guid] is DynamicObject obj)
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
		_players[player.GUID] = player;
		_objects[player.FakeGuid] = player;
	}

	public void Add(SpaceObjectVessel vessel)
	{
		_vessels[vessel.GUID] = vessel;
		_objects[vessel.GUID] = vessel;
	}

	public void Add(DynamicObject dobj)
	{
		_objects[dobj.GUID] = dobj;
		if (dobj.Item != null && dobj.Item is IUpdateable)
		{
			_updateableDynamicObjects[dobj.GUID] = dobj;
		}
	}

	public void Add(Corpse corpse)
	{
		_objects[corpse.GUID] = corpse;
	}

	public void Remove(Player player)
	{
		if (_mainLoopStarted)
		{
			_playersToRemove.Add(player);
			return;
		}
		_players.Remove(player.GUID);
		_objects.Remove(player.FakeGuid);
	}

	public void Remove(SpaceObjectVessel vessel)
	{
		_vessels.Remove(vessel.GUID);
		_objects.Remove(vessel.GUID);
	}

	public void Remove(DynamicObject dobj)
	{
		_objects.Remove(dobj.GUID);
		_updateableDynamicObjects.Remove(dobj.GUID);
	}

	public void Remove(Corpse corpse)
	{
		if (_objects.ContainsKey(corpse.GUID))
		{
			_objects.Remove(corpse.GUID);
		}
	}

	public Server()
	{
		MainThreadId = Thread.CurrentThread.ManagedThreadId;
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
						Debug.Error("-configdir was not supplied a path.");
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
					CleanStart = true;
				break;
				case "-load":
					i++;

					if (i >= args.Length)
					{
						Debug.Error("-load was not supplied a path.");
						break;
					}

					LoadPersistenceFromFile = args[i];
				break;
				case "-randomships":
					i++;

					if (i >= args.Length)
					{
						Debug.Error("-randomships was not supplied a number.");
						break;
					}

					randomShips = args[++i];
				break;
				case "-gport":
					i++;

					if (i >= args.Length)
					{
						Debug.Error("-gport was not supplied a port.");
						break;
					}

					gPort = args[++i];
				break;
				case "-sport":
					i++;

					if (i >= args.Length)
					{
						Debug.Error("-sport was not supplied a port.");
						break;
					}

					sPort = args[++i];
				break;
			}
		}

		// Set properties.
		Properties = new Properties(Server.ConfigDir + "GameServer.ini");
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
		Properties.GetProperty("http_key", ref MsConnection.HttpKey);
		Properties.GetProperty("main_server_ip", ref MsConnection.IpAddress);
		Properties.GetProperty("main_server_port", ref MsConnection.Port);
		string admins = "";
		Properties.GetProperty("server_admins", ref admins);
		string[] adminsArray = admins.Split(',');
		_serverAdmins = adminsArray.Where((string m) => m != "").ToList();
		Properties.GetProperty("movement_send_interval", ref MovementMessageSendInterval);
		if (MovementMessageSendInterval <= 1.4012984643248171E-45)
		{
			MovementMessageSendInterval = 0.1;
		}
		Properties.GetProperty("solar_system_time", ref _solarSystemStartTime);
		Properties.GetProperty("save_interval", ref PersistenceSaveInterval);
		Properties.GetProperty("max_players", ref MaxPlayers);
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

	public void LoginPlayer(long guid, string playerId, CharacterData characterData)
	{
		if (!WorldInitialized)
		{
			InitializeWorld();
		}

		foreach (Player pl in _players.Values)
		{
			Debug.Log(pl.Name, pl.GUID);
		}

		Player player = GetPlayer(guid);
		if (player != null)
		{
			NetworkController.ConnectPlayer(player);
		}
		else
		{
			Debug.Log("Creating new player for client with guid:", guid);

			player = new Player(guid, Vector3D.Zero, QuaternionD.Identity, characterData.Name, playerId, characterData.Gender, characterData.HeadType, characterData.HairType);
			Add(player);
			NetworkController.ConnectPlayer(player);
		}

		if (_serverAdmins.Contains(player.PlayerId) || _serverAdmins.Contains("*"))
		{
			player.IsAdmin = true;
		}
	}

	private void ResetSpawnPointsForPlayer(Player pl, Ship skipShip)
	{
		if (!pl.PlayerId.IsNullOrEmpty() && _spawnPointInvites.ContainsKey(pl.PlayerId))
		{
			ClearSpawnPointInvitation(pl.PlayerId);
		}
		foreach (SpaceObjectVessel ves in AllVessels)
		{
			if (ves is Ship ship && ship != skipShip)
			{
				ship.ResetSpawnPointsForPlayer(pl, sendStatsMessage: true);
			}
		}
	}

	private bool AddPlayerToShip(Player pl, SpawnSetupType setupType, long shipId)
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
		default:
			ResetSpawnPointsForPlayer(pl, null);
			foundShip = SpawnManager.SpawnStartingSetup(setupType.ToString());
			if (foundShip != null)
			{
				foundSpawnPoint = SpawnManager.SetStartingSetupSpawnPoints(foundShip, pl);
			}
			if (foundShip == null || foundSpawnPoint == null)
			{
				Debug.Error("FAILED TO SPAWN STARTING SETUP", pl.GUID, foundShip?.GUID ?? -1);
			}
			break;
		case SpawnSetupType.None:
			if (shipId > 0 && _spawnPointInvites.ContainsKey(pl.PlayerId) && _spawnPointInvites[pl.PlayerId].SpawnPoint.Ship.GUID == shipId)
			{
				foundShip = GetVessel(shipId) as Ship;
				foundSpawnPoint = foundShip != null ? _spawnPointInvites[pl.PlayerId].SpawnPoint : null;
				if (foundSpawnPoint != null)
				{
					ResetSpawnPointsForPlayer(pl, null);
				}
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
			foundSpawnPoint.AuthorizePlayerToSpawnPoint(pl, sendMessage: true);
			pl.Parent = foundSpawnPoint.Ship;
			pl.SetSpawnPoint(foundSpawnPoint);
			pl.SubscribeTo(foundSpawnPoint.Ship.MainVessel);
			return true;
		}
		return false;
	}

	public void RemovePlayer(Player player)
	{
		if (player.Parent != null)
		{
			if (player.Parent.ObjectType == SpaceObjectType.PlayerPivot)
			{
				(player.Parent as Pivot).Destroy();
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

	// TODO: Is this redundant? Invites should probably come from nakama instead.
	public List<SpawnPointDetails> GetAvailableSpawnPoints(Player pl)
	{
		List<SpawnPointDetails> retVal = new List<SpawnPointDetails>();
		if (pl.PlayerId != null && _spawnPointInvites.TryGetValue(pl.PlayerId, out var invite))
		{
			ShipSpawnPoint sp = invite.SpawnPoint;
			SpawnPointDetails spd = new SpawnPointDetails
			{
				Name = sp.Ship.FullName,
				IsPartOfCrew = false,
				SpawnPointParentID = sp.Ship.GUID,
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

	private void LatencyTestListener(NetworkData data)
	{
		NetworkController.SendToGameClient(data.Sender, data);
	}

	private async void Start()
	{
		EventSystem.AddListener(typeof(PlayerSpawnRequest), PlayerSpawnRequestListener);
		EventSystem.AddListener(typeof(PlayerRespawnRequest), PlayerRespawnRequestListener);
		EventSystem.AddListener(typeof(SpawnObjectsRequest), SpawnObjectsRequestListener);
		EventSystem.AddListener(typeof(SubscribeToObjectsRequest), SubscribeToSpaceObjectListener);
		EventSystem.AddListener(typeof(UnsubscribeFromObjectsRequest), UnsubscribeFromSpaceObjectListener);
		EventSystem.AddListener(typeof(TextChatMessage), TextChatMessageListener);
		EventSystem.AddListener(typeof(TransferResourceMessage), TransferResourcesMessageListener);
		EventSystem.AddListener(typeof(FabricateItemMessage), FabricateItemMessageListener);
		EventSystem.AddListener(typeof(CancelFabricationMessage), CancelFabricationMessageListener);
		EventSystem.AddListener(typeof(PlayersOnServerRequest), PlayersOnServerRequestListener);
		EventSystem.AddListener(typeof(AvailableSpawnPointsRequest), AvailableSpawnPointsRequestListener);
		EventSystem.AddListener(typeof(RepairItemMessage), RepairMessageListener);
		EventSystem.AddListener(typeof(RepairVesselMessage), RepairMessageListener);
		EventSystem.AddListener(typeof(HurtPlayerMessage), HurtPlayerMessageListener);
		EventSystem.AddListener(typeof(ConsoleMessage), ConsoleMessageListener);
		EventSystem.AddListener(typeof(ExplosionMessage), ExplosionMessageListener);
		EventSystem.AddListener(typeof(LatencyTestMessage), LatencyTestListener);
		EventSystem.AddListener(typeof(ServerShutDownMessage), ServerShutDownMessageListener);
		EventSystem.AddListener(typeof(NameTagMessage), NameTagMessageListener);

		try
		{
			RegisterServerResponse response = await MsConnection.Send<RegisterServerResponse>(new RegisterServerRequest
			{
				AuthToken = Properties.GetProperty<string>("auth_key"),
				Location = RegionInfo.CurrentRegion.EnglishName,
				GamePort = GamePort,
				StatusPort = StatusPort,
				Hash = CombinedHash
			});

			NetworkController.ServerId = response.ServerId;
			Console.Title = " (id: " + (NetworkController.ServerId == null ? "Not yet assigned" : string.Concat(NetworkController.ServerId)) + ")";
			Debug.UnformattedMessage("r\n\tServer ID: " + NetworkController.ServerId + "\r\n");
			_checkInPassed = true;
			_adminIpAddressRanges = response.AdminIpAddressRanges;
		}
		catch (MainServerException ex)
		{
			IsRunning = false;
			Debug.Log(ex);
		}
		catch (Exception ex)
		{
			Debug.Exception(ex);
			IsRunning = false;
		}
	}

	public void TransferResourcesMessageListener(NetworkData data)
	{
		TransferResourceMessage trm = data as TransferResourceMessage;
		SpaceObjectVessel fromVessel = Instance.GetVessel(trm.FromVesselGuid);
		SpaceObjectVessel toVessel = Instance.GetVessel(trm.ToVesselGuid);
		if (trm.FromLocationType == ResourceLocationType.ResourcesTransferPoint)
		{
			ICargo toCargo2 = null;
			if (trm.ToLocationType == ResourceLocationType.CargoBay)
			{
				toCargo2 = toVessel.CargoBay;
			}
			else if (trm.ToLocationType == ResourceLocationType.ResourceTank)
			{
				toCargo2 = toVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(trm.ToVesselGuid, trm.ToInSceneID));
			}
			DynamicObject dobj2 = fromVessel.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item != null && m.Item.AttachPointID != null && m.Item.AttachPointID.InSceneID == trm.FromInSceneID);
			if (dobj2 != null && dobj2.Item != null && dobj2.Item is ICargo item && toCargo2 != null)
			{
				if (trm.ToLocationType != 0)
				{
					TransferResources(item, trm.FromCompartmentID, toCargo2, trm.ToCompartmentID, trm.ResourceType, trm.Quantity);
				}
				else
				{
					VentResources(item, trm.FromCompartmentID, trm.ResourceType, trm.Quantity);
				}
			}
			return;
		}
		if (trm.ToLocationType == ResourceLocationType.ResourcesTransferPoint)
		{
			ICargo fromCargo2 = null;
			if (trm.FromLocationType == ResourceLocationType.CargoBay)
			{
				fromCargo2 = fromVessel.CargoBay;
			}
			else if (trm.FromLocationType == ResourceLocationType.ResourceTank)
			{
				fromCargo2 = fromVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(trm.FromVesselGuid, trm.FromInSceneID));
			}
			DynamicObject dobj = toVessel.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item != null && m.Item.AttachPointID != null && m.Item.AttachPointID.InSceneID == trm.ToInSceneID);
			if (dobj != null && dobj.Item != null && dobj.Item is ICargo item && fromCargo2 != null)
			{
				if (trm.ToLocationType != 0)
				{
					TransferResources(fromCargo2, trm.FromCompartmentID, item, trm.ToCompartmentID, trm.ResourceType, trm.Quantity);
				}
				else
				{
					VentResources(item, trm.FromCompartmentID, trm.ResourceType, trm.Quantity);
				}
			}
			return;
		}
		ICargo fromCargo = null;
		if (trm.FromLocationType == ResourceLocationType.CargoBay)
		{
			fromCargo = fromVessel.CargoBay;
		}
		else if (trm.FromLocationType == ResourceLocationType.Refinery)
		{
			fromCargo = fromVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(trm.FromVesselGuid, trm.FromInSceneID)) as SubSystemRefinery;
		}
		else if (trm.FromLocationType == ResourceLocationType.Fabricator)
		{
			fromCargo = fromVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(trm.FromVesselGuid, trm.FromInSceneID)) as SubSystemFabricator;
		}
		else if (trm.FromLocationType == ResourceLocationType.ResourceTank)
		{
			fromCargo = fromVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(trm.FromVesselGuid, trm.FromInSceneID));
		}
		ICargo toCargo = null;
		if (trm.ToLocationType == ResourceLocationType.CargoBay)
		{
			toCargo = toVessel.CargoBay;
		}
		else if (trm.ToLocationType == ResourceLocationType.Refinery)
		{
			toCargo = toVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(trm.ToVesselGuid, trm.ToInSceneID)) as SubSystemRefinery;
		}
		else if (trm.ToLocationType == ResourceLocationType.Fabricator)
		{
			toCargo = toVessel.MainDistributionManager.GetSubSystem(new VesselObjectID(trm.ToVesselGuid, trm.ToInSceneID)) as SubSystemFabricator;
		}
		else if (trm.ToLocationType == ResourceLocationType.ResourceTank)
		{
			toCargo = toVessel.MainDistributionManager.GetResourceContainer(new VesselObjectID(trm.ToVesselGuid, trm.ToInSceneID));
		}
		else if (trm.ToLocationType == ResourceLocationType.None)
		{
			VentResources(fromCargo, trm.FromCompartmentID, trm.ResourceType, trm.Quantity);
			return;
		}
		if (fromCargo != null && toCargo != null)
		{
			TransferResources(fromCargo, trm.FromCompartmentID, toCargo, trm.ToCompartmentID, trm.ResourceType, trm.Quantity);
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
			fromCargo.ChangeQuantityBy(fromCompartmentId, resourceType, 0f - qty);
			toCargo.ChangeQuantityBy(toCompartmentId, resourceType, qty);
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
			fromCargo.ChangeQuantityBy(fromCompartmentId, resourceType, 0f - qty);
		}
	}

	private void FabricateItemMessageListener(NetworkData data)
	{
		FabricateItemMessage fim = data as FabricateItemMessage;
		SpaceObjectVessel vessel = GetVessel(fim.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(fim.ID) is SubSystemFabricator fabricator)
		{
			fabricator.Fabricate(fim.ItemType);
		}
	}

	private void CancelFabricationMessageListener(NetworkData data)
	{
		CancelFabricationMessage cfm = data as CancelFabricationMessage;
		SpaceObjectVessel vessel = GetVessel(cfm.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(cfm.ID) is SubSystemFabricator fabricator)
		{
			fabricator.Cancel(cfm.CurrentItemOnly);
		}
	}

	private void RepairMessageListener(NetworkData data)
	{
		Player pl = GetPlayer(data.Sender);
		if (pl == null)
		{
			return;
		}
		Item rTool = pl.PlayerInventory.HandsSlot.Item;
		if (rTool is not RepairTool rt)
		{
			return;
		}
		if (data is RepairVesselMessage rvm)
		{
			rt.RepairVessel(rvm.ID);
		}
		else if (data is RepairItemMessage rim)
		{
			if (rim.GUID > 0)
			{
				rt.RepairItem(rim.GUID);
			}
			else
			{
				rt.ConsumeFuel(rt.RepairAmount * rt.FuelConsumption);
			}
		}
	}

	private void HurtPlayerMessageListener(NetworkData data)
	{
		Player pl = GetPlayer(data.Sender);
		HurtPlayerMessage hpm = data as HurtPlayerMessage;
		pl.Stats.TakeDamage(hpm.Duration, hpm.Damage);
	}

	private void ConsoleMessageListener(NetworkData data)
	{
		Player player = GetPlayer(data.Sender);
		ConsoleMessage cm = data as ConsoleMessage;
		if (player.IsAdmin)
		{
			ProcessConsoleCommand(cm.Text, player);
		}
	}

	private void TextChatMessageListener(NetworkData data)
	{
		TextChatMessage tcm = data as TextChatMessage;
		Player player = _players[tcm.Sender];
		tcm.GUID = player.FakeGuid;
		tcm.Name = player.Name;
		if (tcm.MessageText.Length > 250)
		{
			tcm.MessageText = tcm.MessageText.Substring(0, 250);
		}
		if (tcm.Local)
		{
			Vector3D playerGlobalPos = player.Parent.Position + player.Position;
			{
				foreach (Player pl in _players.Values)
				{
					if ((pl.Parent.Position + pl.Position - playerGlobalPos).SqrMagnitude < 1000000.0 && pl != player)
					{
						NetworkController.SendToGameClient(pl.GUID, tcm);
					}
				}
				return;
			}
		}
		NetworkController.SendToAllClients(tcm, tcm.Sender);
	}

	private void ProcessConsoleCommand(string cmd, Player player)
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
				if (handsItem != null && handsItem is ICargo cargo)
				{
					foreach (CargoCompartmentData ccd in cargo.Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
					{
						using List<CargoResourceData>.Enumerator enumerator2 = ccd.Resources.GetEnumerator();
						if (enumerator2.MoveNext())
						{
							CargoResourceData r = enumerator2.Current;
							cargo.ChangeQuantityBy(ccd.ID, r.ResourceType, ccd.Capacity);
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
								cargo1.ChangeQuantityBy(ccd2.ID, enumerator5.Current.ResourceType, ccd2.Capacity);
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
							rc.ChangeQuantityBy(ccd3.ID, crd.ResourceType, ccd3.Capacity);
						}
					}
				}
				{
					foreach (GeneratorCapacitor cap in from m in vessel.MainDistributionManager.GetGenerators()
						where m is GeneratorCapacitor
						select m)
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
					Corpse corpse = new Corpse(player);
					corpse.LocalPosition = player.LocalPosition + player.LocalRotation * Vector3D.Forward;
					NetworkController.SendToClientsSubscribedTo(new SpawnObjectsResponse
					{
						Data = new List<SpawnObjectResponseData>
						{
							new SpawnCorpseResponseData
							{
								GUID = corpse.GUID,
								Details = corpse.GetDetails()
							}
						}
					}, -1L, player.Parent);
					return;
				}
				int tier = 1;
				if (parts.Length == 3 && !int.TryParse(parts[2], out tier))
				{
					tier = 1;
				}
				List<InventorySlot> inventorySlots = new List<InventorySlot>();
				inventorySlots.Add(player.PlayerInventory.OutfitSlot);
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
						DynamicObject.SpawnDynamicObject(dod.ItemType, (dod.DefaultAuxData as GenericItemData).SubType, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot, refill: true);
					}
					else if (dod.ItemType == ItemType.MachineryPart)
					{
						InventorySlot slot2 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, (dod.DefaultAuxData as MachineryPartData).PartType, parent, -1, spawnItemPosition, null, null, tier, slot2, refill: true);
					}
					else
					{
						InventorySlot slot3 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(dod.ItemType));
						DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot3, refill: true);
					}
					return;
				}
				foreach (ItemType v in Enum.GetValues(typeof(ItemType)))
				{
					if (v.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot4 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(v));
						DynamicObject.SpawnDynamicObject(v, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot4, refill: true);
						return;
					}
				}
				foreach (GenericItemSubType v4 in Enum.GetValues(typeof(GenericItemSubType)))
				{
					if (v4.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot5 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.GenericItem));
						DynamicObject.SpawnDynamicObject(ItemType.GenericItem, v4, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot5, refill: true);
						return;
					}
				}
				foreach (MachineryPartType v5 in Enum.GetValues(typeof(MachineryPartType)))
				{
					if (v5.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot6 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						DynamicObject.SpawnDynamicObject(ItemType.MachineryPart, GenericItemSubType.None, v5, parent, -1, spawnItemPosition, null, null, tier, slot6, refill: true);
						return;
					}
				}
				{
					foreach (GameScenes.SceneId v6 in Enum.GetValues(typeof(GameScenes.SceneId)))
					{
						if (v6.ToString().ToLower().Contains(parts[1]))
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
								Asteroid asteroid = Asteroid.CreateNewAsteroid(v6, "", -1L, new List<long> { parent is SpaceObjectVessel vessel ? vessel.MainVessel.GUID : parent.GUID }, null, offset * 10.0, null, null, tag, checkPosition: false);
								asteroid.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()).Normalized * 6.0;
							}
							else
							{
								Ship ship2 = Ship.CreateNewShip(v6, "", -1L, new List<long> { parent is SpaceObjectVessel vessel ? vessel.MainVessel.GUID : parent.GUID }, null, offset, null, null, tag, checkPosition: false);
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
				Ship ship3 = Ship.CreateNewShip(GameScenes.SceneId.AltCorp_CorridorModule, "", -1L, new List<long> { parent.GUID }, null, offset3, null, null, checkPosition: false);
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
				long guid = player.Parent.GUID;
				if (parent is SpaceObjectVessel vessel7)
				{
					offset2 = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * (offset2 - vessel7.VesselData.CollidersCenterOffset.ToVector3D());
					direction = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * direction;
					guid = vessel7.MainVessel.GUID;
				}
				Ship ship = Ship.CreateNewShip(GameScenes.SceneId.AltCorp_DockableContainer, "PHTORP MK4", -1L, new List<long> { guid }, null, offset2, null, null, checkPosition: false);
				ship.Forward = direction;
				Vector3D thrust = direction * 80.0;
				ship.Orbit.InitFromStateVectors(ship.Orbit.Parent, ship.Orbit.Position, ship.Orbit.Velocity + thrust, Instance.SolarSystem.CurrentTime, areValuesRelative: false);
				ship.Health = 5f;
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
						cbd = StaticData.SolarSystem.CelestialBodies.FirstOrDefault((CelestialBodyData m) => m.Name.ToLower().Contains(parts[1].ToLower()));
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
						foreach (Item item in from m in _objects.Values
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
				NetworkController.SendToGameClient(player.GUID, new ConsoleMessage
				{
					Text = msg
				});
				return;
			}
			if (parts[0] == "station" && parts.Length == 2)
			{
				StationBlueprint.AssembleStation(parts[1], "JsonStation", "JsonStation", null, parent.GUID);
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
				NetworkController.SendToGameClient(player.GUID, new ConsoleMessage
				{
					Text = "God mode: " + (player.Stats.GodMode ? "ON" : "OFF")
				});
				return;
			}
			if (parts[0] == "teleport" && parts.Length == 2)
			{
				ArtificialBody target = null;
				Player p = _players.Values.FirstOrDefault((Player m) => m.PlayerId == parts[1] || m.Name.ToLower() == parts[1].ToLower());
				if (p != null && p.Parent is ArtificialBody body)
				{
					target = body is not SpaceObjectVessel vessel ? body : vessel.MainVessel;
				}
				else
				{
					SpaceObjectVessel v2 = (from m in SolarSystem.GetArtificialBodies()
						where m is SpaceObjectVessel
						select m as SpaceObjectVessel).FirstOrDefault((SpaceObjectVessel m) => m.FullName.Replace(' ', '_').ToLower().Contains(parts[1].ToLower()));
					if (v2 != null)
					{
						target = v2.MainVessel;
					}
				}
				ArtificialBody myAb = parent is SpaceObjectVessel spaceObjectVessel ? spaceObjectVessel.MainVessel : parent as ArtificialBody;
				if (target != null && target != myAb)
				{
					myAb.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
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
				SpawnManager.RespawnBlueprintRule(parts[1]);
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
					player.PlayerInventory.HandsSlot.Item.DynamicObj.SendStatsToClient();
				}
				else
				{
					if (parent is not SpaceObjectVessel vessel4)
					{
						return;
					}

					vessel4.Health = vessel4.MaxHealth;
					foreach (VesselRepairPoint vrp in vessel4.RepairPoints)
					{
						vrp.Health = vrp.MaxHealth;
					}
					vessel4.Health = health;
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
						if (sr2.SpawnedVessels.FirstOrDefault((SpaceObjectVessel m) => m.GUID == ab.GUID) != null)
						{
							found = true;
							break;
						}
					}
					if (!found)
					{
						vessel.MarkForDestruction = true;
						SpawnManager.SpawnedVessels.Remove(vessel.GUID);
					}
				}
				return;
			}
			if (parts[0] == "unmatchall")
			{
				ArtificialBody[] artificialBodies3 = SolarSystem.GetArtificialBodies();
				foreach (ArtificialBody ab2 in artificialBodies3)
				{
					ab2.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
				}
				return;
			}
			if (parts[0] == "resetblueprints")
			{
				foreach (Player pl in _players.Values)
				{
					pl.Blueprints = ObjectCopier.DeepCopy(StaticData.DefaultBlueprints);
					NetworkController.SendToGameClient(pl.GUID, new UpdateBlueprintsMessage
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
					vessel3.MainVessel.DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
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
				NetworkController.SendToGameClient(player.GUID, new ConsoleMessage
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
				List<SpaceObjectVessel> list = new List<SpaceObjectVessel>();
				list.Add(vessel.MainVessel);
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
							DynamicObject.SpawnDynamicObject(it, gt, mt, vessel2, ap.InSceneID, null, null, null, MathHelper.RandomRange(1, 5), null, refill: true);
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
			if (sh.Orbit.Parent != null && sh.Orbit.Parent.CelestialBody != null)
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

	private void PlayerSpawnRequestListener(NetworkData data)
	{
		PlayerSpawnRequest p = data as PlayerSpawnRequest;
		bool spawnSuccsessful = false;
		Player pl = NetworkController.GetPlayer(data.Sender);
		if (pl == null)
		{
			Debug.Error("Player spawn request error, player is null", p.Sender);
			return;
		}
		pl.MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		spawnSuccsessful = !pl.IsAlive ? AddPlayerToShip(pl, p.SpawnSetupType, p.SpawnPointParentId) : pl.Parent != null && pl.Parent is ArtificialBody;
		PlayerSpawnResponse spawnResponse = new PlayerSpawnResponse();
		if (!spawnSuccsessful)
		{
			spawnResponse.Response = ResponseResult.Error;
		}
		else
		{
			if (pl.Parent is Pivot pivot && pivot.StabilizeToTargetObj != null)
			{
				pivot.DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
			}
			if (pl.Parent != null && !pl.IsSubscribedTo(pl.Parent.GUID))
			{
				pl.SubscribeTo(pl.Parent);
			}
			ArtificialBody parentObj = pl.Parent as ArtificialBody;
			spawnResponse.ParentID = parentObj.GUID;
			spawnResponse.ParentType = parentObj.ObjectType;
			spawnResponse.MainVesselID = parentObj.GUID;

			SpaceObjectVessel mainVessel = parentObj as SpaceObjectVessel;
			if (mainVessel != null && mainVessel.IsDocked)
			{
				mainVessel = mainVessel.DockedToMainVessel;
				spawnResponse.MainVesselID = mainVessel.GUID;
			}

			ArtificialBody mainAb = mainVessel != null ? mainVessel : parentObj;
			spawnResponse.ParentTransform = new ObjectTransform
			{
				GUID = mainAb.GUID,
				Type = mainAb.ObjectType,
				Forward = mainAb.Forward.ToFloatArray(),
				Up = mainAb.Up.ToFloatArray()
			};

			if (mainVessel != null && mainVessel is Ship mainShip)
			{
				spawnResponse.DockedVessels = mainShip.GetDockedVesselsData();
				spawnResponse.VesselData = mainShip.VesselData;
				spawnResponse.VesselObjects = mainShip.GetVesselObjects();
			}
			else if (mainVessel != null && mainVessel is Asteroid asteroid)
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

			if (pl.CurrentSpawnPoint != null && ((pl.CurrentSpawnPoint.IsPlayerInSpawnPoint && pl.CurrentSpawnPoint.Ship == pl.Parent) || (pl.CurrentSpawnPoint.Type == SpawnPointType.SimpleSpawn && pl.CurrentSpawnPoint.Executor == null && !pl.IsAlive)))
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

			List<DynamicObjectDetails> playerObjects = new List<DynamicObjectDetails>();
			foreach (DynamicObject dobj in pl.DynamicObjects.Values)
			{
				playerObjects.Add(dobj.GetDetails());
			}
			spawnResponse.DynamicObjects = playerObjects;

			spawnResponse.Health = pl.Health;
			spawnResponse.IsAdmin = pl.IsAdmin;
			if (pl.AuthorizedSpawnPoint != null)
			{
				spawnResponse.HomeGUID = pl.AuthorizedSpawnPoint.Ship.MainVessel.GUID;
			}
			if (ServerRestartTimeSec > 0.0)
			{
				spawnResponse.TimeUntilServerRestart = (RestartTime - DateTime.UtcNow).TotalSeconds;
			}
			if (pl.Parent is ArtificialBody body)
			{
				HashSet<GameScenes.SceneId> sceneIDs = new HashSet<GameScenes.SceneId>();
				List<SpaceObjectVessel> vessels = (from m in SolarSystem.GetArtificialBodieslsInRange(body, 10000.0)
					where m is SpaceObjectVessel
					select m as SpaceObjectVessel).ToList();
				if (body is SpaceObjectVessel vessel)
				{
					vessels.Add(vessel);
				}
				foreach (SpaceObjectVessel ves in vessels)
				{
					sceneIDs.Add(ves.SceneID);
					foreach (SpaceObjectVessel dves in ves.AllDockedVessels)
					{
						sceneIDs.Add(dves.SceneID);
					}
				}
				spawnResponse.Scenes = sceneIDs.ToList();
			}
			spawnResponse.Quests = pl.Quests.Select((Quest m) => m.GetDetails()).ToList();
			spawnResponse.Blueprints = pl.Blueprints;
			spawnResponse.NavMapDetails = pl.NavMapDetails;
		}
		NetworkController.SendToGameClient(p.Sender, spawnResponse);
		SolarSystem.SendMovementMessageToPlayer(pl);
	}

	private void PlayerRespawnRequestListener(NetworkData data)
	{
	}

	private void SpawnObjectsRequestListener(NetworkData data)
	{
		SpawnObjectsRequest req = (SpawnObjectsRequest)data;
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		Player pl = GetPlayer(data.Sender);
		if (pl == null)
		{
			return;
		}
		foreach (long guid in req.GUIDs)
		{
			SpaceObject obj = GetObject(guid);
			if (obj != null && (obj is not SpaceObjectVessel vessel || vessel.IsMainVessel))
			{
				res.Data.Add(obj.GetSpawnResponseData(pl));
			}
		}
		NetworkController.SendToGameClient(req.Sender, res);
	}

	private void UpdateDynamicObjectsRespawnTimers(double deltaTime)
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
			if (dos.Parent is SpaceObjectVessel && dos.Parent.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item != null && m.Item.AttachPointID != null && dos.ApDetails != null && m.Item.AttachPointID.InSceneID == dos.ApDetails.InSceneID) != null)
			{
				dos.Timer = dos.RespawnTime;
				continue;
			}
			DynamicObjectsRespawnList.Remove(dos);
			if (_vessels.ContainsKey(dos.Parent.GUID))
			{
				DynamicObject dobj = new DynamicObject(dos.Data, dos.Parent, -1L);
				if (dos.Data.AttachPointInSceneId > 0 && dobj.Item != null)
				{
					dobj.Item.SetAttachPoint(dos.ApDetails);
				}
				dobj.APDetails = dos.ApDetails;
				dobj.RespawnTime = dos.Data.SpawnSettings.Length != 0 ? dos.Data.SpawnSettings[0].RespawnTime : -1f;
				if (dobj.Item != null && dobj.Item != null && dos.MaxHealth >= 0f && dos.MinHealth >= 0f)
				{
					IDamageable idmg = dobj.Item;
					idmg.Health = (int)(idmg.MaxHealth * MathHelper.Clamp(MathHelper.RandomRange(dos.MinHealth, dos.MaxHealth), 0f, 1f));
				}
				SpawnObjectsResponse res = new SpawnObjectsResponse();
				res.Data.Add(dobj.GetSpawnResponseData(null));
				NetworkController.SendToClientsSubscribedTo(res, -1L, dos.Parent);
			}
		}
		toRemove.Clear();
	}

	private void UpdateData(double deltaTime)
	{
		EventSystem.Instance.InvokeQueuedData();
		SolarSystem.UpdateTime(deltaTime);
		SolarSystem.UpdatePositions();
		PhysicsController.Update();
		UpdateDynamicObjectsRespawnTimers(deltaTime);
		UpdateObjectTimers(deltaTime);
		UpdatePlayerInvitationTimers(deltaTime);
		_movementMessageTimer += deltaTime;
		if (_movementMessageTimer >= MovementMessageSendInterval || ForceMovementMessageSend)
		{
			_movementMessageTimer = 0.0;
			ForceMovementMessageSend = false;
			SolarSystem.SendMovementMessage();
		}
		if (VesselsDataUpdate.Count > 0)
		{
			NetworkController.SendToAllClients(new UpdateVesselDataMessage
			{
				VesselsDataUpdate = VesselsDataUpdate.Values.ToList()
			}, -1L);
			VesselsDataUpdate = new ConcurrentDictionary<long, VesselDataUpdate>();
		}
	}

	public void RemoveWorldObjects()
	{
		Debug.Info("REMOVING ALL WORLD OBJECTS");
		try
		{
			long[] array = _players.Keys.ToArray();
			foreach (long guid in array)
			{
				NetworkController.DisconnectClient(guid);
			}
			NetworkController.DisconnectAllClients();
		}
		catch (Exception)
		{
		}
		_players.Clear();
		_objects.Clear();
		ArtificialBody[] artificialBodies = Instance.SolarSystem.GetArtificialBodies();
		foreach (ArtificialBody ab in artificialBodies)
		{
			if (ab is Ship ship)
			{
				ship.Destroy();
			}
			else if (ab is Asteroid asteroid)
			{
				asteroid.Destroy();
			}
			else
			{
				Instance.SolarSystem.RemoveArtificialBody(ab);
			}
		}
		_vessels.Clear();
		WorldInitialized = false;
	}

	public void DestroyArtificialBody(ArtificialBody ab, bool destroyChildren = true, bool vesselExploded = false)
	{
		if (ab == null)
		{
			return;
		}
		if (ab is SpaceObjectVessel ves)
		{
			if (destroyChildren && ves.AllDockedVessels.Count > 0)
			{
				foreach (SpaceObjectVessel child in new List<SpaceObjectVessel>(ves.AllDockedVessels))
				{
					DestroyArtificialBody(child, destroyChildren: false, vesselExploded);
				}
			}
			foreach (Player pl in new List<Player>(ves.VesselCrew))
			{
				try
				{
					pl.KillYourself(HurtType.Shipwreck, createCorpse: false);
				}
				catch (Exception ex)
				{
					Debug.Exception(ex);
				}
			}
			if (vesselExploded)
			{
				ves.DamageVesselsInExplosionRadius();
				NetworkController.SendToClientsSubscribedTo(new DestroyVesselMessage
				{
					GUID = ves.GUID
				}, -1L, ves);
			}
			ves.Destroy();
		}
		else
		{
			ab.Destroy();
		}
	}

	public void UpdateObjectTimers(double deltaTime)
	{
		HashSet<SpaceObjectVessel> destroyVessels = null;
		foreach (SpaceObjectVessel vessel in AllVessels)
		{
			vessel.UpdateTimers(deltaTime);
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
				destroyVessels.Add(vessel.DockedToMainVessel != null ? vessel.DockedToMainVessel : vessel);
			}
			else
			{
				vessel.UndockAll();
			}
		}
		if (destroyVessels != null)
		{
			foreach (SpaceObjectVessel dv in destroyVessels)
			{
				DestroyArtificialBody(dv, destroyChildren: true, vesselExploded: true);
			}
		}
		foreach (DynamicObject dobj in _updateableDynamicObjects.Values)
		{
			(dobj.Item as IUpdateable).Update(deltaTime);
		}
		foreach (Player pl in AllPlayers)
		{
			pl.UpdateTimers(deltaTime);
		}
		foreach (DebrisField df in DebrisFields)
		{
			df.SpawnFragments();
		}
	}

	private void PrintObjectsDebug(double time)
	{
		Debug.Info("Server stats, objects", _objects.Count, "players", _players.Count, "vessels", _vessels.Count, "artificial bodies", SolarSystem.ArtificialBodiesCount);
	}

	public void MainLoop()
	{
		SolarSystem.InitializeData();
		InitializeDebrisFields();
		if (CleanStart || PersistenceSaveInterval < 0.0 || (CleanStart = !Persistence.Load(LoadPersistenceFromFile)))
		{
			if (_solarSystemStartTime < 0.0)
			{
				SolarSystem.CalculatePositionsAfterTime(MathHelper.RandomRange(86400.0, 5256000.0));
			}
			else
			{
				SolarSystem.CalculatePositionsAfterTime(_solarSystemStartTime);
			}
			InitializeWorld();
		}
		else
		{
			WorldInitialized = true;
		}
		Start();
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

		Debug.Log("Starting main game loop.");
		Task.Run(StartMainLoopWatcher);
		while (IsRunning)
		{
			try
			{
				DateTime currentTime = DateTime.UtcNow;
				TimeSpan span = currentTime - _lastTime;

				// Actual main loop. Functionality go here.
				if (span.TotalMilliseconds >= _tickMilliseconds)
				{
					NetworkController.Tick();

					AddRemovePlayers();
					if (_printDebugObjects && !hadSleep && (currentTime - lastServerTickedWithoutSleepTime).TotalSeconds > 60.0)
					{
						Debug.Info("Server ticked without sleep, time span ms", span.TotalMilliseconds, "tick ms", _tickMilliseconds, "objects", _objects.Count, "players", _players.Count, "vessels", _vessels.Count, "artificial bodies", SolarSystem.ArtificialBodiesCount);
						lastServerTickedWithoutSleepTime = currentTime;
					}
					hadSleep = false;
					DeltaTime = span.TotalSeconds;
					UpdateData(span.TotalSeconds);
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
					SpawnManager.UpdateTimers(DeltaTime);
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
					Thread.Sleep((int)(_tickMilliseconds - span.TotalMilliseconds));
				}
			}
			catch (Exception ex)
			{
				Debug.Exception(ex);
			}
		}

		// Shutting down...
		Debug.Log("Main game loop ended; Shutting down server...");
		if (SavePersistenceDataOnShutdown)
		{
			Persistence.Save();
		}
		MainLoopEnded.Set();
		try
		{
			if (ParentProcess.FileName == "GameServerWatchdog.exe" && !ParentProcess.GetParentProcess().HasExited)
			{
				Restart = false;
			}
		}
		catch
		{
			// ignored
		}

		if (Restart)
		{
			RestartServer(CleanRestart);
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
					Debug.Warning("Main loop stuck for more than 5 sec.");
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
			_players[player2.GUID] = player2;
			_objects[player2.FakeGuid] = player2;
		}
		_playersToAdd = new ConcurrentBag<Player>();
		foreach (Player player in _playersToRemove)
		{
			_players.Remove(player.GUID);
			_objects.Remove(player.FakeGuid);
		}
		_playersToRemove = new ConcurrentBag<Player>();
	}

	public static void RestartServer(bool clean)
	{
		try
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
		catch (Exception ex)
		{
			Debug.Exception(ex);
		}
	}

	public void InitializeWorld()
	{
		if (!WorldInitialized)
		{
			SpawnManager.Initialize();
			WorldInitialized = true;
		}
	}

	private void ToggleLockDoor(Ship ship, short inSceneId, bool isLocked)
	{
		VesselDockingPort port = ship.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == inSceneId);
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

	private void SubscribeToSpaceObjectListener(NetworkData data)
	{
		SubscribeToObjectsRequest req = data as SubscribeToObjectsRequest;
		Player player = GetPlayer(req.Sender);
		if (player == null)
		{
			return;
		}
		foreach (long guid in req.GUIDs)
		{
			SpaceObject so = GetObject(guid);
			if (so != null)
			{
				player.SubscribeTo(so);
				NetworkController.SendToGameClient(req.Sender, so.GetInitializeMessage());
				if (so is ArtificialBody)
				{
					player.UpdateArtificialBodyMovement.Add(so.GUID);
				}
			}
		}
	}

	private void UnsubscribeFromSpaceObjectListener(NetworkData data)
	{
		UnsubscribeFromObjectsRequest req = data as UnsubscribeFromObjectsRequest;
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

	public void PlayersOnServerRequestListener(NetworkData data)
	{
		PlayersOnServerRequest req = data as PlayersOnServerRequest;
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
			if (ves.SpawnPoints.Find((ShipSpawnPoint m) => m.SpawnPointID == req.SpawnPointID.InSceneID) == null)
			{
				return;
			}
			res.SpawnPointID = new VesselObjectID
			{
				InSceneID = req.SpawnPointID.InSceneID,
				VesselGUID = req.SpawnPointID.VesselGUID
			};

			res.PlayersOnServer = new List<PlayerOnServerData>();
			foreach (Player pl in NetworkController.GetAllPlayers())
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
					Debug.Error("Player ID is null or empty", pl.GUID, pl.Name);
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
			res.SecuritySystemID = new VesselObjectID
			{
				InSceneID = 0,
				VesselGUID = req.SecuritySystemID.VesselGUID
			};

			res.PlayersOnServer = new List<PlayerOnServerData>();
			foreach (Player pl in NetworkController.GetAllPlayers())
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
					Debug.Error("Player ID is null or empty", pl.GUID, pl.Name);
				}
			}
		}

		NetworkController.SendToGameClient(req.Sender, res);
	}

	public void AvailableSpawnPointsRequestListener(NetworkData data)
	{
		AvailableSpawnPointsRequest req = data as AvailableSpawnPointsRequest;
		Player pl = GetPlayer(req.Sender);
		if (pl != null)
		{
			NetworkController.SendToGameClient(req.Sender, new AvailableSpawnPointsResponse
			{
				SpawnPoints = GetAvailableSpawnPoints(pl)
			});
		}
	}

	public void ServerShutDownMessageListener(NetworkData data)
	{
	}

	private void NameTagMessageListener(NetworkData data)
	{
		NameTagMessage msg = data as NameTagMessage;
		SpaceObjectVessel vessel = GetVessel(msg.ID.VesselGUID);
		NetworkController.SendToClientsSubscribedTo(data, -1L, vessel);
		try
		{
			vessel.NameTags.Find((NameTagData m) => m.InSceneID == msg.ID.InSceneID).NameTagText = msg.NameTagText;
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

	public void UpdatePlayerInvitationTimers(double deltaTime)
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
			ClearSpawnPointInvitation(key);
		}
	}

	public void ClearSpawnPointInvitation(string playerId)
	{
		if (_spawnPointInvites.ContainsKey(playerId))
		{
			_spawnPointInvites[playerId].SpawnPoint.SetInvitation("", "", sendMessage: true);
			_spawnPointInvites.Remove(playerId);
		}
	}

	public void CreateSpawnPointInvitation(ShipSpawnPoint sp, string playerId, string playerName)
	{
		_spawnPointInvites.Add(playerId, new SpawnPointInviteData
		{
			SpawnPoint = sp,
			InviteTimer = SpawnPointInviteTimer
		});
		sp.SetInvitation(playerId, playerName, sendMessage: true);
	}

	public bool PlayerInviteChanged(ShipSpawnPoint sp, string invitedPlayerId, string invitedPlayerName, Player sender)
	{
		if (!invitedPlayerId.IsNullOrEmpty())
		{
			if (_spawnPointInvites.ContainsKey(invitedPlayerId) && _spawnPointInvites[invitedPlayerId].SpawnPoint == sp && sp.InvitedPlayerId == invitedPlayerId)
			{
				return false;
			}
			if (_spawnPointInvites.ContainsKey(invitedPlayerId))
			{
				ClearSpawnPointInvitation(invitedPlayerId);
			}
			if (!sp.InvitedPlayerId.IsNullOrEmpty() && _spawnPointInvites.ContainsKey(sp.InvitedPlayerId))
			{
				ClearSpawnPointInvitation(sp.InvitedPlayerId);
			}
			CreateSpawnPointInvitation(sp, invitedPlayerId, invitedPlayerName);
			return true;
		}
		if (!sp.InvitedPlayerId.IsNullOrEmpty())
		{
			if (_spawnPointInvites.ContainsKey(sp.InvitedPlayerId))
			{
				ClearSpawnPointInvitation(sp.InvitedPlayerId);
			}
			else
			{
				sp.SetInvitation("", "", sendMessage: true);
			}
			return true;
		}
		return false;
	}

	private void ServerAutoRestartTimer(double time)
	{
		DateTime currentTime = DateTime.UtcNow;
		if (currentTime.AddSeconds(_timeToRestart) > RestartTime)
		{
			if ((RestartTime - currentTime).TotalSeconds >= _timeToRestart - 2.0)
			{
				NetworkController.SendToAllClients(SendSystemMessage(SystemMessagesTypes.RestartServerTime, null), -1L);
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

	private void UpdateShipSystemsTimer(double time)
	{
		if (_updatingShipSystems)
		{
			return;
		}
		Task.Run(delegate
		{
			try
			{
				_updatingShipSystems = true;
				List<SpaceObjectVessel> list = new List<SpaceObjectVessel>(_vessels.Values);
				foreach (SpaceObjectVessel current in list)
				{
					current.UpdateVesselSystems();
					current.DecayGraceTimer = MathHelper.Clamp(current.DecayGraceTimer - time, 0.0, double.MaxValue);
					if (current.DecayGraceTimer <= double.Epsilon)
					{
						current.ChangeHealthBy((float)((double)(0f - current.ExposureDamage) * VesselDecayRateMultiplier * time), null, VesselRepairPoint.Priority.Internal, force: false, VesselDamageType.Decay, time);
					}
				}
				foreach (DebrisField current2 in DebrisFields)
				{
					current2.CheckVessels(time);
				}
			}
			catch
			{
			}
			_updatingShipSystems = false;
		});
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
		try
		{
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
		catch (Exception ex)
		{
			Debug.Exception(ex);
		}
	}

	private void ExplosionMessageListener(NetworkData data)
	{
		ExplosionMessage em = data as ExplosionMessage;
		Item item = GetItem(em.ItemGUID);
		if (item == null)
		{
			return;
		}
		HashSet<long> affectedObjects = new HashSet<long>();
		if (em.AffectedGUIDs != null && em.AffectedGUIDs.Length != 0)
		{
			Vector3D itemPos = item.DynamicObj.Position;
			long[] affectedGuiDs = em.AffectedGUIDs;
			foreach (long affectedGuid in affectedGuiDs)
			{
				SpaceObject tmpSp = Instance.GetObject(affectedGuid);
				if ((tmpSp.Position - item.DynamicObj.Position).Magnitude < (double)(item.ExplosionRadius * 1.5f) && affectedObjects.Add(tmpSp.GUID))
				{
					if (tmpSp is Player player)
					{
						player.Stats.TakeDamage(HurtType.Explosion, item.ExplosionDamage);
					}
					if (tmpSp is DynamicObject dynamicObject && dynamicObject.Item != null)
					{
						dynamicObject.Item.TakeDamage(new Dictionary<TypeOfDamage, float> { { item.ExplosionDamageType, item.ExplosionDamage } });
					}
				}
			}
		}
		if (item.ExplosionDamageType == TypeOfDamage.Impact && item.DynamicObj.Parent is SpaceObjectVessel parentVessel && affectedObjects.Add(parentVessel.GUID))
		{
			List<VesselRepairPoint> list = null;
			if (em.RepairPointIDs != null)
			{
				list = new List<VesselRepairPoint>();
				VesselObjectID[] repairPointIDs = em.RepairPointIDs;
				foreach (VesselObjectID rpid in repairPointIDs)
				{
					SpaceObjectVessel vessel = Instance.GetVessel(rpid.VesselGUID);
					if (vessel != null)
					{
						list.Add(parentVessel.RepairPoints.Find((VesselRepairPoint m) => m.ID.InSceneID == rpid.InSceneID));
					}
				}
			}
			if (parentVessel.ChangeHealthBy(0f - item.ExplosionDamage, list, VesselRepairPoint.Priority.None, force: false, VesselDamageType.GrenadeExplosion) != 0f)
			{
				ShipCollisionMessage scm = new ShipCollisionMessage
				{
					CollisionVelocity = 0f,
					ShipOne = parentVessel.GUID,
					ShipTwo = -1L
				};
				NetworkController.SendToClientsSubscribedTo(scm, -1L, parentVessel);
			}
		}
		Extensions.Invoke(delegate
		{
			item.DestroyItem();
		}, 1.0);
	}
}
