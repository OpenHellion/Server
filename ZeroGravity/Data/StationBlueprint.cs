using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenHellion.IO;
using ZeroGravity.Math;
using ZeroGravity.Objects;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;

namespace ZeroGravity.Data;

public class StationBlueprint
{
	public class Structure
	{
		public int StructureID;

		public string StructureType;

		public List<DockingPort> DockingPorts;

		public bool? SystemsOnline;

		public bool? Invulnerable;

		public bool? DoorsLocked;

		public string Tag;

		public float? HealthMultiplier;

		public bool? DockingControlsDisabled;

		public float? AirPressure;

		public float? AirQuality;

		public bool? SecurityPanelsLocked;

		public GameScenes.SceneId SceneID => Server.NameGenerator.GetSceneId(StructureType);
	}

	public class DockingPort
	{
		public string PortName;

		public int OrderID;

		public int? DockedStructureID;

		public string DockedPortName;

		public bool Locked;
	}

	private static string configDir;

	public string Version;

	public string Name;

	public string LinkURI;

	public List<Structure> Structures;

	public double[] LocalPosition;

	public double[] LocalRotation;

	public double[] LocalAngularVelocity;

	public bool Invulnerable = true;

	public bool? SystemsOnline;

	public bool? DoorsLocked;

	public float? HealthMultiplier;

	public bool? DockingControlsDisabled;

	public bool? AutoStabilizationDisabled;

	public float? AirPressure;

	public float? AirQuality;

	public bool? MatchVelocity;

	public bool? SecurityPanelsLocked;

	static StationBlueprint()
	{
		if (Server.ConfigDir.IsNullOrEmpty() || !Directory.Exists(Server.ConfigDir + "Data"))
		{
			configDir = "";
		}
		else
		{
			configDir = Server.ConfigDir;
		}
	}

	public SpaceObjectVessel AssembleStation(string name, string tag, SpawnRuleOrbit spawnRuleOrbit, long? nearArtificialBodyGUID, float? AsteroidResourcesMultiplier)
	{
		Dictionary<Structure, GameScenes.SceneId> structures = new Dictionary<Structure, GameScenes.SceneId>();
		foreach (Structure str2 in Structures)
		{
			GameScenes.SceneId sid = Server.NameGenerator.GetSceneId(str2.StructureType);
			if (sid == GameScenes.SceneId.None)
			{
				return null;
			}
			structures[str2] = sid;
		}
		SpaceObjectVessel mainVessel = null;
		Dictionary<Structure, SpaceObjectVessel> spawnedVessels = new Dictionary<Structure, SpaceObjectVessel>();
		foreach (KeyValuePair<Structure, GameScenes.SceneId> str in structures)
		{
			if (!spawnedVessels.TryGetValue(str.Key, out var vessel))
			{
				string vesselTag2 = tag != "" && str.Key.Tag != "" ? tag + ";" + str.Key.Tag : tag + str.Key.Tag;
				vessel = mainVessel != null ? SpaceObjectVessel.CreateNew(str.Value, "", -1L, localRotation: MathHelper.RandomRotation(), vesselTag: vesselTag2, nearArtificialBodyGUIDs: new List<long> { mainVessel.GUID }, celestialBodyGUIDs: null, positionOffset: null, velocityAtPosition: null, checkPosition: false, AsteroidResourcesMultiplier: AsteroidResourcesMultiplier) : SpaceObjectVessel.CreateNew(str.Value, "", -1L, localRotation: MathHelper.RandomRotation(), vesselTag: vesselTag2, spawnRuleOrbit: spawnRuleOrbit, nearArtificialBodyGUIDs: spawnRuleOrbit == null ? new List<long> { nearArtificialBodyGUID.Value } : null, celestialBodyGUIDs: null, positionOffset: null, velocityAtPosition: null, checkPosition: spawnRuleOrbit == null, AsteroidResourcesMultiplier: AsteroidResourcesMultiplier);
				vessel.VesselData.VesselRegistration = !name.IsNullOrEmpty() ? name : Server.NameGenerator.GenerateStationRegistration();
				spawnedVessels[str.Key] = vessel;
			}
			else
			{
				vessel = spawnedVessels[str.Key];
			}
			if (mainVessel == null)
			{
				mainVessel = vessel;
			}
			if (str.Key.DockingPorts == null)
			{
				continue;
			}
			foreach (DockingPort dp in str.Key.DockingPorts.Where((DockingPort m) => m.DockedStructureID.HasValue))
			{
				KeyValuePair<Structure, GameScenes.SceneId> otherStr = structures.FirstOrDefault((KeyValuePair<Structure, GameScenes.SceneId> m) => m.Key.StructureID == dp.DockedStructureID);
				if (otherStr.Key.DockingPorts != null)
				{
					string vesselTag = tag != "" && otherStr.Key.Tag != "" ? tag + ";" + otherStr.Key.Tag : tag + otherStr.Key.Tag;
					if (!spawnedVessels.TryGetValue(otherStr.Key, out var otherVessel))
					{
						otherVessel = SpaceObjectVessel.CreateNew(otherStr.Value, "", -1L, localRotation: MathHelper.RandomRotation(), vesselTag: vesselTag, nearArtificialBodyGUIDs: new List<long> { mainVessel.GUID }, celestialBodyGUIDs: null, positionOffset: null, velocityAtPosition: null, checkPosition: false, AsteroidResourcesMultiplier: AsteroidResourcesMultiplier);
						spawnedVessels[otherStr.Key] = otherVessel;
					}
					else
					{
						otherVessel = spawnedVessels[otherStr.Key];
					}
					otherVessel.VesselData.VesselRegistration = !name.IsNullOrEmpty() ? name : Server.NameGenerator.GenerateStationRegistration();
					VesselDockingPort shipPort = vessel.DockingPorts.FirstOrDefault((VesselDockingPort m) => m.OrderID == dp.OrderID);
					DockingPort dp2 = otherStr.Key.DockingPorts.FirstOrDefault((DockingPort m) => m.DockedStructureID == str.Key.StructureID);
					int otherOrderID = dp2.OrderID;
					VesselDockingPort otherPort = otherVessel.DockingPorts.FirstOrDefault((VesselDockingPort m) => m.OrderID == otherOrderID);
					if (shipPort == null || otherPort == null)
					{
						throw new Exception($"Docking port not found. Station name: {name}, StructureType: {str.Key.StructureType}, StructureID: {str.Key.StructureID}, OrderID: {dp.OrderID}");
					}
					if (!shipPort.DockingStatus && !otherPort.DockingStatus)
					{
						otherVessel.DockToVessel(otherPort, shipPort, vessel, disableStabilization: true, spawnRuleOrbit == null, buildingStation: true);
					}
				}
			}
		}
		foreach (KeyValuePair<Structure, SpaceObjectVessel> kv in spawnedVessels)
		{
			SetStates(kv.Key, kv.Value);
		}
		if (spawnRuleOrbit != null)
		{
			mainVessel.Orbit = spawnRuleOrbit.GenerateRandomOrbit();
		}
		else if (nearArtificialBodyGUID.HasValue && LocalPosition != null && LocalPosition.Length == 3)
		{
			ArtificialBody ab = Server.Instance.GetObject(nearArtificialBodyGUID.Value) as ArtificialBody;
			mainVessel.Orbit = new OrbitParameters();
			mainVessel.Orbit.CopyDataFrom(ab.Orbit, Server.SolarSystemTime, exactCopy: true);
			mainVessel.Orbit.RelativePosition += QuaternionD.LookRotation(ab.Forward, ab.Up) * LocalPosition.ToVector3D();
			mainVessel.Orbit.InitFromCurrentStateVectors(Server.SolarSystemTime);
		}
		mainVessel.Orbit.UpdateOrbit();
		if (LocalAngularVelocity != null && LocalAngularVelocity.Length == 3)
		{
			mainVessel.Rotation = LocalAngularVelocity.ToVector3D();
		}
		return mainVessel;
	}

	private void SetStates(Structure structure, SpaceObjectVessel vessel)
	{
		vessel.IsInvulnerable = structure.Invulnerable.HasValue ? structure.Invulnerable.Value : Invulnerable;
		if (structure.HealthMultiplier.HasValue)
		{
			vessel.Health *= structure.HealthMultiplier.Value;
		}
		else if (HealthMultiplier.HasValue)
		{
			vessel.Health *= HealthMultiplier.Value;
		}
		if (structure.DockingControlsDisabled.HasValue)
		{
			vessel.DockingControlsDisabled = structure.DockingControlsDisabled.Value;
		}
		else if (DockingControlsDisabled.HasValue)
		{
			vessel.DockingControlsDisabled = DockingControlsDisabled.Value;
		}
		if (structure.SecurityPanelsLocked.HasValue)
		{
			vessel.SecurityPanelsLocked = structure.SecurityPanelsLocked.Value;
		}
		else if (SecurityPanelsLocked.HasValue)
		{
			vessel.SecurityPanelsLocked = SecurityPanelsLocked.Value;
		}
		if (AutoStabilizationDisabled.HasValue)
		{
			vessel.AutoStabilizationDisabled = AutoStabilizationDisabled.Value;
		}
		foreach (DistributionManager.CompoundRoom compRoom in vessel.Rooms.Select((Room m) => m.CompoundRoom).Distinct())
		{
			if (structure.AirPressure.HasValue)
			{
				compRoom.AirPressure = MathHelper.Clamp(structure.AirPressure.Value, 0f, 1f);
			}
			else if (AirPressure.HasValue)
			{
				compRoom.AirPressure = MathHelper.Clamp(AirPressure.Value, 0f, 1f);
			}
			if (structure.AirQuality.HasValue)
			{
				compRoom.AirQuality = MathHelper.Clamp(structure.AirQuality.Value, 0f, 1f);
			}
			else if (AirQuality.HasValue)
			{
				compRoom.AirQuality = MathHelper.Clamp(AirQuality.Value, 0f, 1f);
			}
		}
		if (vessel.DistributionManager != null)
		{
			foreach (VesselComponent vc in from m in vessel.DistributionManager.GetVesselComponents()
				where m.CanBlueprintForceState
				select m)
			{
				if ((structure.SystemsOnline.HasValue && structure.SystemsOnline.Value) || (SystemsOnline.HasValue && SystemsOnline.Value))
				{
					vc.GoOnLine();
				}
				else if ((structure.SystemsOnline.HasValue && !structure.SystemsOnline.Value) || (SystemsOnline.HasValue && !SystemsOnline.Value))
				{
					vc.GoOffLine(autoRestart: false);
				}
			}
		}
		foreach (Door door in vessel.Doors)
		{
			if ((structure.DoorsLocked.HasValue && structure.DoorsLocked.Value) || (DoorsLocked.HasValue && DoorsLocked.Value))
			{
				door.IsLocked = true;
			}
			else if ((structure.DoorsLocked.HasValue && structure.DoorsLocked.Value) || (DoorsLocked.HasValue && DoorsLocked.Value))
			{
				door.IsLocked = false;
			}
		}
		foreach (VesselDockingPort vdp in vessel.DockingPorts)
		{
			DockingPort dp = structure.DockingPorts.FirstOrDefault((DockingPort m) => m.OrderID == vdp.OrderID);
			if (dp != null)
			{
				vdp.Locked = dp.Locked;
			}
		}
	}

	public static List<SpaceObjectVessel> AssembleStation(string blueprintName, string name, string tag, SpawnRuleOrbit spawnRuleOrbit, long? nearArtificialBodyGUID, float? AsteroidResourcesMultiplier = 1f)
	{
		List<SpaceObjectVessel> list = new List<SpaceObjectVessel>();
		string fileName = configDir + "Data/Stations/" + blueprintName + ".json";
		try
		{
			StationBlueprint sb = JsonSerialiser.Load<StationBlueprint>(fileName);
			SpaceObjectVessel mainVessel2 = sb.AssembleStation(name.IsNullOrEmpty() ? sb.Name : name, tag, spawnRuleOrbit, nearArtificialBodyGUID, AsteroidResourcesMultiplier);
			list.Add(mainVessel2);
			mainVessel2.UpdateVesselData();
			return list;
		}
		catch (Exception)
		{
			try
			{
				StationBlueprint[] sbs = JsonSerialiser.Load<StationBlueprint[]>(fileName);
				SpaceObjectVessel mainVessel = null;
				StationBlueprint[] array = sbs;
				foreach (StationBlueprint sb2 in array)
				{
					if (mainVessel == null)
					{
						mainVessel = sb2.AssembleStation(name.IsNullOrEmpty() ? sb2.Name : name, tag, spawnRuleOrbit, nearArtificialBodyGUID, AsteroidResourcesMultiplier);
						mainVessel.UpdateVesselData();
						list.Add(mainVessel);
						continue;
					}
					SpaceObjectVessel vessel = sb2.AssembleStation(sb2.Name, tag, null, mainVessel.GUID, AsteroidResourcesMultiplier);
					if (sb2.MatchVelocity.HasValue && sb2.MatchVelocity.Value)
					{
						vessel.StabilizeToTarget(mainVessel, forceStabilize: true);
					}
					vessel.UpdateVesselData();
					list.Add(vessel);
				}
				return list;
			}
			catch (Exception ex)
			{
				Debug.Exception(ex);
			}
		}
		return null;
	}
}
