using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenHellion.IO;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;
using ZeroGravity.Spawn;

namespace ZeroGravity;

public class Persistence
{
	private const string PersistanceFileName = "hellion_{0}.save";

	private const int CurrentSaveType = 1;

	private static void SaveVesselPersistence(ref PersistenceObject per, SpaceObjectVessel ves)
	{
		if (ves.IsDebrisFragment)
		{
			return;
		}
		if (ves.ObjectType == SpaceObjectType.Ship)
		{
			per.Ships.Add((ves as IPersistantObject).GetPersistenceData());
		}
		else if (ves.ObjectType == SpaceObjectType.Asteroid)
		{
			per.Asteroids.Add((ves as IPersistantObject).GetPersistenceData());
		}
		if (ves.DockedVessels is { Count: > 0 })
		{
			foreach (SpaceObjectVessel child2 in ves.DockedVessels)
			{
				SaveVesselPersistence(ref per, child2);
			}
		}
		if (ves.StabilizedToTargetChildren is not { Count: > 0 })
		{
			return;
		}
		foreach (SpaceObjectVessel child in ves.StabilizedToTargetChildren)
		{
			if (!child.IsDocked)
			{
				SaveVesselPersistence(ref per, child);
			}
		}
	}

	private static void SaveRespawnObjectPersistence(ref PersistenceObject per, Server.DynamicObjectsRespawn obj)
	{
		PersistenceObjectDataRespawnObject data = new PersistenceObjectDataRespawnObject
		{
			ItemID = obj.Data.ItemID,
			ParentGUID = obj.Parent.Guid,
			ParentType = obj.Parent.ObjectType,
			Position = obj.Data.Position,
			Forward = obj.Data.Forward,
			Up = obj.Data.Up,
			AuxData = obj.Data.AuxData,
			RespawnTime = obj.Data.SpawnSettings.Length != 0 ? obj.Data.SpawnSettings[0].RespawnTime : -1f,
			Timer = obj.Timer
		};
		if (obj.ApDetails != null)
		{
			data.AttachPointID = obj.ApDetails.InSceneID;
		}
		per.RespawnObjects.Add(data);
	}

	private static void SaveSpawnPointPeristence(ref PersistenceObject per, ShipSpawnPoint sp)
	{
		PersistenceObjectDataSpawnPoint data = new PersistenceObjectDataSpawnPoint
		{
			GUID = sp.Ship.Guid,
			SpawnID = sp.SpawnPointID,
			SpawnType = sp.Type,
			SpawnState = sp.State
		};
		if (sp.Player != null)
		{
			data.PlayerGUID = sp.Player.Guid;
			data.IsPlayerInSpawnPoint = sp.IsPlayerInSpawnPoint;
		}
		per.SpawnPoints.Add(data);
	}

	public static void Save(string filename = null, SaveFileAuxData auxData = null)
	{
		PersistenceObject per = new PersistenceObject
		{
			SaveType = CurrentSaveType,
			SolarSystemTime = Server.Instance.SolarSystem.CurrentTime,
			AuxData = auxData,
			Ships = new HashSet<PersistenceObjectData>(),
			Asteroids = new HashSet<PersistenceObjectData>(),
			Players = new HashSet<PersistenceObjectData>(),
			RespawnObjects = new HashSet<PersistenceObjectData>(),
			SpawnPoints = new HashSet<PersistenceObjectData>(),
			ArenaControllers = new HashSet<PersistenceObjectData>()
		};

		foreach (SpaceObjectVessel ves in Server.Instance.AllVessels)
		{
			if (!ves.IsDocked && ves.StabilizeToTargetObj == null && ves.ObjectType is SpaceObjectType.Ship or SpaceObjectType.Asteroid)
			{
				SaveVesselPersistence(ref per, ves);
			}
		}

		foreach (Player pl in Server.Instance.AllPlayers)
		{
			if (pl != null)
			{
				per.Players.Add(pl.GetPersistenceData());
			}
		}

		foreach (Server.DynamicObjectsRespawn obj in Server.Instance.DynamicObjectsRespawnList)
		{
			SaveRespawnObjectPersistence(ref per, obj);
		}

		foreach (SpaceObjectVessel ves2 in Server.Instance.AllVessels)
		{
			if (ves2.SpawnPoints is not { Count: > 0 })
			{
				continue;
			}
			foreach (ShipSpawnPoint sp in ves2.SpawnPoints)
			{
				SaveSpawnPointPeristence(ref per, sp);
			}
		}

		foreach (DeathMatchArenaController dmac in Server.Instance.DeathMatchArenaControllers)
		{
			per.ArenaControllers.Add(dmac.GetPersistenceData());
		}
		per.DoomControllerData = Server.Instance.DoomedShipController.GetPersistenceData();
		per.SpawnManagerData = SpawnManager.GetPersistenceData();
		DirectoryInfo d = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), Server.ConfigDir));
		FileInfo[] Files = d.GetFiles("*.save");
		if (Files.Length >= Server.MaxNumberOfSaveFiles)
		{
			Array.Sort(Files, (FileInfo file1, FileInfo file2) => file2.CreationTimeUtc.CompareTo(file1.CreationTimeUtc));
			for (int i = Server.MaxNumberOfSaveFiles - 1; i < Files.Length; i++)
			{
				Files[i].Delete();
			}
		}

		if (filename == null)
		{
			filename = string.Format(PersistanceFileName, DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss"));
		}

		// TODO: FIX
		// JsonSerialiser.SerializeToFile(per, Path.Combine(Server.ConfigDir, filename), JsonSerialiser.Formatting.None);
		Debug.Log("Saved world...");
	}

	private static void LoadRespawnObjectPersistence(PersistenceObjectDataRespawnObject data)
	{
		SpaceObject parentObj = Server.Instance.GetObject(data.ParentGUID);
		if (parentObj == null)
		{
			Debug.LogError("Could not find parent object for respawn object persistence", data.ItemID);
			return;
		}
		AttachPointDetails apd = null;
		if (data.AttachPointID is > 0)
		{
			apd = new AttachPointDetails
			{
				InSceneID = data.AttachPointID.Value
			};
		}
		DynamicObjectSceneData dynamicObjectSceneData = new DynamicObjectSceneData
		{
			ItemID = data.ItemID,
			Position = data.Position,
			Forward = data.Forward,
			Up = data.Up,
			AttachPointInSceneId = data.AttachPointID.HasValue ? data.AttachPointID.Value : -1,
			AuxData = data.AuxData,
			SpawnSettings = new DynaminObjectSpawnSettings[1]
			{
				new DynaminObjectSpawnSettings
				{
					RespawnTime = data.RespawnTime,
					SpawnChance = -1f,
					Tag = ""
				}
			}
		};
		DynamicObjectSceneData sceneData = dynamicObjectSceneData;
		Server.Instance.DynamicObjectsRespawnList.Add(new Server.DynamicObjectsRespawn
		{
			Data = sceneData,
			Parent = parentObj,
			Timer = data.Timer,
			RespawnTime = data.RespawnTime,
			ApDetails = apd
		});
	}

	private static void LoadSpawnPointPeristence(PersistenceObjectDataSpawnPoint data)
	{
		try
		{
			Ship sh = Server.Instance.GetVessel(data.GUID) as Ship;
			ShipSpawnPoint sp = sh.SpawnPoints.Find((ShipSpawnPoint m) => m.SpawnPointID == data.SpawnID);
			sp.State = data.SpawnState;
			sp.Type = data.SpawnType;
			if (data.PlayerGUID.HasValue)
			{
				sp.Player = Server.Instance.GetPlayer(data.PlayerGUID.Value);
				sp.IsPlayerInSpawnPoint = data.IsPlayerInSpawnPoint.Value;
				sp.Player.IsInsideSpawnPoint = sp.IsPlayerInSpawnPoint;
				if (sp.IsPlayerInSpawnPoint || sp.State == SpawnPointState.Authorized)
				{
					sp.Player.SetSpawnPoint(sp);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogError("Failed to load spawn point from persistence", ex.Message, ex.StackTrace);
		}
	}

	public static async Task<bool> Load(string filename = null)
	{
		DirectoryInfo d = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), Server.ConfigDir));
		FileInfo[] files = d.GetFiles("*.save");
		FileInfo loadFromFile = null;
		loadFromFile = filename == null ? files.OrderByDescending((FileInfo m) => m.LastWriteTimeUtc).FirstOrDefault() : files.FirstOrDefault((FileInfo m) => m.Name.ToLower() == filename.ToLower());
		if (loadFromFile != null)
		{
			PersistenceObject persistence = JsonSerialiser.Load<PersistenceObject>(loadFromFile.FullName);
			Server.Instance.SolarSystem.CalculatePositionsAfterTime(persistence.SolarSystemTime);
			if (persistence.Asteroids != null)
			{
				await Parallel.ForEachAsync(persistence.Asteroids, async (asteroidData, ct) =>
				{
					Asteroid ast = new Asteroid(asteroidData.GUID, initializeOrbit: false, Vector3D.Zero, Vector3D.One, Vector3D.Forward, Vector3D.Up);
					await ast.LoadPersistenceData(asteroidData);
				});
			}
			if (persistence.Ships != null)
			{
				await Parallel.ForEachAsync(persistence.Ships, async (shipData, ct) =>
				{
					Ship sh = new Ship(shipData.GUID, initializeOrbit: false, Vector3D.Zero, Vector3D.One, Vector3D.Forward, Vector3D.Up);
					await sh.LoadPersistenceData(shipData);
				});
			}
			if (persistence.Players != null)
			{
				await Parallel.ForEachAsync(persistence.Players, async (data, ct) =>
				{
					var playerData = data as PersistenceObjectDataPlayer;
					Player player = await Player.CreatePlayerAsync(playerData.GUID, Vector3D.Zero, QuaternionD.Identity, "PersistenceLoad", "", playerData.Gender, playerData.HeadType, playerData.HairType, addToServerList: false);
					await player.LoadPersistenceData(playerData);
				});
			}
			if (persistence.RespawnObjects != null)
			{
				foreach (PersistenceObjectDataRespawnObject respawnObjectData in persistence.RespawnObjects.Cast<PersistenceObjectDataRespawnObject>())
				{
					LoadRespawnObjectPersistence(respawnObjectData);
				}
			}
			if (persistence.SpawnPoints != null)
			{
				foreach (PersistenceObjectDataSpawnPoint spawnPointData in persistence.SpawnPoints.Cast<PersistenceObjectDataSpawnPoint>())
				{
					LoadSpawnPointPeristence(spawnPointData);
				}
			}
			if (persistence.ArenaControllers != null)
			{
				await Parallel.ForEachAsync(persistence.ArenaControllers, async (data, ct) =>
				{
					var arenaControllerData = data as PersistenceArenaControllerData;
					DeathMatchArenaController arenaController = new DeathMatchArenaController();
					await arenaController.LoadPersistenceData(arenaControllerData);
				});
			}
			if (persistence.DoomControllerData != null)
			{
				await Server.Instance.DoomedShipController.LoadPersistenceData(persistence.DoomControllerData);
			}
			await SpawnManager.LoadPersistenceData(persistence.SpawnManagerData);
			Debug.LogFormat("Loaded save with name {0}.", loadFromFile.Name);
			return true;
		}
		return false;
	}

	public static async Task<DynamicObject> CreateDynamicObject(PersistenceObjectDataDynamicObject persistenceData, SpaceObject parent, StructureSceneData structureSceneData = null)
	{
		if (persistenceData == null)
		{
			return null;
		}
		int apInSceneID = -1;
		DynamicObjectAuxData auxData = null;
		PersistenceObjectDataItem data = persistenceData as PersistenceObjectDataItem;
		if (data.AttachPointID.HasValue)
		{
			apInSceneID = data.AttachPointID.Value;
		}
		if (structureSceneData is { DynamicObjects: not null })
		{
			DynamicObjectSceneData dobjSceneData = structureSceneData.DynamicObjects.Find((DynamicObjectSceneData m) => m.ItemID == data.ItemID);
			if (dobjSceneData != null)
			{
				auxData = ObjectCopier.DeepCopy(dobjSceneData.AuxData);
			}
		}
		if (auxData == null && StaticData.DynamicObjectsDataList.TryGetValue(data.ItemID, out var dod))
		{
			auxData = ObjectCopier.DeepCopy(dod.DefaultAuxData);
		}
		DynamicObjectSceneData dynamicObjectSceneData = new DynamicObjectSceneData
		{
			ItemID = persistenceData.ItemID,
			Position = persistenceData.RespawnTime.HasValue ? persistenceData.RespawnPosition : persistenceData.LocalPosition,
			Forward = persistenceData.RespawnTime.HasValue ? persistenceData.RespawnForward : (persistenceData.LocalRotation.ToQuaternionD() * Vector3D.Forward).ToFloatArray(),
			Up = persistenceData.RespawnTime.HasValue ? persistenceData.RespawnUp : (persistenceData.LocalRotation.ToQuaternionD() * Vector3D.Up).ToFloatArray(),
			AttachPointInSceneId = apInSceneID,
			AuxData = persistenceData.RespawnAuxData != null ? persistenceData.RespawnAuxData : auxData,
			SpawnSettings = !persistenceData.RespawnTime.HasValue ? null : new DynaminObjectSpawnSettings[1]
			{
				new DynaminObjectSpawnSettings
				{
					RespawnTime = persistenceData.RespawnTime.Value,
					SpawnChance = -1f,
					Tag = ""
				}
			}
		};
		DynamicObjectSceneData sceneData = dynamicObjectSceneData;
		if (persistenceData is PersistenceObjectDataItem item)
		{
			sceneData.AuxData.Tier = item.Tier;
		}
		DynamicObject dobj = await DynamicObject.CreateDynamicObjectAsync(sceneData, parent, persistenceData.GUID);
		if (dobj.Item != null)
		{
			if (dobj.Parent is SpaceObjectVessel)
			{
				PersistenceObjectDataItem persistenceObjectDataItem = data;
				if (persistenceObjectDataItem is { AttachPointID: not null } && dobj.Parent.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item?.AttachPointID != null && m.Item.AttachPointID.InSceneID == data.AttachPointID.Value) != null)
				{
					await dobj.DestroyDynamicObject();
					return null;
				}
			}
			await dobj.Item.LoadPersistenceData(persistenceData);
		}
		else
		{
			await dobj.LoadPersistenceData(persistenceData);
		}
		foreach (PersistenceObjectDataDynamicObject childData in persistenceData.ChildObjects.Cast<PersistenceObjectDataDynamicObject>())
		{
			await CreateDynamicObject(childData, dobj, structureSceneData);
		}
		return dobj;
	}
}

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public struct PersistenceObject
{
	public int SaveType;

	public double SolarSystemTime;

	public SaveFileAuxData AuxData;

	public HashSet<PersistenceObjectData> Ships;

	public HashSet<PersistenceObjectData> Asteroids;

	public HashSet<PersistenceObjectData> Players;

	public HashSet<PersistenceObjectData> RespawnObjects;

	public HashSet<PersistenceObjectData> SpawnPoints;

	public HashSet<PersistenceObjectData> ArenaControllers;

	public PersistenceObjectData DoomControllerData;

	public PersistenceObjectData SpawnManagerData;
}
