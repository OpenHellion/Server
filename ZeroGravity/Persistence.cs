using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;
using ZeroGravity.Spawn;

namespace ZeroGravity;

public class Persistence
{
	public static string PersistanceFileName = "hellion_{0}.save";

	public static int CurrentSaveType = 1;

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

	private static void SaveVesselPersistence(ref Persistence per, SpaceObjectVessel ves)
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
		if (ves.DockedVessels != null && ves.DockedVessels.Count > 0)
		{
			foreach (SpaceObjectVessel child2 in ves.DockedVessels)
			{
				SaveVesselPersistence(ref per, child2);
			}
		}
		if (ves.StabilizedToTargetChildren == null || ves.StabilizedToTargetChildren.Count <= 0)
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

	private static void SaveRespawnObjectPersistence(ref Persistence per, Server.DynamicObjectsRespawn obj)
	{
		PersistenceObjectDataRespawnObject data = new PersistenceObjectDataRespawnObject
		{
			ItemID = obj.Data.ItemID,
			ParentGUID = obj.Parent.GUID,
			ParentType = obj.Parent.ObjectType,
			Position = obj.Data.Position,
			Forward = obj.Data.Forward,
			Up = obj.Data.Up,
			AuxData = obj.Data.AuxData,
			RespawnTime = ((obj.Data.SpawnSettings.Length != 0) ? obj.Data.SpawnSettings[0].RespawnTime : (-1f)),
			Timer = obj.Timer
		};
		if (obj.APDetails != null)
		{
			data.AttachPointID = obj.APDetails.InSceneID;
		}
		per.RespawnObjects.Add(data);
	}

	private static void SaveSpawnPointPeristence(ref Persistence per, ShipSpawnPoint sp)
	{
		PersistenceObjectDataSpawnPoint data = new PersistenceObjectDataSpawnPoint
		{
			GUID = sp.Ship.GUID,
			SpawnID = sp.SpawnPointID,
			SpawnType = sp.Type,
			SpawnState = sp.State
		};
		if (sp.Player != null)
		{
			data.PlayerGUID = sp.Player.GUID;
			data.IsPlayerInSpawnPoint = sp.IsPlayerInSpawnPoint;
		}
		per.SpawnPoints.Add(data);
	}

	public static void Save(string filename = null, SaveFileAuxData auxData = null)
	{
		try
		{
			Persistence per = new Persistence();
			per.SaveType = CurrentSaveType;
			per.SolarSystemTime = Server.Instance.SolarSystem.CurrentTime;
			per.AuxData = auxData;
			per.Ships = new HashSet<PersistenceObjectData>();
			per.Asteroids = new HashSet<PersistenceObjectData>();
			per.Players = new HashSet<PersistenceObjectData>();
			per.RespawnObjects = new HashSet<PersistenceObjectData>();
			per.SpawnPoints = new HashSet<PersistenceObjectData>();
			per.ArenaControllers = new HashSet<PersistenceObjectData>();
			foreach (SpaceObjectVessel ves in Server.Instance.AllVessels)
			{
				if (!ves.IsDocked && ves.StabilizeToTargetObj == null && (ves.ObjectType == SpaceObjectType.Ship || ves.ObjectType == SpaceObjectType.Asteroid))
				{
					SaveVesselPersistence(ref per, ves);
				}
			}
			foreach (Player pl in Server.Instance.AllPlayers)
			{
				if (pl != null)
				{
					per.Players.Add(((IPersistantObject)pl).GetPersistenceData());
				}
			}
			foreach (Server.DynamicObjectsRespawn obj in Server.Instance.DynamicObjectsRespawnList)
			{
				SaveRespawnObjectPersistence(ref per, obj);
			}
			foreach (SpaceObjectVessel ves2 in Server.Instance.AllVessels)
			{
				if (ves2.SpawnPoints == null || ves2.SpawnPoints.Count <= 0)
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
			Json.SerializeToFile(per, Path.Combine(Server.ConfigDir, filename), Json.Formatting.Indented);
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	public static void LoadRespawnObjectPersistence(PersistenceObjectDataRespawnObject data)
	{
		SpaceObject parentObj = Server.Instance.GetObject(data.ParentGUID);
		if (parentObj == null)
		{
			Dbg.Error("Could not find parent object for respawn object persistence", data.ItemID);
			return;
		}
		AttachPointDetails apd = null;
		if (data.AttachPointID.HasValue && data.AttachPointID.Value > 0)
		{
			apd = new AttachPointDetails
			{
				InSceneID = data.AttachPointID.Value
			};
		}
		DynamicObjectSceneData dynamicObjectSceneData = new DynamicObjectSceneData();
		dynamicObjectSceneData.ItemID = data.ItemID;
		dynamicObjectSceneData.Position = data.Position;
		dynamicObjectSceneData.Forward = data.Forward;
		dynamicObjectSceneData.Up = data.Up;
		dynamicObjectSceneData.AttachPointInSceneId = (data.AttachPointID.HasValue ? data.AttachPointID.Value : (-1));
		dynamicObjectSceneData.AuxData = data.AuxData;
		dynamicObjectSceneData.SpawnSettings = new DynaminObjectSpawnSettings[1]
		{
			new DynaminObjectSpawnSettings
			{
				RespawnTime = data.RespawnTime,
				SpawnChance = -1f,
				Tag = ""
			}
		};
		DynamicObjectSceneData sceneData = dynamicObjectSceneData;
		Server.Instance.DynamicObjectsRespawnList.Add(new Server.DynamicObjectsRespawn
		{
			Data = sceneData,
			Parent = parentObj,
			Timer = data.Timer,
			RespawnTime = data.RespawnTime,
			APDetails = apd
		});
	}

	public static void LoadSpawnPointPeristence(PersistenceObjectDataSpawnPoint data)
	{
		try
		{
			Ship sh = Server.Instance.GetVessel(data.GUID) as Ship;
			ShipSpawnPoint sp = sh.SpawnPoints.Find((ShipSpawnPoint m) => m.SpawnPointID == data.SpawnID);
			sp.State = data.SpawnState;
			sp.Type = data.SpawnType;
			if (data.PlayerGUID.HasValue)
			{
				Player pl = (sp.Player = Server.Instance.GetPlayer(data.PlayerGUID.Value));
				sp.IsPlayerInSpawnPoint = data.IsPlayerInSpawnPoint.Value;
				pl.IsInsideSpawnPoint = sp.IsPlayerInSpawnPoint;
				if (sp.IsPlayerInSpawnPoint || sp.State == SpawnPointState.Authorized)
				{
					pl.SetSpawnPoint(sp);
				}
			}
		}
		catch (Exception ex)
		{
			Dbg.Error("Failed to load spawn point from persistence", ex.Message, ex.StackTrace);
		}
	}

	public static bool Load(string filename = null)
	{
		DirectoryInfo d = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), Server.ConfigDir));
		FileInfo[] files = d.GetFiles("*.save");
		FileInfo loadFromFile = null;
		loadFromFile = ((filename == null) ? files.OrderByDescending((FileInfo m) => m.LastWriteTimeUtc).FirstOrDefault() : files.FirstOrDefault((FileInfo m) => m.Name.ToLower() == filename.ToLower()));
		if (loadFromFile != null)
		{
			Persistence per = Json.Load<Persistence>(loadFromFile.FullName);
			Server.Instance.SolarSystem.CalculatePositionsAfterTime(per.SolarSystemTime);
			if (per.Asteroids != null)
			{
				foreach (PersistenceObjectData data6 in per.Asteroids)
				{
					Asteroid ast = new Asteroid(data6.GUID, initializeOrbit: false, Vector3D.Zero, Vector3D.One, Vector3D.Forward, Vector3D.Up);
					ast.LoadPersistenceData(data6);
				}
			}
			if (per.Ships != null)
			{
				foreach (PersistenceObjectData data5 in per.Ships)
				{
					Ship sh = new Ship(data5.GUID, initializeOrbit: false, Vector3D.Zero, Vector3D.One, Vector3D.Forward, Vector3D.Up);
					sh.LoadPersistenceData(data5);
				}
			}
			if (per.Players != null)
			{
				foreach (PersistenceObjectDataPlayer data4 in per.Players)
				{
					Player pl = new Player(data4.GUID, Vector3D.Zero, QuaternionD.Identity, "PersistenceLoad", "", data4.Gender, data4.HeadType, data4.HairType, addToServerList: false);
					pl.LoadPersistenceData(data4);
				}
			}
			if (per.RespawnObjects != null)
			{
				foreach (PersistenceObjectDataRespawnObject data3 in per.RespawnObjects)
				{
					LoadRespawnObjectPersistence(data3);
				}
			}
			if (per.SpawnPoints != null)
			{
				foreach (PersistenceObjectDataSpawnPoint data2 in per.SpawnPoints)
				{
					LoadSpawnPointPeristence(data2);
				}
			}
			if (per.ArenaControllers != null)
			{
				foreach (PersistenceArenaControllerData data in per.ArenaControllers)
				{
					DeathMatchArenaController arenaController = new DeathMatchArenaController();
					arenaController.LoadPersistenceData(data);
				}
			}
			if (per.DoomControllerData != null)
			{
				Server.Instance.DoomedShipController.LoadPersistenceData(per.DoomControllerData);
			}
			SpawnManager.LoadPersistenceData(per.SpawnManagerData);
			return true;
		}
		return false;
	}

	public static DynamicObject CreateDynamicObject(PersistenceObjectDataDynamicObject persistenceData, SpaceObject parent, StructureSceneData structureSceneData = null)
	{
		try
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
			if (structureSceneData != null && structureSceneData.DynamicObjects != null)
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
			DynamicObjectSceneData dynamicObjectSceneData = new DynamicObjectSceneData();
			dynamicObjectSceneData.ItemID = persistenceData.ItemID;
			dynamicObjectSceneData.Position = (persistenceData.RespawnTime.HasValue ? persistenceData.RespawnPosition : persistenceData.LocalPosition);
			dynamicObjectSceneData.Forward = (persistenceData.RespawnTime.HasValue ? persistenceData.RespawnForward : (persistenceData.LocalRotation.ToQuaternionD() * Vector3D.Forward).ToFloatArray());
			dynamicObjectSceneData.Up = (persistenceData.RespawnTime.HasValue ? persistenceData.RespawnUp : (persistenceData.LocalRotation.ToQuaternionD() * Vector3D.Up).ToFloatArray());
			dynamicObjectSceneData.AttachPointInSceneId = apInSceneID;
			dynamicObjectSceneData.AuxData = ((persistenceData.RespawnAuxData != null) ? persistenceData.RespawnAuxData : auxData);
			dynamicObjectSceneData.SpawnSettings = ((!persistenceData.RespawnTime.HasValue) ? null : new DynaminObjectSpawnSettings[1]
			{
				new DynaminObjectSpawnSettings
				{
					RespawnTime = persistenceData.RespawnTime.Value,
					SpawnChance = -1f,
					Tag = ""
				}
			});
			DynamicObjectSceneData sceneData = dynamicObjectSceneData;
			if (persistenceData is PersistenceObjectDataItem)
			{
				sceneData.AuxData.Tier = (persistenceData as PersistenceObjectDataItem).Tier;
			}
			DynamicObject dobj = new DynamicObject(sceneData, parent, persistenceData.GUID);
			if (dobj.Item != null)
			{
				if (dobj.Parent is SpaceObjectVessel)
				{
					PersistenceObjectDataItem persistenceObjectDataItem = data;
					if (persistenceObjectDataItem != null && persistenceObjectDataItem.AttachPointID.HasValue && dobj.Parent.DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item?.AttachPointID != null && m.Item.AttachPointID.InSceneID == data.AttachPointID.Value) != null)
					{
						dobj.DestroyDynamicObject();
						return null;
					}
				}
				dobj.Item.LoadPersistenceData(persistenceData);
			}
			else
			{
				dobj.LoadPersistenceData(persistenceData);
			}
			foreach (PersistenceObjectDataDynamicObject childData in persistenceData.ChildObjects)
			{
				CreateDynamicObject(childData, dobj, structureSceneData);
			}
			return dobj;
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
			return null;
		}
	}
}
