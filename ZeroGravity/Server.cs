using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BulletSharp;
using OpenHellion.Networking;
using OpenHellion.Networking.Message.MainServer;
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

		public AttachPointDetails APDetails;

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
		private static DateTime lastClearDate = DateTime.UtcNow;

		private static Dictionary<GameScenes.SceneID, int> dailySpawnCount = new Dictionary<GameScenes.SceneID, int>();

		private static List<char> monthCodes = new List<char>
		{
			'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
			'K', 'L'
		};

		private static Dictionary<GameScenes.SceneID, string> shipNaming = new Dictionary<GameScenes.SceneID, string>
		{
			{
				GameScenes.SceneID.AltCorp_LifeSupportModule,
				"LSM-AC:HE3/"
			},
			{
				GameScenes.SceneID.ALtCorp_PowerSupply_Module,
				"PSM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_AirLock,
				"AM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Cargo_Module,
				"CBM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Command_Module,
				"CM-AC:HE3/"
			},
			{
				GameScenes.SceneID.AltCorp_DockableContainer,
				"IC-AC:HE2/"
			},
			{
				GameScenes.SceneID.AltCorp_CorridorIntersectionModule,
				"CTM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Corridor45TurnModule,
				"CLM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Corridor45TurnRightModule,
				"CRM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_CorridorVertical,
				"CSM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_CorridorModule,
				"CIM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_StartingModule,
				"OUTPOST "
			},
			{
				GameScenes.SceneID.AltCorp_Shuttle_SARA,
				"AC-ARG HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_CrewQuarters_Module,
				"CQM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_SolarPowerModule,
				"SPM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_FabricatorModule,
				"FM-AC:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Shuttle_CECA,
				"Steropes:HE1/"
			},
			{
				GameScenes.SceneID.Generic_Debris_Spawn1,
				"Small Debris:HE1/"
			},
			{
				GameScenes.SceneID.Generic_Debris_Spawn2,
				"Large Debris:HE1/"
			},
			{
				GameScenes.SceneID.Generic_Debris_Spawn3,
				"Medium Debris:HE1/"
			},
			{
				GameScenes.SceneID.Generic_Debris_Outpost001,
				"Doomed outpost:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_PatchModule,
				"Bulkhead:HE1/"
			},
			{
				GameScenes.SceneID.SOE_Location002,
				"SOE Derelict:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Secure_Module,
				"CM-SDS:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_Destroyed_Shuttle_CECA,
				"Steropes:HE1/"
			},
			{
				GameScenes.SceneID.AltCorp_CorridorIntersection_MKII,
				"CTM-SDS:HE1/"
			},
			{
				GameScenes.SceneID.AlrCorp_Corridor_MKII,
				"CIM-SDS:HE1/"
			}
		};

		private static List<GameScenes.SceneID> derelicts = new List<GameScenes.SceneID>
		{
			GameScenes.SceneID.Generic_Debris_Corridor001,
			GameScenes.SceneID.Generic_Debris_Corridor002,
			GameScenes.SceneID.Generic_Debris_JuncRoom001,
			GameScenes.SceneID.Generic_Debris_JuncRoom002
		};

		public static string GenerateObjectRegistration(SpaceObjectType type, CelestialBody parentCB, GameScenes.SceneID sceneId)
		{
			string name = "";
			string dailySpawnDigits = "000";
			if (type == SpaceObjectType.Ship && !derelicts.Contains(sceneId))
			{
				if (shipNaming.ContainsKey(sceneId))
				{
					name += shipNaming[sceneId];
				}
				else
				{
					Dbg.Warning("No name tag for ship", sceneId);
					name = name + type.ToString() + MathHelper.RandomNextInt().ToString("X");
				}
			}
			else
			{
				string parentBody;
				if (parentCB != null)
				{
					string plnt = ((CelestialBodyGUID)parentCB.GUID).ToString();
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
			if ((DateTime.UtcNow - lastClearDate).TotalDays > 1.0)
			{
				lastClearDate = DateTime.UtcNow;
				dailySpawnCount.Clear();
			}
			if (dailySpawnCount.ContainsKey(sceneId))
			{
				dailySpawnCount[sceneId]++;
			}
			else
			{
				dailySpawnCount.Add(sceneId, 1);
			}
			return name + DateTime.UtcNow.Day.ToString("00") + monthCodes[DateTime.UtcNow.Month - 1] + dailySpawnCount[sceneId].ToString(dailySpawnDigits);
		}

		public static string GenerateStationRegistration()
		{
			return "STATION";
		}

		public static GameScenes.SceneID GetSceneID(string text)
		{
			foreach (GameScenes.SceneID scene in Enum.GetValues(typeof(GameScenes.SceneID)))
			{
				if (scene.ToString().ToLower().StartsWith(text.ToLower()))
				{
					return scene;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneID, string> kv2 in shipNaming)
			{
				if (kv2.Value.ToLower().StartsWith(text.ToLower()))
				{
					return kv2.Key;
				}
			}
			foreach (KeyValuePair<GameScenes.SceneID, string> kv in shipNaming)
			{
				if (kv.Value.ToLower().Contains(text.ToLower()))
				{
					return kv.Key;
				}
			}
			return GameScenes.SceneID.None;
		}
	}

	public static double RCS_THRUST_MULTIPLIER = 1.0;

	public static double RCS_ROTATION_MULTIPLIER = 1.0;

	public static double CELESTIAL_BODY_RADIUS_MULTIPLIER = 1.0;

	public static Properties Properties = null;

	public static string ConfigDir = "";

#if HELLION_SP
	public static double PersistenceSaveInterval = 0.0;
#else
	public static double PersistenceSaveInterval = 900.0;
#endif

	public static bool CleanStart = false;

	public static bool Restart = false;

	public static bool CleanRestart = false;

	public static double CelestialBodyDeathDistance = 10000.0;

	private Dictionary<long, SpaceObject> m_objects = new Dictionary<long, SpaceObject>();

	private Dictionary<long, Player> m_players = new Dictionary<long, Player>();

	private ConcurrentBag<Player> m_playersToAdd = new ConcurrentBag<Player>();

	private ConcurrentBag<Player> m_playersToRemove = new ConcurrentBag<Player>();

	private Dictionary<long, SpaceObjectVessel> m_vessels = new Dictionary<long, SpaceObjectVessel>();

	private Dictionary<long, DynamicObject> m_updateableDynamicObjects = new Dictionary<long, DynamicObject>();

	private List<UpdateTimer> m_timersToRemove = new List<UpdateTimer>();

	private List<UpdateTimer> m_timers = new List<UpdateTimer>();

	public SolarSystem SolarSystem = null;

	private static Server serverInstance = null;

	public static bool IsRunning = false;

	private long numberOfTicks = 64L;

#if HELLION_SP
	public static int GamePort = 6104;

	public static int StatusPort = 6105;
#else
	public static int GamePort = 6004;

	public static int StatusPort = 6005;
#endif

	public static bool SavePersistenceDataOnShutdown = false;

	public static bool CheckInPassed = false;

	public bool WorldInitialized = false;

	public DateTime ServerStartTime = DateTime.UtcNow;

	private float solarSystemStartTime = -1f;

	public string ServerPassword = "";

	public string ServerName = "Hellion Game Server";

	public string Description = "";

	public int MaxPlayers = 100;

	public static int MaxNumberOfSaveFiles = 10;

	private IpAddressRange[] AdminIPAddressRanges = new IpAddressRange[0];

	private Dictionary<string, SpawnPointInviteData> SpawnPointInvites = new Dictionary<string, SpawnPointInviteData>();

	public static AutoResetEvent MainLoopEnded = new AutoResetEvent(initialState: false);

	private bool mainLoopStarted = false;

	public List<DebrisField> DebrisFields = new List<DebrisField>();

	public static double VesselDecayRateMultiplier = 1.0;

	public static double VesselDecayGracePeriod = 0.0;

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

	private bool updateDataInSeparateThread;

	public static int JunkItemsCleanupScope = 1;

	public static double JunkItemsTimeToLive = 3600.0;

	public static double JunkItemsCleanupInterval = 900.0;

	public static bool CanWarpThroughCelestialBodies = false;

	public static double MaxAngularVelocityPerAxis = 300.0;

	private static List<string> serverAdmins = new List<string>();

	public static double ServerRestartTimeSec = -1.0;

	public static DateTime restartTime;

	private double timeToRestart = 1800.0;

	public List<DeathMatchArenaController> DeathMatchArenaControllers = new List<DeathMatchArenaController>();

	public DoomedShipController DoomedShipController = new DoomedShipController();

	public static double MovementMessageSendInterval = 0.1;

	public static bool ForceMovementMessageSend = false;

	private static double _movementMessageTimer = 0.0;

	public static volatile int MainThreadID;

	public BulletPhysicsController PhysicsController;

	public DateTime LastChatMessageTime = DateTime.UtcNow;

	private double persistenceSaveTimer = 0.0;

	private bool printDebugObjects = false;

	public static uint NetworkDataHash = ClassHasher.GetClassHashCode(typeof(NetworkData));

	public static uint SceneDataHash = ClassHasher.GetClassHashCode(typeof(ISceneData));

	public static uint CombinedHash = NetworkDataHash * SceneDataHash;

	private bool manualSave;

	private string manualSaveFileName = null;

	private SaveFileAuxData manualSaveAuxData = null;

	private bool updatingShipSystems;

	public ConcurrentDictionary<long, VesselDataUpdate> VesselsDataUpdate = new ConcurrentDictionary<long, VesselDataUpdate>();

	public List<DynamicObjectsRespawn> DynamicObjectsRespawnList = new List<DynamicObjectsRespawn>();

	private double tickMilliseconds = 0.0;

	public double DeltaTime;

	private DateTime lastTime;

	public AutoResetEvent UpdateDataFinished = new AutoResetEvent(initialState: false);

	public static List<GameScenes.SceneID> RandomShipSpawnSceneIDs = new List<GameScenes.SceneID>
	{
		GameScenes.SceneID.AltCorp_CorridorModule,
		GameScenes.SceneID.AltCorp_CorridorIntersectionModule,
		GameScenes.SceneID.AltCorp_Corridor45TurnModule,
		GameScenes.SceneID.AltCorp_Shuttle_SARA,
		GameScenes.SceneID.ALtCorp_PowerSupply_Module,
		GameScenes.SceneID.AltCorp_LifeSupportModule,
		GameScenes.SceneID.AltCorp_Cargo_Module,
		GameScenes.SceneID.AltCorp_CorridorVertical,
		GameScenes.SceneID.AltCorp_Command_Module,
		GameScenes.SceneID.AltCorp_Corridor45TurnRightModule,
		GameScenes.SceneID.AltCorp_StartingModule,
		GameScenes.SceneID.AltCorp_AirLock,
		GameScenes.SceneID.Generic_Debris_JuncRoom001,
		GameScenes.SceneID.Generic_Debris_JuncRoom002,
		GameScenes.SceneID.Generic_Debris_Corridor001,
		GameScenes.SceneID.Generic_Debris_Corridor002,
		GameScenes.SceneID.AltCorp_DockableContainer
	};

	public static double SpawnPointInviteTimer = 300.0;

	public static string LoadPersistenceFromFile = null;

	public Dictionary<long, SpaceObjectVessel>.ValueCollection AllVessels => m_vessels.Values;

	public Dictionary<long, Player>.ValueCollection AllPlayers => m_players.Values;

	public static Server Instance => serverInstance;

	public TimeSpan RunTime => DateTime.UtcNow - ServerStartTime;

	public double TickMilliseconds => tickMilliseconds;

	public static double SolarSystemTime => Instance.SolarSystem.CurrentTime;

	public bool DoesObjectExist(long guid)
	{
		return m_objects.ContainsKey(guid);
	}

	public SpaceObject GetObject(long guid)
	{
		if (m_objects.ContainsKey(guid))
		{
			return m_objects[guid];
		}
		return null;
	}

	public SpaceObjectTransferable GetTransferable(long guid)
	{
		if (m_objects.ContainsKey(guid) && m_objects[guid] is SpaceObjectTransferable)
		{
			return m_objects[guid] as SpaceObjectTransferable;
		}
		return null;
	}

	/// <summary>
	/// 	Get a player with a guid.
	/// </summary>
	public Player GetPlayer(long guid)
	{
		Player player = null;
		m_players.TryGetValue(guid, out player);
		return player;
	}

	/// <summary>
	/// 	Gets a player from a specified player id.
	/// </summary>
	public Player GetPlayerFromPlayerId(string playerId)
	{
		return GetPlayer(GUIDFactory.PlayerIdToGuid(playerId));
	}

	/// <summary>
	/// 	Gets a player from a specified native id.<br />
	/// 	Warning: This is pretty slow, as it searches through all players.
	/// </summary>
	public Player GetPlayerFromNativeId(string nativeId)
	{
		return m_players.Values.ToList().Find((Player m) => m.NativeId == nativeId);
	}

	public SpaceObjectVessel GetVessel(long guid)
	{
		if (m_vessels.ContainsKey(guid))
		{
			return m_vessels[guid];
		}
		return null;
	}

	public Item GetItem(long guid)
	{
		if (m_objects.ContainsKey(guid) && m_objects[guid] is DynamicObject)
		{
			return (m_objects[guid] as DynamicObject).Item;
		}
		return null;
	}

	public void Add(Player player)
	{
		if (mainLoopStarted)
		{
			m_playersToAdd.Add(player);
			return;
		}
		m_players[player.GUID] = player;
		m_objects[player.FakeGuid] = player;
	}

	public void Add(SpaceObjectVessel vessel)
	{
		m_vessels[vessel.GUID] = vessel;
		m_objects[vessel.GUID] = vessel;
	}

	public void Add(DynamicObject dobj)
	{
		m_objects[dobj.GUID] = dobj;
		if (dobj.Item != null && dobj.Item is IUpdateable)
		{
			m_updateableDynamicObjects[dobj.GUID] = dobj;
		}
	}

	public void Add(Corpse corpse)
	{
		m_objects[corpse.GUID] = corpse;
	}

	public void Remove(Player player)
	{
		if (mainLoopStarted)
		{
			m_playersToRemove.Add(player);
			return;
		}
		m_players.Remove(player.GUID);
		m_objects.Remove(player.FakeGuid);
	}

	public void Remove(SpaceObjectVessel vessel)
	{
		m_vessels.Remove(vessel.GUID);
		m_objects.Remove(vessel.GUID);
	}

	public void Remove(DynamicObject dobj)
	{
		m_objects.Remove(dobj.GUID);
		m_updateableDynamicObjects.Remove(dobj.GUID);
	}

	public void Remove(Corpse corpse)
	{
		if (m_objects.ContainsKey(corpse.GUID))
		{
			m_objects.Remove(corpse.GUID);
		}
	}

	public Server()
	{
		MainThreadID = Thread.CurrentThread.ManagedThreadId;
		IsRunning = true;
		serverInstance = this;
		PhysicsController = new BulletPhysicsController();
		SolarSystem = new SolarSystem();
		LoadServerSettings();
		Console.Title = ServerName + " (id: " + ((NetworkController.ServerID == null) ? "Not yet assigned" : string.Concat(NetworkController.ServerID)) + ")";
		Stopwatch stopWatch = new Stopwatch();
		stopWatch.Start();
		Thread.Sleep(1);
		stopWatch.Stop();
		long maxTicks = (long)(1000.0 / stopWatch.Elapsed.TotalMilliseconds);
		Dbg.UnformattedMessage(string.Format("==============================================================================\r\n\tServer name: {5}\r\n\tServer ID: {1}\r\n\tGame port: {6}\r\n\tStatus port: {7}\r\n\tStart date: {0}\r\n\tServer ticks: {2}{4}\r\n\tMax server ticks (not precise): {3}\r\n==============================================================================", DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss.ffff"), (NetworkController.ServerID == null) ? "Not yet assigned" : string.Concat(NetworkController.ServerID), numberOfTicks, maxTicks, (numberOfTicks > maxTicks) ? " WARNING: Server ticks is larger than max tick" : "", ServerName, GamePort, StatusPort));
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
						Dbg.Error("-configdir was not supplied a path.");
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
						Dbg.Error("-load was not supplied a path.");
						break;
					}

					LoadPersistenceFromFile = args[i];
				break;
				case "-randomships":
					i++;

					if (i >= args.Length)
					{
						Dbg.Error("-randomships was not supplied a number.");
						break;
					}

					randomShips = args[++i];
				break;
				case "-gport":
					i++;

					if (i >= args.Length)
					{
						Dbg.Error("-gport was not supplied a port.");
						break;
					}

					gPort = args[++i];
				break;
				case "-sport":
					i++;

					if (i >= args.Length)
					{
						Dbg.Error("-sport was not supplied a port.");
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
			Properties.SetProperty("game_client_port", gPort);
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
		try
		{
			NetworkController.ServerID = File.ReadAllText(ConfigDir + "ServerID.txt").Trim();
		}
		catch
		{
		}
		Properties.GetProperty("server_tick_count", ref numberOfTicks);
		Properties.GetProperty("game_client_port", ref GamePort);
		Properties.GetProperty("status_port", ref StatusPort);
#if HELLION_SP
		CheckAndFixPorts();
#endif
		Properties.GetProperty("main_server_ip", ref MSConnection.IpAddress);
		Properties.GetProperty("main_server_port", ref MSConnection.Port);
		string admins = "";
		Properties.GetProperty("server_admins", ref admins);
		string[] adminsArray = admins.Split(',');
		serverAdmins = adminsArray.Where((string m) => m != "").ToList();
		Properties.GetProperty("movement_send_interval", ref MovementMessageSendInterval);
		if (MovementMessageSendInterval <= 1.4012984643248171E-45)
		{
			MovementMessageSendInterval = 0.1;
		}
		Properties.GetProperty("solar_system_time", ref solarSystemStartTime);
		Properties.GetProperty("save_interval", ref PersistenceSaveInterval);
		Properties.GetProperty("server_password", ref ServerPassword);
		Properties.GetProperty("server_name", ref ServerName);
		Properties.GetProperty("description", ref Description);
		if (Description.Length > 500)
		{
			Description = Description.Substring(0, 497) + "...";
			Dbg.Error("Server description too long. Maximum length is 500 characters.");
		}
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
		Properties.GetProperty("update_data_in_separate_thread", ref updateDataInSeparateThread);
		Properties.GetProperty("junk_items_cleanup_scope", ref JunkItemsCleanupScope);
		Properties.GetProperty("junk_items_time_to_live", ref JunkItemsTimeToLive);
		Properties.GetProperty("junk_items_cleanup_interval", ref JunkItemsCleanupInterval);
		Properties.GetProperty("can_warp_through_celestial_bodies", ref CanWarpThroughCelestialBodies);
		Properties.GetProperty("max_angular_velocity_per_axis", ref MaxAngularVelocityPerAxis);
		double tmpTimer = SpaceObjectVessel.ArenaRescueTime;
		Properties.GetProperty("arena_ship_respawn_timer", ref tmpTimer);
		SpaceObjectVessel.ArenaRescueTime = tmpTimer;
		Properties.GetProperty("print_debug_objects", ref printDebugObjects);
		Properties.GetProperty("spawn_manager_print_categories", ref SpawnManager.Settings.PrintCategories);
		Properties.GetProperty("spawn_manager_print_spawn_rules", ref SpawnManager.Settings.PrintSpawnRules);
		Properties.GetProperty("spawn_manager_print_item_attach_points", ref SpawnManager.Settings.PrintItemAttachPoints);
		Properties.GetProperty("spawn_manager_print_item_type_ids", ref SpawnManager.Settings.PrintItemTypeIDs);
	}

	private void CheckAndFixPorts()
	{
		for (int j = 6000; j <= 65535; j++)
		{
			if (j == StatusPort)
			{
				continue;
			}
			IPEndPoint ep2 = new IPEndPoint(System.Net.IPAddress.Any, j);
			Socket socket2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				socket2.Bind(ep2);
				socket2.Close();
				GamePort = j;
			}
			catch
			{
				continue;
			}
			break;
		}
		for (int i = 6000; i <= 65535; i++)
		{
			if (i != GamePort)
			{
				IPEndPoint ep = new IPEndPoint(System.Net.IPAddress.Any, i);
				Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					socket.Bind(ep);
					socket.Close();
					StatusPort = i;
					break;
				}
				catch
				{
				}
			}
		}
	}

	public void LoginPlayer(long guid, string playerId, string nativeId, CharacterData characterData)
	{
		if (!WorldInitialized)
		{
			InitializeWorld();
		}
		Player player = GetPlayer(guid);
		if (player != null)
		{
			NetworkController.Instance.ConnectPlayer(player);
		}
		else
		{
			player = new Player(guid, Vector3D.Zero, QuaternionD.Identity, characterData.Name, playerId, nativeId, characterData.Gender, characterData.HeadType, characterData.HairType);
			Add(player);
			NetworkController.Instance.ConnectPlayer(player);
		}
		if (serverAdmins.Contains(player.PlayerId) || serverAdmins.Contains("*"))
		{
			player.IsAdmin = true;
		}
	}

	private void ResetSpawnPointsForPlayer(Player pl, Ship skipShip)
	{
		if (!pl.PlayerId.IsNullOrEmpty() && SpawnPointInvites.ContainsKey(pl.PlayerId))
		{
			ClearSpawnPointInvitation(pl.PlayerId);
		}
		foreach (SpaceObjectVessel ves in AllVessels)
		{
			if (ves is Ship && ves != skipShip)
			{
				(ves as Ship).ResetSpawnPointsForPlayer(pl, sendStatsMessage: true);
			}
		}
	}

	private bool AddPlayerToShip(Player pl, SpawnSetupType setupType, long shipID)
	{
		if (shipID == 0L && setupType == SpawnSetupType.None)
		{
			return false;
		}
		Ship foundShip = null;
		ShipSpawnPoint foundSpawnPoint = null;
#if HELLION_SP
		if (setupType != 0)
		{
			if (setupType == SpawnSetupType.Continue)
			{
				if (pl.AuthorizedSpawnPoint == null)
				{
					return false;
				}
				foundShip = pl.AuthorizedSpawnPoint.Ship;
				foundSpawnPoint = pl.AuthorizedSpawnPoint;
			}
			else if (setupType == SpawnSetupType.MiningSteropes || setupType == SpawnSetupType.MiningArges)
			{
				Asteroid ast2 = GetRandomAsteroid();
				foundShip = Ship.CreateNewShip((setupType == SpawnSetupType.MiningSteropes) ? GameScenes.SceneID.AltCorp_Shuttle_CECA : GameScenes.SceneID.AltCorp_Shuttle_SARA, "", -1L, (ast2 != null) ? new List<long> { ast2.GUID } : null, null, null, null, null, "StartingScene;");
				if (foundShip != null)
				{
					Vector3D forward = ast2.Position - foundShip.Position;
					Vector3D up = foundShip.Up;
					Vector3D.OrthoNormalize(ref forward, ref up);
					foundShip.Forward = forward;
					foundShip.Up = up;
					foundSpawnPoint = foundShip.GetPlayerSpawnPoint(pl);
					if (foundShip.AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == pl.PlayerId) == null)
					{
						foundShip.AuthorizedPersonel.Add(new AuthorizedPerson
						{
							PlayerId = pl.PlayerId,
							PlayerNativeId = pl.NativeId,
							Name = pl.Name,
							Rank = AuthorizedPersonRank.CommandingOfficer
						});
					}
				}
			}
			else if (setupType == SpawnSetupType.TestShip)
			{
				Asteroid ast = GetRandomAsteroid();
				foundShip = Ship.CreateNewShip(GameScenes.SceneID.FlatShipTest, "", -1L, (ast != null) ? new List<long> { ast.GUID } : null, null, null, null, null, "StartingScene;");
				if (foundShip != null)
				{
					Vector3D forward2 = ast.Position - foundShip.Position;
					Vector3D up2 = foundShip.Up;
					Vector3D.OrthoNormalize(ref forward2, ref up2);
					foundShip.Forward = forward2;
					foundShip.Up = up2;
					foundSpawnPoint = foundShip.GetPlayerSpawnPoint(pl);
					if (foundShip.AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == pl.PlayerId) == null)
					{
						foundShip.AuthorizedPersonel.Add(new AuthorizedPerson
						{
							PlayerId = pl.PlayerId,
							PlayerNativeId = pl.NativeId,
							Name = pl.Name,
							Rank = AuthorizedPersonRank.CommandingOfficer
						});
					}
				}
			}
			else if (setupType == SpawnSetupType.FreeRoamSteropes || setupType == SpawnSetupType.FreeRoamArges)
			{
				foundShip = Ship.CreateNewShip((setupType == SpawnSetupType.FreeRoamSteropes) ? GameScenes.SceneID.AltCorp_Shuttle_CECA : GameScenes.SceneID.AltCorp_Shuttle_SARA, "", -1L, null, null, null, null, null, "StartingScene;");
				if (foundShip != null)
				{
					foundSpawnPoint = foundShip.GetPlayerSpawnPoint(pl);
					if (foundShip.AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == pl.PlayerId) == null)
					{
						foundShip.AuthorizedPersonel.Add(new AuthorizedPerson
						{
							PlayerId = pl.PlayerId,
							PlayerNativeId = pl.NativeId,
							Name = pl.Name,
							Rank = AuthorizedPersonRank.CommandingOfficer
						});
					}
				}
			}
			else
			{
				switch (setupType)
				{
				case SpawnSetupType.SteropesNearRandomStation:
				{
					SpaceObjectVessel ves = (from m in AllVessels
						where m.IsMainVessel && m.IsPrefabStationVessel
						orderby MathHelper.RandomNextDouble()
						select m).FirstOrDefault();
					foundShip = Ship.CreateNewShip(GameScenes.SceneID.AltCorp_Shuttle_CECA, "", -1L, (ves != null) ? new List<long> { ves.GUID } : null, null, null, null, null, "StartingScene;");
					if (foundShip != null)
					{
						foundSpawnPoint = foundShip.GetPlayerSpawnPoint(pl);
						Vector3D forward3 = ves.Position - foundShip.Position;
						Vector3D up3 = foundShip.Up;
						Vector3D.OrthoNormalize(ref forward3, ref up3);
						foundShip.Forward = forward3;
						foundShip.Up = up3;
					}
					break;
				}
				case SpawnSetupType.SteropesNearDoomedOutpost:
				{
					Ship sh = Ship.CreateNewShip(GameScenes.SceneID.Generic_Debris_Outpost001, "", -1L);
					sh.Orbit.InitFromPeriapisAndApoapsis(SolarSystem.GetCelestialBody(2L).Orbit, MathHelper.RandomRange(9500000, 10000000), MathHelper.RandomRange(28000000, 32000000), MathHelper.RandomRange(-90, 90), MathHelper.RandomRange(32, 89), MathHelper.RandomRange(44, 87), MathHelper.RandomRange(270, 270), SolarSystemTime);
					foundShip = Ship.CreateNewShip(GameScenes.SceneID.AltCorp_Shuttle_CECA, "", -1L, (sh != null) ? new List<long> { sh.GUID } : null, null, null, null, null, "StartingScene;");
					if (foundShip != null)
					{
						Vector3D forward4 = sh.Position - foundShip.Position;
						Vector3D up4 = foundShip.Up;
						Vector3D.OrthoNormalize(ref forward4, ref up4);
						foundShip.Forward = forward4;
						foundShip.Up = up4;
						foundSpawnPoint = foundShip.GetPlayerSpawnPoint(pl);
					}
					break;
				}
				default:
					ResetSpawnPointsForPlayer(pl, null);
					foundShip = SpawnManager.SpawnStartingSetup(setupType.ToString());
					if (foundShip != null)
					{
						foundSpawnPoint = SpawnManager.SetStartingSetupSpawnPoints(foundShip, pl);
					}
					if (foundShip == null || foundSpawnPoint == null)
					{
						Dbg.Error("FAILED TO SPAWN STARTING SETUP", pl.GUID, foundShip?.GUID ?? (-1));
					}
					break;
				}
			}
		}
		else if (shipID > 0 && SpawnPointInvites.ContainsKey(pl.PlayerId) && SpawnPointInvites[pl.PlayerId].SpawnPoint.Ship.GUID == shipID)
		{
			foundShip = GetVessel(shipID) as Ship;
			foundSpawnPoint = ((foundShip != null) ? SpawnPointInvites[pl.PlayerId].SpawnPoint : null);
			if (foundSpawnPoint != null)
			{
				ResetSpawnPointsForPlayer(pl, null);
			}
		}
		else if (shipID > 0)
		{
			foundShip = GetVessel(shipID) as Ship;
			foundSpawnPoint = foundShip?.GetPlayerSpawnPoint(pl);
			if (foundSpawnPoint != null)
			{
				ResetSpawnPointsForPlayer(pl, null);
			}
		}
#else
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
				Dbg.Error("FAILED TO SPAWN STARTING SETUP", pl.GUID, foundShip?.GUID ?? (-1));
			}
			break;
		case SpawnSetupType.None:
			if (shipID > 0 && SpawnPointInvites.ContainsKey(pl.PlayerId) && SpawnPointInvites[pl.PlayerId].SpawnPoint.Ship.GUID == shipID)
			{
				foundShip = GetVessel(shipID) as Ship;
				foundSpawnPoint = ((foundShip != null) ? SpawnPointInvites[pl.PlayerId].SpawnPoint : null);
				if (foundSpawnPoint != null)
				{
					ResetSpawnPointsForPlayer(pl, null);
				}
			}
			break;
		}
#endif
		if (foundShip != null && foundSpawnPoint != null)
		{
			foundSpawnPoint.Player = pl;
			if (foundSpawnPoint.Executer != null)
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

	public void CheckPosition(Player player)
	{
	}

	public void RemovePlayer(Player player)
	{
		if (player.Parent != null)
		{
			if (player.Parent.ObjectType == SpaceObjectType.PlayerPivot)
			{
				(player.Parent as Pivot).Destroy();
			}
			if (player.Parent.ObjectType == SpaceObjectType.Ship || player.Parent.ObjectType == SpaceObjectType.Asteroid)
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
		List<SpawnPointDetails> retVal = new List<SpawnPointDetails>();
		if (pl.PlayerId != null && SpawnPointInvites.ContainsKey(pl.PlayerId))
		{
			ShipSpawnPoint sp = SpawnPointInvites[pl.PlayerId].SpawnPoint;
			SpawnPointDetails spd = new SpawnPointDetails();
			spd.Name = sp.Ship.FullName;
			spd.IsPartOfCrew = false;
			spd.SpawnPointParentID = sp.Ship.GUID;
			spd.PlayersOnShip = new List<string>();
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
		NetworkController.Instance.SendToGameClient(data.Sender, data);
	}

	private void Start()
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
		EventSystem.AddListener(typeof(SaveGameMessage), SaveGameMessageListener);
		EventSystem.AddListener(typeof(ServerShutDownMessage), ServerShutDownMessageListener);
		EventSystem.AddListener(typeof(NameTagMessage), NameTagMessageListener);

#if !HELLION_SP
		MSConnection.Get<CheckInResponse>(new CheckInRequest
		{
			//ServerID = NetworkController.ServerID,
			//ServerName = ServerName,
			Region = Region.Europe,
			GamePort = GamePort,
			StatusPort = StatusPort,
			//Private = !ServerPassword.IsNullOrEmpty(),
			Hash = CombinedHash
			//CleanStart = CleanStart
		}, CheckInResponseListener);
#endif
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
			if (dobj2 != null && dobj2.Item != null && dobj2.Item is ICargo && toCargo2 != null)
			{
				if (trm.ToLocationType != 0)
				{
					TransferResources(dobj2.Item as ICargo, trm.FromCompartmentID, toCargo2, trm.ToCompartmentID, trm.ResourceType, trm.Quantity);
				}
				else
				{
					VentResources(dobj2.Item as ICargo, trm.FromCompartmentID, trm.ResourceType, trm.Quantity);
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
			if (dobj != null && dobj.Item != null && dobj.Item is ICargo && fromCargo2 != null)
			{
				if (trm.ToLocationType != 0)
				{
					TransferResources(fromCargo2, trm.FromCompartmentID, dobj.Item as ICargo, trm.ToCompartmentID, trm.ResourceType, trm.Quantity);
				}
				else
				{
					VentResources(dobj.Item as ICargo, trm.FromCompartmentID, trm.ResourceType, trm.Quantity);
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

	public void TransferResources(ICargo fromCargo, short fromCompartmentID, ICargo toCargo, short toCompartmentID, ResourceType resourceType, float quantity)
	{
		CargoCompartmentData fromCompartment = fromCargo.Compartments.Find((CargoCompartmentData m) => m.ID == fromCompartmentID);
		CargoCompartmentData toCompartment = toCargo.Compartments.Find((CargoCompartmentData m) => m.ID == toCompartmentID);
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
			fromCargo.ChangeQuantityBy(fromCompartmentID, resourceType, 0f - qty);
			toCargo.ChangeQuantityBy(toCompartmentID, resourceType, qty);
		}
	}

	private void VentResources(ICargo fromCargo, short fromCompartmentID, ResourceType resourceType, float quantity)
	{
		CargoCompartmentData fromCompartment = fromCargo.Compartments.Find((CargoCompartmentData m) => m.ID == fromCompartmentID);
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
			fromCargo.ChangeQuantityBy(fromCompartmentID, resourceType, 0f - qty);
		}
	}

	public void FabricateItemMessageListener(NetworkData data)
	{
		FabricateItemMessage fim = data as FabricateItemMessage;
		SpaceObjectVessel vessel = GetVessel(fim.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(fim.ID) is SubSystemFabricator fabricator)
		{
			fabricator.Fabricate(fim.ItemType);
		}
	}

	public void CancelFabricationMessageListener(NetworkData data)
	{
		CancelFabricationMessage cfm = data as CancelFabricationMessage;
		SpaceObjectVessel vessel = GetVessel(cfm.ID.VesselGUID);
		if (vessel != null && vessel.DistributionManager.GetSubSystem(cfm.ID) is SubSystemFabricator fabricator)
		{
			fabricator.Cancel(cfm.CurrentItemOnly);
		}
	}

	public void RepairMessageListener(NetworkData data)
	{
		Player pl = GetPlayer(data.Sender);
		if (pl == null)
		{
			return;
		}
		Item rTool = pl.PlayerInventory.HandsSlot.Item;
		if (!(rTool is RepairTool))
		{
			return;
		}
		if (data is RepairVesselMessage)
		{
			RepairVesselMessage rvm = data as RepairVesselMessage;
			(rTool as RepairTool).RepairVessel(rvm.ID);
		}
		else if (data is RepairItemMessage)
		{
			RepairItemMessage rim = data as RepairItemMessage;
			RepairTool rt = rTool as RepairTool;
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

	public void HurtPlayerMessageListener(NetworkData data)
	{
		Player pl = GetPlayer(data.Sender);
		HurtPlayerMessage hpm = data as HurtPlayerMessage;
		pl.Stats.TakeDamage(hpm.Duration, hpm.Damage);
	}

	public void ConsoleMessageListener(NetworkData data)
	{
		Player player = GetPlayer(data.Sender);
		ConsoleMessage cm = data as ConsoleMessage;
		if (player.IsAdmin)
		{
			ProcessConsoleCommand(cm.Text, player);
		}
	}

	public void TextChatMessageListener(NetworkData data)
	{
		TextChatMessage tcm = data as TextChatMessage;
		Player player = m_players[tcm.Sender];
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
				foreach (Player pl in m_players.Values)
				{
					if ((pl.Parent.Position + pl.Position - playerGlobalPos).SqrMagnitude < 1000000.0 && pl != player)
					{
						NetworkController.Instance.SendToGameClient(pl.GUID, tcm);
					}
				}
				return;
			}
		}
		NetworkController.Instance.SendToAllClients(tcm, tcm.Sender);
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
				if (handsItem != null && handsItem is ICargo)
				{
					foreach (CargoCompartmentData ccd in (handsItem as ICargo).Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
					{
						using List<CargoResourceData>.Enumerator enumerator2 = ccd.Resources.GetEnumerator();
						if (enumerator2.MoveNext())
						{
							CargoResourceData r = enumerator2.Current;
							(handsItem as ICargo).ChangeQuantityBy(ccd.ID, r.ResourceType, ccd.Capacity);
						}
					}
				}
				if (player.PlayerInventory.CurrOutfit != null)
				{
					foreach (InventorySlot os in player.PlayerInventory.CurrOutfit.InventorySlots.Values)
					{
						Item item2 = os.Item;
						if (item2 == null || !(item2 is ICargo))
						{
							continue;
						}
						foreach (CargoCompartmentData ccd2 in (item2 as ICargo).Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
						{
							using List<CargoResourceData>.Enumerator enumerator5 = ccd2.Resources.GetEnumerator();
							if (enumerator5.MoveNext())
							{
								CargoResourceData r2 = enumerator5.Current;
								(item2 as ICargo).ChangeQuantityBy(ccd2.ID, r2.ResourceType, ccd2.Capacity);
							}
						}
					}
				}
				if (!(parent is SpaceObjectVessel))
				{
					return;
				}
				foreach (ResourceContainer rc in (parent as SpaceObjectVessel).MainDistributionManager.GetResourceContainers())
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
					foreach (GeneratorCapacitor cap in from m in (parent as SpaceObjectVessel).MainDistributionManager.GetGenerators()
						where m is GeneratorCapacitor
						select m)
					{
						cap.Capacity = cap.MaxCapacity;
					}
					return;
				}
			}
			if (parts[0] == "spawn" && (parts.Length == 2 || parts.Length == 3))
			{
				Vector3D spawnItemPosition = player.LocalPosition + player.LocalRotation * Vector3D.Forward;
				if (parts[1].ToLower() == "corpse")
				{
					Corpse corpse = new Corpse(player);
					corpse.LocalPosition = player.LocalPosition + player.LocalRotation * Vector3D.Forward;
					NetworkController.Instance.SendToClientsSubscribedTo(new SpawnObjectsResponse
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
						DynamicObject.SpawnDynamicObject(dod.ItemType, (dod.DefaultAuxData as GenericItemData).SubType, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot, null, refill: true);
					}
					else if (dod.ItemType == ItemType.MachineryPart)
					{
						InventorySlot slot2 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, (dod.DefaultAuxData as MachineryPartData).PartType, parent, -1, spawnItemPosition, null, null, tier, slot2, null, refill: true);
					}
					else
					{
						InventorySlot slot3 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(dod.ItemType));
						DynamicObject.SpawnDynamicObject(dod.ItemType, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot3, null, refill: true);
					}
					return;
				}
				foreach (ItemType v in Enum.GetValues(typeof(ItemType)))
				{
					if (v.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot4 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(v));
						DynamicObject.SpawnDynamicObject(v, GenericItemSubType.None, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot4, null, refill: true);
						return;
					}
				}
				foreach (GenericItemSubType v4 in Enum.GetValues(typeof(GenericItemSubType)))
				{
					if (v4.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot5 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.GenericItem));
						DynamicObject.SpawnDynamicObject(ItemType.GenericItem, v4, MachineryPartType.None, parent, -1, spawnItemPosition, null, null, tier, slot5, null, refill: true);
						return;
					}
				}
				foreach (MachineryPartType v5 in Enum.GetValues(typeof(MachineryPartType)))
				{
					if (v5.ToString().ToLower().Contains(parts[1]))
					{
						InventorySlot slot6 = inventorySlots.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(ItemType.MachineryPart));
						DynamicObject.SpawnDynamicObject(ItemType.MachineryPart, GenericItemSubType.None, v5, parent, -1, spawnItemPosition, null, null, tier, slot6, null, refill: true);
						return;
					}
				}
				{
					foreach (GameScenes.SceneID v6 in Enum.GetValues(typeof(GameScenes.SceneID)))
					{
						if (v6.ToString().ToLower().Contains(parts[1]))
						{
							string tag = ((parts.Length == 3) ? parts[2] : "");
							if (GameScenes.Ranges.IsShip(v6))
							{
								tag = tag + ((tag == "" || tag.EndsWith(";")) ? "" : ";") + "_RescueVessel";
							}
							Vector3D offset;
							if (parent is SpaceObjectVessel)
							{
								SpaceObjectVessel vessel6 = parent as SpaceObjectVessel;
								offset = QuaternionD.LookRotation(vessel6.Forward, vessel6.Up) * (player.LocalPosition + player.LocalRotation * QuaternionD.Euler(0f - player.MouseLook, 0.0, 0.0) * Vector3D.Forward * 25.0);
							}
							else
							{
								offset = player.LocalPosition + player.LocalRotation * Vector3D.Forward * 50.0;
							}
							if (GameScenes.Ranges.IsAsteroid(v6))
							{
								Asteroid asteroid = Asteroid.CreateNewAsteroid(v6, "", -1L, new List<long> { (parent is SpaceObjectVessel) ? (parent as SpaceObjectVessel).MainVessel.GUID : parent.GUID }, null, offset * 10.0, null, null, tag, checkPosition: false);
								asteroid.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()).Normalized * 6.0;
							}
							else
							{
								Ship ship2 = Ship.CreateNewShip(v6, "", -1L, new List<long> { (parent is SpaceObjectVessel) ? (parent as SpaceObjectVessel).MainVessel.GUID : parent.GUID }, null, offset, null, null, tag, checkPosition: false);
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
				if (parent is SpaceObjectVessel && int.TryParse(parts[1], out var time2))
				{
					(parent as SpaceObjectVessel).SelfDestructTimer = new SelfDestructTimer(parent as SpaceObjectVessel, time2);
				}
				return;
			}
			if (parts[0] == "hitme" && parts.Length == 1)
			{
				double distance = 500.0;
				double velocity = 50.0;
				double radius2 = 10.0;
				Vector3D offset3 = ((!(parent is SpaceObjectVessel)) ? (player.LocalRotation * Vector3D.Forward * distance) : (QuaternionD.LookRotation((parent as SpaceObjectVessel).Forward, (parent as SpaceObjectVessel).Up) * player.LocalRotation * Vector3D.Forward * distance));
				Ship ship3 = Ship.CreateNewShip(GameScenes.SceneID.AltCorp_CorridorModule, "", -1L, new List<long> { parent.GUID }, null, offset3, null, null, "", checkPosition: false);
				Vector3D thrust2 = -offset3.Normalized * velocity;
				Vector3D offset4 = new Vector3D(MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5) * radius2 * 2.0;
				ship3.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()) * 50.0;
				ship3.Orbit.InitFromStateVectors(ship3.Orbit.Parent, ship3.Orbit.Position + offset4, ship3.Orbit.Velocity + thrust2, Instance.SolarSystem.CurrentTime, areValuesRelative: false);
				return;
			}
			if (parts[0] == "setmain" && parts.Length == 1 && parent is SpaceObjectVessel)
			{
				(parent as SpaceObjectVessel).SetMainVessel();
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
				if (parent is SpaceObjectVessel)
				{
					SpaceObjectVessel vessel7 = parent as SpaceObjectVessel;
					offset2 = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * (offset2 - vessel7.VesselData.CollidersCenterOffset.ToVector3D());
					direction = QuaternionD.LookRotation(vessel7.MainVessel.Forward, vessel7.MainVessel.Up) * direction;
					guid = vessel7.MainVessel.GUID;
				}
				Ship ship = Ship.CreateNewShip(GameScenes.SceneID.AltCorp_DockableContainer, "PHTORP MK4", -1L, new List<long> { guid }, null, offset2, null, null, "", checkPosition: false);
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
			if ((parts[0] == "countships" || parts[0] == "countitems") && (parts.Length == 1 || parts.Length == 2))
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
						foreach (Item item in from m in m_objects.Values
							where m is DynamicObject && (m as DynamicObject).Parent == ves && (m as DynamicObject).Item != null
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
					msg = msg + ((msg == "") ? "" : "\r\n") + kv.Key + ": " + kv.Value;
				}
				NetworkController.Instance.SendToGameClient(player.GUID, new ConsoleMessage
				{
					Text = msg
				});
				return;
			}
			if (parts[0] == "station" && parts.Length == 2)
			{
				List<SpaceObjectVessel> vessels = StationBlueprint.AssembleStation(parts[1], "JsonStation", "JsonStation", null, parent.GUID, 1f);
				return;
			}
			if (parts[0] == "collision" && parts.Length == 2)
			{
				if (!(player.Parent is SpaceObjectVessel))
				{
					return;
				}
				SpaceObjectVessel vessel5 = player.Parent as SpaceObjectVessel;
				vessel5.MainVessel.RigidBody.CollisionFlags = ((parts[1] == "0") ? CollisionFlags.NoContactResponse : CollisionFlags.None);
				{
					foreach (SpaceObjectVessel v3 in vessel5.MainVessel.AllDockedVessels)
					{
						v3.RigidBody.CollisionFlags = ((parts[1] == "0") ? CollisionFlags.NoContactResponse : CollisionFlags.None);
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
				NetworkController.Instance.SendToGameClient(player.GUID, new ConsoleMessage
				{
					Text = "God mode: " + (player.Stats.GodMode ? "ON" : "OFF")
				});
				return;
			}
			if (parts[0] == "teleport" && parts.Length == 2)
			{
				ArtificialBody target = null;
				Player p = m_players.Values.FirstOrDefault((Player m) => m.PlayerId == parts[1] || m.Name.ToLower() == parts[1].ToLower());
				if (p != null && p.Parent is ArtificialBody)
				{
					target = ((!(p.Parent is SpaceObjectVessel)) ? (p.Parent as ArtificialBody) : (p.Parent as SpaceObjectVessel).MainVessel);
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
				ArtificialBody myAB = ((parent is SpaceObjectVessel) ? (parent as SpaceObjectVessel).MainVessel : (parent as ArtificialBody));
				if (target != null && target != myAB)
				{
					myAB.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
					myAB.Orbit.CopyDataFrom(target.Orbit, SolarSystem.CurrentTime, exactCopy: true);
					if (myAB is Pivot)
					{
						myAB.Orbit.RelativePosition -= player.LocalPosition + player.LocalRotation * Vector3D.Forward * (target.Radius + 100.0);
						myAB.Orbit.RelativeVelocity -= player.LocalVelocity;
					}
					else
					{
						myAB.Orbit.RelativePosition -= myAB.Forward * (target.Radius + 100.0);
					}
					myAB.Orbit.InitFromCurrentStateVectors(SolarSystem.CurrentTime);
					myAB.Orbit.UpdateOrbit();
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
					if (!(parent is SpaceObjectVessel))
					{
						return;
					}
					SpaceObjectVessel vessel4 = parent as SpaceObjectVessel;
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
					if (!(ab is SpaceObjectVessel) || (!(ab as SpaceObjectVessel).IsInvulnerable && !ab.IsPartOfSpawnSystem && !(ab is Asteroid)))
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
						ab.MarkForDestruction = true;
						SpawnManager.SpawnedVessels.Remove(ab.GUID);
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
				foreach (Player pl in m_players.Values)
				{
					pl.Blueprints = ObjectCopier.DeepCopy(StaticData.DefaultBlueprints);
					NetworkController.Instance.SendToGameClient(pl.GUID, new UpdateBlueprintsMessage
					{
						Blueprints = pl.Blueprints
					});
				}
				return;
			}
			if (parts[0] == "blink" && parts.Length == 2)
			{
				double.TryParse(parts[1], out var dist);
				if (parent is SpaceObjectVessel)
				{
					SpaceObjectVessel vessel3 = parent as SpaceObjectVessel;
					vessel3.MainVessel.DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
					vessel3.MainVessel.Orbit.RelativePosition += vessel3.Forward * dist * 1000.0;
					vessel3.MainVessel.Orbit.InitFromCurrentStateVectors(SolarSystemTime);
					vessel3.MainVessel.Orbit.UpdateOrbit();
				}
			}
			else if (parts[0] == "whereami" && parts.Length == 1)
			{
				string msg2;
				if (parent is SpaceObjectVessel)
				{
					SpaceObjectVessel vessel = parent as SpaceObjectVessel;
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
					if (!(parent is Pivot))
					{
						return;
					}
					msg2 = "Near " + (parent as Pivot).Orbit.Parent.CelestialBody.ToString();
				}
				NetworkController.Instance.SendToGameClient(player.GUID, new ConsoleMessage
				{
					Text = msg2
				});
			}
			else
			{
				if (!(parts[0] == "restock") || parts.Length != 1 || !(parent is SpaceObjectVessel))
				{
					return;
				}
				List<SpaceObjectVessel> list = new List<SpaceObjectVessel>();
				list.Add((parent as SpaceObjectVessel).MainVessel);
				{
					foreach (SpaceObjectVessel vessel2 in list.Concat((parent as SpaceObjectVessel).MainVessel.AllDockedVessels))
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
							DynamicObject.SpawnDynamicObject(it, gt, mt, vessel2, ap.InSceneID, null, null, null, MathHelper.RandomRange(1, 5), null, null, refill: true);
							if (mt != 0)
							{
								vessel2.MainDistributionManager.GetVesselComponentByPartSlot(ap.Item.AttachPointID)?.FitPartToSlot(ap.Item.AttachPointID, (MachineryPart)ap.Item);
							}
						}
					}
					return;
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public TextChatMessage SendSystemMessage(SystemMessagesTypes type, Ship sh)
	{
		TextChatMessage tcm = new TextChatMessage();
		tcm.GUID = -1L;
		tcm.Name = "System";
		tcm.MessageType = type;
		tcm.MessageText = "";
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
			string timeLeftToRestart = "";
			timeLeftToRestart = ((!(timeToRestart <= 10.0)) ? (timeToRestart / 60.0).ToString() : timeToRestart.ToString());
			tcm.MessageParam = new string[2]
			{
				timeLeftToRestart,
				(timeToRestart <= 10.0) ? "seconds" : "minutes"
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
		Player pl = NetworkController.Instance.GetPlayer(data.Sender);
		if (pl == null)
		{
			Dbg.Error("Player spawn request error, player is null", p.Sender);
			return;
		}
		pl.MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		spawnSuccsessful = ((!pl.IsAlive) ? AddPlayerToShip(pl, p.SpawnSetupType, p.SpawPointParentID) : (pl.Parent != null && pl.Parent is ArtificialBody));
		PlayerSpawnResponse spawnResponse = new PlayerSpawnResponse();
		if (!spawnSuccsessful)
		{
			spawnResponse.Response = ResponseResult.Error;
		}
		else
		{
			if (pl.Parent is Pivot && (pl.Parent as Pivot).StabilizeToTargetObj != null)
			{
				(pl.Parent as Pivot).DisableStabilization(disableForChildren: false, updateBeforeDisable: true);
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

			ArtificialBody mainAb = ((mainVessel != null) ? mainVessel : parentObj);
			spawnResponse.ParentTransform = new ObjectTransform
			{
				GUID = mainAb.GUID,
				Type = mainAb.ObjectType,
				Forward = mainAb.Forward.ToFloatArray(),
				Up = mainAb.Up.ToFloatArray()
			};

			if (mainVessel != null && mainVessel is Ship)
			{
				Ship mainShip = mainVessel as Ship;
				spawnResponse.DockedVessels = mainShip.GetDockedVesselsData();
				spawnResponse.VesselData = mainShip.VesselData;
				spawnResponse.VesselObjects = mainShip.GetVesselObjects();
			}
			else if (mainVessel != null && mainVessel is Asteroid)
			{
				spawnResponse.VesselData = mainVessel.VesselData;
				spawnResponse.MiningPoints = (mainVessel as Asteroid).MiningPoints.Values.Select((AsteroidMiningPoint m) => m.GetDetails()).ToList();
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

			if (pl.CurrentSpawnPoint != null && ((pl.CurrentSpawnPoint.IsPlayerInSpawnPoint && pl.CurrentSpawnPoint.Ship == pl.Parent) || (pl.CurrentSpawnPoint.Type == SpawnPointType.SimpleSpawn && pl.CurrentSpawnPoint.Executer == null && !pl.IsAlive)))
			{
				spawnResponse.SpawnPointID = pl.CurrentSpawnPoint.SpawnPointID;
			}
			else
			{
				spawnResponse.CharacterTransform = new CharacterTransformData();
				spawnResponse.CharacterTransform.LocalPosition = pl.LocalPosition.ToFloatArray();
				spawnResponse.CharacterTransform.LocalRotation = pl.LocalRotation.ToFloatArray();
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
				spawnResponse.TimeUntilServerRestart = (restartTime - DateTime.UtcNow).TotalSeconds;
			}
			if (pl.Parent is ArtificialBody)
			{
				HashSet<GameScenes.SceneID> SceneIDs = new HashSet<GameScenes.SceneID>();
				List<SpaceObjectVessel> vessels = (from m in SolarSystem.GetArtificialBodieslsInRange(pl.Parent as ArtificialBody, 10000.0)
					where m is SpaceObjectVessel
					select m as SpaceObjectVessel).ToList();
				if (pl.Parent is SpaceObjectVessel)
				{
					vessels.Add(pl.Parent as SpaceObjectVessel);
				}
				foreach (SpaceObjectVessel ves in vessels)
				{
					SceneIDs.Add(ves.SceneID);
					foreach (SpaceObjectVessel dves in ves.AllDockedVessels)
					{
						SceneIDs.Add(dves.SceneID);
					}
				}
				spawnResponse.Scenes = SceneIDs.ToList();
			}
			spawnResponse.Quests = pl.Quests.Select((Quest m) => m.GetDetails()).ToList();
			spawnResponse.Blueprints = pl.Blueprints;
			spawnResponse.NavMapDetails = pl.NavMapDetails;
		}
		NetworkController.Instance.SendToGameClient(p.Sender, spawnResponse);
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
			if (obj != null && (!(obj is SpaceObjectVessel) || (obj as SpaceObjectVessel).IsMainVessel))
			{
				res.Data.Add(obj.GetSpawnResponseData(pl));
			}
		}
		NetworkController.Instance.SendToGameClient(req.Sender, res);
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
			if (dos.Parent is SpaceObjectVessel && dos.Parent.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item != null && m.Item.AttachPointID != null && dos.APDetails != null && m.Item.AttachPointID.InSceneID == dos.APDetails.InSceneID) != null)
			{
				dos.Timer = dos.RespawnTime;
				continue;
			}
			DynamicObjectsRespawnList.Remove(dos);
			if (m_vessels.ContainsKey(dos.Parent.GUID))
			{
				DynamicObject dobj = new DynamicObject(dos.Data, dos.Parent, -1L);
				if (dos.Data.AttachPointInSceneId > 0 && dobj.Item != null)
				{
					dobj.Item.SetAttachPoint(dos.APDetails);
				}
				dobj.APDetails = dos.APDetails;
				dobj.RespawnTime = ((dos.Data.SpawnSettings.Length != 0) ? dos.Data.SpawnSettings[0].RespawnTime : (-1f));
				if (dobj.Item != null && dobj.Item != null && dos.MaxHealth >= 0f && dos.MinHealth >= 0f)
				{
					IDamageable idmg = dobj.Item;
					idmg.Health = (int)(idmg.MaxHealth * MathHelper.Clamp(MathHelper.RandomRange(dos.MinHealth, dos.MaxHealth), 0f, 1f));
				}
				SpawnObjectsResponse res = new SpawnObjectsResponse();
				res.Data.Add(dobj.GetSpawnResponseData(null));
				NetworkController.Instance.SendToClientsSubscribedTo(res, -1L, dos.Parent);
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
			NetworkController.Instance.SendToAllClients(new UpdateVesselDataMessage
			{
				VesselsDataUpdate = VesselsDataUpdate.Values.ToList()
			}, -1L);
			VesselsDataUpdate = new ConcurrentDictionary<long, VesselDataUpdate>();
		}
	}

	public void RemoveWorldObjects()
	{
		Dbg.Info("REMOVING ALL WORLD OBJECTS");
		try
		{
			long[] array = m_players.Keys.ToArray();
			foreach (long guid in array)
			{
				NetworkController.Instance.DisconnectClient(guid);
			}
			NetworkController.Instance.DisconnectAllClients();
		}
		catch (Exception)
		{
		}
		m_players.Clear();
		m_objects.Clear();
		ArtificialBody[] artificialBodies = Instance.SolarSystem.GetArtificialBodies();
		foreach (ArtificialBody ab in artificialBodies)
		{
			if (ab is Ship)
			{
				(ab as Ship).Destroy();
			}
			else if (ab is Asteroid)
			{
				(ab as Asteroid).Destroy();
			}
			else
			{
				Instance.SolarSystem.RemoveArtificialBody(ab);
			}
		}
		m_vessels.Clear();
		WorldInitialized = false;
	}

	public void DestroyArtificialBody(ArtificialBody ab, bool destroyChildren = true, bool vesselExploded = false)
	{
		if (ab == null)
		{
			return;
		}
		if (ab is SpaceObjectVessel)
		{
			SpaceObjectVessel ves = ab as SpaceObjectVessel;
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
					Dbg.Exception(ex);
				}
			}
			if (vesselExploded)
			{
				ves.DamageVesselsInExplosionRadius();
				NetworkController.Instance.SendToClientsSubscribedTo(new DestroyVesselMessage
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
			if (!(vessel is Ship) || !(vessel.Health < float.Epsilon))
			{
				continue;
			}
			if (vessel.DockedToVessel == null && vessel.DockedVessels.Count == 0)
			{
				if (destroyVessels == null)
				{
					destroyVessels = new HashSet<SpaceObjectVessel>();
				}
				destroyVessels.Add((vessel.DockedToMainVessel != null) ? vessel.DockedToMainVessel : vessel);
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
		foreach (DynamicObject dobj in m_updateableDynamicObjects.Values)
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
		Dbg.Info("Server stats, objects", m_objects.Count, "players", m_players.Count, "vessels", m_vessels.Count, "artificial bodies", SolarSystem.ArtificialBodiesCount);
	}

	public void MainLoop()
	{
		SolarSystem.InitializeData();
		InitializeDebrisFields();
		if (CleanStart || PersistenceSaveInterval < 0.0 || (CleanStart = !Persistence.Load(LoadPersistenceFromFile)))
		{
			if ((double)solarSystemStartTime < 0.0)
			{
				SolarSystem.CalculatePositionsAfterTime(MathHelper.RandomRange(86400.0, 5256000.0));
			}
			else
			{
				SolarSystem.CalculatePositionsAfterTime(solarSystemStartTime);
			}
			InitializeWorld();
		}
		else
		{
			WorldInitialized = true;
		}
		Start();
		NetworkController.Instance.Start();
		tickMilliseconds = System.Math.Floor(1000.0 / (double)numberOfTicks);
		lastTime = DateTime.UtcNow;
#if !HELLION_SP
		if (ServerRestartTimeSec > 0.0)
		{
			restartTime = DateTime.UtcNow.AddSeconds(ServerRestartTimeSec);
			SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, ServerAutoRestartTimer);
		}
#endif
		DoomedShipController.SubscribeToTimer();
		bool hadSleep = true;
		DateTime lastServerTickedWithoutSleepTime = DateTime.MinValue;
		if (printDebugObjects)
		{
			SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_hr, PrintObjectsDebug);
		}
		SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, UpdateShipSystemsTimer);
		mainLoopStarted = true;

#if HELLION_SP
		// Don't change. This gives the single player game the required connection info.
		Console.WriteLine("ports:" + GamePort + "," + StatusPort);
		Console.WriteLine("ready");
#endif

		Dbg.Log("Starting main game loop.");
#if !HELLION_SP
		Task.Run(delegate
		{
			MainLoopWatcher();
		});
#endif
		while (IsRunning)
		{
			try
			{
				DateTime currentTime = DateTime.UtcNow;
				TimeSpan span = currentTime - lastTime;

				// Actual main loop. Functionaly go here.
				if (span.TotalMilliseconds >= tickMilliseconds)
				{
					NetworkController.Instance.Tick();

					AddRemovePlayers();
					if (printDebugObjects && !hadSleep && (currentTime - lastServerTickedWithoutSleepTime).TotalSeconds > 60.0)
					{
						Dbg.Info("Server ticked without sleep, time span ms", span.TotalMilliseconds, "tick ms", tickMilliseconds, "objects", m_objects.Count, "players", m_players.Count, "vessels", m_vessels.Count, "artificial bodies", SolarSystem.ArtificialBodiesCount);
						lastServerTickedWithoutSleepTime = currentTime;
					}
					hadSleep = false;
					DeltaTime = span.TotalSeconds;
					UpdateData(span.TotalSeconds);
					lastTime = currentTime;
					foreach (UpdateTimer timer2 in m_timers)
					{
						timer2.AddTime(DeltaTime);
					}
					if (m_timersToRemove.Count > 0)
					{
						foreach (UpdateTimer timer in m_timersToRemove)
						{
							if (timer.OnTick == null)
							{
								m_timers.Remove(timer);
							}
						}
						m_timersToRemove.Clear();
					}
					SpawnManager.UpdateTimers(DeltaTime);
					if (PersistenceSaveInterval > 0.0 || manualSave)
					{
						persistenceSaveTimer += span.TotalSeconds;
						if (persistenceSaveTimer >= PersistenceSaveInterval || manualSave)
						{
							persistenceSaveTimer = 0.0;
							Persistence.Save(manualSaveFileName, manualSaveAuxData);
						}
						manualSave = false;
						manualSaveFileName = null;
						manualSaveAuxData = null;
					}
				}
				else
				{
					hadSleep = true;
					Thread.Sleep((int)(tickMilliseconds - span.TotalMilliseconds));
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}

#if HELLION_SP
			// Stop server if player has closed the game.
			try
			{
				if (ParentProcess.GetParentProcess().HasExited)
				{
					IsRunning = false;
					SavePersistenceDataOnShutdown = false;
					Restart = false;
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}
#endif
		}

		// Shutting down...
		Dbg.Log("Main game loop ended; Shutting down server...");
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
		}
		if (Restart)
		{
			RestartServer(CleanRestart);
		}
	}

	private void MainLoopWatcher()
	{
		double lastSolarSystemTime = SolarSystemTime;
		bool logEvent = true;
		while (IsRunning)
		{
			if (SolarSystemTime - lastSolarSystemTime > 5.0)
			{
				if (logEvent)
				{
					Dbg.Warning("Main loop stuck for more than 5 sec.");
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
		foreach (Player player2 in m_playersToAdd)
		{
			m_players[player2.GUID] = player2;
			m_objects[player2.FakeGuid] = player2;
		}
		m_playersToAdd = new ConcurrentBag<Player>();
		foreach (Player player in m_playersToRemove)
		{
			m_players.Remove(player.GUID);
			m_objects.Remove(player.FakeGuid);
		}
		m_playersToRemove = new ConcurrentBag<Player>();
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
			Dbg.Exception(ex);
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
		ship.SceneTriggerExecuters.Find((SceneTriggerExecuter m) => m.InSceneID == inSceneId)?.ChangeState(0L, new SceneTriggerExecuterDetails
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
				NetworkController.Instance.SendToGameClient(req.Sender, so.GetInitializeMessage());
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
			if (m_players.ContainsKey(req.Sender))
			{
				m_players[req.Sender].UnsubscribeFrom(so);
			}
		}
	}

	public void SubscribeToTimer(UpdateTimer.TimerStep step, UpdateTimer.TimeStepDelegate del)
	{
		UpdateTimer timer = m_timers.Find((UpdateTimer x) => x.Step == step);
		if (timer == null)
		{
			timer = new UpdateTimer(step);
			m_timers.Add(timer);
		}
		UpdateTimer updateTimer = timer;
		updateTimer.OnTick = (UpdateTimer.TimeStepDelegate)Delegate.Combine(updateTimer.OnTick, del);
	}

	public void UnsubscribeFromTimer(UpdateTimer.TimerStep step, UpdateTimer.TimeStepDelegate del)
	{
		UpdateTimer timer = m_timers.Find((UpdateTimer x) => x.Step == step);
		if (timer != null)
		{
			timer.OnTick = (UpdateTimer.TimeStepDelegate)Delegate.Remove(timer.OnTick, del);
			if (timer.OnTick == null)
			{
				m_timersToRemove.Add(timer);
			}
		}
	}

	public void CheckInResponseListener(CheckInResponse data)
	{
		if (data.Result == ResponseResult.Success)
		{
			if (NetworkController.ServerID != data.ServerId)
			{
				NetworkController.ServerID = data.ServerId;
				Console.Title = ServerName + " (id: " + ((NetworkController.ServerID == null) ? "Not yet assigned" : string.Concat(NetworkController.ServerID)) + ")";
				Dbg.UnformattedMessage("==============================================================================\r\n\tServer ID: " + NetworkController.ServerID + "\r\n==============================================================================\r\n");
				try
				{
					File.WriteAllText(ConfigDir + "ServerID.txt", string.Concat(NetworkController.ServerID));
				}
				catch
				{
				}
			}
			CheckInPassed = true;
			AdminIPAddressRanges = data.AdminIPAddressRanges;
#if !HELLION_SP
			SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_hr, SendCheckInMessage);
#endif
		}
		else
		{
			IsRunning = false;
			Dbg.Exception(new Exception(data.Result.ToString()));
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
			foreach (Player pl in NetworkController.Instance.GetAllPlayers())
			{
				if (!pl.PlayerId.IsNullOrEmpty())
				{
					res.PlayersOnServer.Add(new PlayerOnServerData
					{
						PlayerNativeId = pl.NativeId,
						PlayerId = pl.PlayerId,
						Name = pl.Name,
						AlreadyHasInvite = SpawnPointInvites.ContainsKey(pl.PlayerId)
					});
				}
				if (pl.PlayerId.IsNullOrEmpty())
				{
					Dbg.Error("Player ID is null or empty", pl.GUID, pl.Name);
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
			foreach (Player pl in NetworkController.Instance.GetAllPlayers())
			{
				if (!pl.PlayerId.IsNullOrEmpty())
				{
					res.PlayersOnServer.Add(new PlayerOnServerData
					{
						PlayerNativeId = pl.NativeId,
						PlayerId = pl.PlayerId,
						Name = pl.Name,
						AlreadyHasInvite = false
					});
				}
				if (pl.PlayerId.IsNullOrEmpty())
				{
					Dbg.Error("Player ID is null or empty", pl.GUID, pl.Name);
				}
			}
		}

		NetworkController.Instance.SendToGameClient(req.Sender, res);
	}

	public void AvailableSpawnPointsRequestListener(NetworkData data)
	{
		AvailableSpawnPointsRequest req = data as AvailableSpawnPointsRequest;
		Player pl = GetPlayer(req.Sender);
		if (pl != null)
		{
			NetworkController.Instance.SendToGameClient(req.Sender, new AvailableSpawnPointsResponse
			{
				SpawnPoints = GetAvailableSpawnPoints(pl)
			});
		}
	}

	public void SaveGameMessageListener(NetworkData data)
	{
#if HELLION_SP
		manualSaveAuxData = (data as SaveGameMessage).AuxData;
		manualSaveFileName = (data as SaveGameMessage).FileName;
		manualSave = true;
#endif
	}

	public void ServerShutDownMessageListener(NetworkData data)
	{
#if HELLION_SP
		ServerShutDownMessage msg = data as ServerShutDownMessage;
		Restart = msg.Restrat;
		CleanRestart = msg.CleanRestart;
		SavePersistenceDataOnShutdown = false;
		IsRunning = false;
#endif
	}

	private void NameTagMessageListener(NetworkData data)
	{
		NameTagMessage msg = data as NameTagMessage;
		SpaceObjectVessel vessel = GetVessel(msg.ID.VesselGUID);
		NetworkController.Instance.SendToClientsSubscribedTo(data, -1L, vessel);
		try
		{
			vessel.NameTags.Find((NameTagData m) => m.InSceneID == msg.ID.InSceneID).NameTagText = msg.NameTagText;
		}
		catch
		{
		}
	}

	public void SendCheckInMessage(double amount)
	{
		MSConnection.Send(new CheckInMessage
		{
			ServerId = NetworkController.ServerID
		});
	}

	public bool IsAddressAutorized(string address)
	{
		try
		{
			if (address == "127.0.0.1" || address == "localhost")
			{
				return true;
			}
			byte[] bytes = System.Net.IPAddress.Parse(address).GetAddressBytes();
			Array.Reverse(bytes);
			uint addr = BitConverter.ToUInt32(bytes, 0);
			IpAddressRange[] adminIPAddressRanges = AdminIPAddressRanges;
			foreach (IpAddressRange range in adminIPAddressRanges)
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
		if (SpawnPointInvites.Count == 0)
		{
			return;
		}
		List<string> removedKeys = null;
		foreach (KeyValuePair<string, SpawnPointInviteData> item in SpawnPointInvites)
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
		if (removedKeys == null || removedKeys.Count <= 0)
		{
			return;
		}
		foreach (string key in removedKeys)
		{
			ClearSpawnPointInvitation(key);
		}
	}

	public void ClearSpawnPointInvitation(string steamID)
	{
		if (SpawnPointInvites.ContainsKey(steamID))
		{
			SpawnPointInvites[steamID].SpawnPoint.SetInvitation("", "", sendMessage: true);
			SpawnPointInvites.Remove(steamID);
		}
	}

	public void CreateSpawnPointInvitation(ShipSpawnPoint sp, string steamID, string playerName)
	{
		SpawnPointInvites.Add(steamID, new SpawnPointInviteData
		{
			SpawnPoint = sp,
			InviteTimer = SpawnPointInviteTimer
		});
		sp.SetInvitation(steamID, playerName, sendMessage: true);
	}

	public bool PlayerInviteChanged(ShipSpawnPoint sp, string invitedPlayerSteamID, string invitedPlayerName, Player sender)
	{
		if (!invitedPlayerSteamID.IsNullOrEmpty())
		{
			if (SpawnPointInvites.ContainsKey(invitedPlayerSteamID) && SpawnPointInvites[invitedPlayerSteamID].SpawnPoint == sp && sp.InvitedPlayerSteamID == invitedPlayerSteamID)
			{
				return false;
			}
			if (SpawnPointInvites.ContainsKey(invitedPlayerSteamID))
			{
				ClearSpawnPointInvitation(invitedPlayerSteamID);
			}
			if (!sp.InvitedPlayerSteamID.IsNullOrEmpty() && SpawnPointInvites.ContainsKey(sp.InvitedPlayerSteamID))
			{
				ClearSpawnPointInvitation(sp.InvitedPlayerSteamID);
			}
			CreateSpawnPointInvitation(sp, invitedPlayerSteamID, invitedPlayerName);
			return true;
		}
		if (!sp.InvitedPlayerSteamID.IsNullOrEmpty())
		{
			if (SpawnPointInvites.ContainsKey(sp.InvitedPlayerSteamID))
			{
				ClearSpawnPointInvitation(sp.InvitedPlayerSteamID);
			}
			else
			{
				sp.SetInvitation("", "", sendMessage: true);
			}
			return true;
		}
		return false;
	}

#if HELLION_SP
	public Asteroid GetRandomAsteroid()
	{
		List<Asteroid> asteroids = new List<Asteroid>();
		foreach (KeyValuePair<long, SpaceObjectVessel> item in m_vessels)
		{
			if (item.Value is Asteroid)
			{
				asteroids.Add(item.Value as Asteroid);
			}
		}
		if (asteroids.Count > 0)
		{
			return asteroids[MathHelper.RandomRange(0, asteroids.Count)];
		}
		return Asteroid.CreateNewAsteroid(GameScenes.SceneID.Asteroid01, "", -1L, null, null, null, null, null, "StartingScene;");
	}

#endif
	private void ServerAutoRestartTimer(double time)
	{
		DateTime currentTime = DateTime.UtcNow;
		if (currentTime.AddSeconds(timeToRestart) > restartTime)
		{
			if ((restartTime - currentTime).TotalSeconds >= timeToRestart - 2.0)
			{
				NetworkController.Instance.SendToAllClients(SendSystemMessage(SystemMessagesTypes.RestartServerTime, null), -1L);
			}
			if (timeToRestart == 1800.0)
			{
				timeToRestart = 900.0;
			}
			else if (timeToRestart == 900.0)
			{
				timeToRestart = 300.0;
			}
			else if (timeToRestart == 300.0)
			{
				timeToRestart = 60.0;
			}
			else if (timeToRestart == 60.0)
			{
				timeToRestart = 10.0;
			}
			else if (timeToRestart <= 10.0)
			{
				timeToRestart -= 1.0;
			}
			else
			{
				timeToRestart -= 300.0;
			}
		}
		if (currentTime > restartTime)
		{
			IsRunning = false;
			Restart = true;
			SavePersistenceDataOnShutdown = true;
			restartTime = currentTime.AddSeconds(ServerRestartTimeSec);
		}
	}

	private void UpdateShipSystemsTimer(double time)
	{
		if (updatingShipSystems)
		{
			return;
		}
		Task.Run(delegate
		{
			try
			{
				updatingShipSystems = true;
				List<SpaceObjectVessel> list = new List<SpaceObjectVessel>(m_vessels.Values);
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
			updatingShipSystems = false;
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
			string dir = ((!ConfigDir.IsNullOrEmpty() && Directory.Exists(ConfigDir + "Data")) ? ConfigDir : "");
			List<DebrisField.DebrisFieldData> list = Json.Load<List<DebrisField.DebrisFieldData>>(dir + "Data/DebrisFields.json");
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
			Dbg.Exception(ex);
		}
	}

	public ServerStatusResponse GetServerStatusResponse(ServerStatusRequest req)
	{
		return new ServerStatusResponse
		{
			Response = ResponseResult.Success,
			Description = (req.SendDetails ? Description : null),
			MaxPlayers = (short)MaxPlayers,
			CurrentPlayers = NetworkController.Instance.CurrentOnlinePlayers(),
			AlivePlayers = (short)m_players.Values.Count((Player m) => m.IsAlive),
			CharacterData = GetPlayerFromPlayerId(req.PlayerId)?.GetCharacterData()
		};
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
			long[] affectedGUIDs = em.AffectedGUIDs;
			foreach (long affectedGuid in affectedGUIDs)
			{
				SpaceObject tmpSp = Instance.GetObject(affectedGuid);
				if ((tmpSp.Position - item.DynamicObj.Position).Magnitude < (double)(item.ExplosionRadius * 1.5f) && affectedObjects.Add(tmpSp.GUID))
				{
					if (tmpSp is Player)
					{
						(tmpSp as Player).Stats.TakeDamage(HurtType.Explosion, item.ExplosionDamage);
					}
					if (tmpSp is DynamicObject && (tmpSp as DynamicObject).Item != null)
					{
						(tmpSp as DynamicObject).Item.TakeDamage(new Dictionary<TypeOfDamage, float> { { item.ExplosionDamageType, item.ExplosionDamage } });
					}
				}
			}
		}
		if (item.ExplosionDamageType == TypeOfDamage.Impact && item.DynamicObj.Parent is SpaceObjectVessel && affectedObjects.Add(item.DynamicObj.Parent.GUID))
		{
			List<VesselRepairPoint> list = null;
			SpaceObjectVessel parentVessel = item.DynamicObj.Parent as SpaceObjectVessel;
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
					ShipOne = item.DynamicObj.Parent.GUID,
					ShipTwo = -1L
				};
				NetworkController.Instance.SendToClientsSubscribedTo(scm, -1L, item.DynamicObj.Parent);
			}
		}
		Extensions.Invoke(delegate
		{
			item.DestroyItem();
		}, 1.0);
	}
}
