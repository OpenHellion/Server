using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class DistributionManager
{
	public class ShortArrayComparer : IEqualityComparer<short[]>
	{
		public bool Equals(short[] x, short[] y)
		{
			if (x.Length != y.Length)
			{
				return false;
			}
			for (int i = 0; i < x.Length; i++)
			{
				if (x[i] != y[i])
				{
					return false;
				}
			}
			return true;
		}

		public int GetHashCode(short[] obj)
		{
			long result = 17L;
			for (int i = 0; i < obj.Length; i++)
			{
				result = result * 23 + obj[i];
			}
			return (int)(result & 0xFFFFFFF);
		}
	}

	public class CompoundRoom
	{
		public float Volume;

		public HashSet<Room> Rooms = new HashSet<Room>();

		private float _AirPressure = 1f;

		private float _AirQuality = 1f;

		private float _AirPressureChangeRate;

		private float _AirQualityChangeRate;

		public List<ILifeSupportDevice> LifeSupportDevices = new List<ILifeSupportDevice>();

		public short ID;

		public List<IAirConsumer> AirConsumers = new List<IAirConsumer>();

		public bool IsAirOk => (double)AirPressure > -0.67 * (double)AirQuality + 1.0;

		public float AirPressure
		{
			get
			{
				return _AirPressure;
			}
			set
			{
				if (float.IsNaN(value))
				{
					_AirPressure = 0f;
				}
				else
				{
					_AirPressure = MathHelper.Clamp(value, 0f, 1f);
				}
				foreach (Room av in Rooms)
				{
					av.AirPressure = _AirPressure;
				}
			}
		}

		public float AirQuality
		{
			get
			{
				return _AirQuality;
			}
			set
			{
				if (float.IsNaN(value))
				{
					_AirQuality = 0f;
				}
				else
				{
					_AirQuality = MathHelper.Clamp(value, 0f, 1f);
				}
				foreach (Room av in Rooms)
				{
					av.AirQuality = _AirQuality;
				}
			}
		}

		public float AirQualityChangeRate
		{
			get
			{
				return _AirQualityChangeRate;
			}
			set
			{
				_AirQualityChangeRate = value;
				foreach (Room av in Rooms)
				{
					av.AirQualityChangeRate = _AirQualityChangeRate;
				}
			}
		}

		public float AirPressureChangeRate
		{
			get
			{
				return _AirPressureChangeRate;
			}
			set
			{
				_AirPressureChangeRate = value;
				foreach (Room av in Rooms)
				{
					av.AirPressureChangeRate = _AirPressureChangeRate;
				}
			}
		}

		public bool FireCanBurn => AirQuality * AirPressure >= 0.25f;

		public CompoundRoom(Room room, short id)
		{
			ID = id;
			addConnectedRooms(room);
			float quantitySum = 0f;
			float qualitySum = 0f;
			float quantityDivider = 0f;
			float qualityDivider = 0f;
			foreach (Room r in Rooms)
			{
				Volume += r.Volume;
				quantitySum += r.AirPressure * r.Volume;
				qualitySum += r.AirQuality * r.AirPressure * r.Volume;
				quantityDivider += r.Volume;
				qualityDivider += r.AirPressure * r.Volume;
				if (r.AirConsumers.Count > 0)
				{
					AirConsumers.AddRange(r.AirConsumers);
				}
				if (r.LifeSupportDevices.Count > 0)
				{
					LifeSupportDevices.AddRange(r.LifeSupportDevices);
				}
			}
			_AirPressure = quantityDivider > 0f ? quantitySum / quantityDivider : 0f;
			_AirQuality = qualityDivider > 0f ? qualitySum / qualityDivider : 0f;
		}

		private void addConnectedRooms(Room room)
		{
			if (!Rooms.Add(room))
			{
				return;
			}
			room.CompoundRoom = this;
			foreach (Room linkedRoom in room.LinkedRooms)
			{
				List<Door> connectingDoors = room.Doors.Where((Door m) => m.Room1 == linkedRoom || m.Room2 == linkedRoom).ToList();
				if (connectingDoors.Count == 0 || connectingDoors.Count((Door m) => !m.IsSealed) > 0)
				{
					addConnectedRooms(linkedRoom);
				}
			}
		}

		public void RemoveAirConsumer(IAirConsumer consumer)
		{
			AirConsumers.Remove(consumer);
			foreach (Room r in Rooms)
			{
				r.AirConsumers.Remove(consumer);
			}
		}
	}

	private class DistributionNode
	{
		[JsonIgnore]
		public HashSet<DistributionNode> LinkedNodes = new HashSet<DistributionNode>();

		public List<IResourceUser> ResourceUsers = new List<IResourceUser>();

		public List<IResourceProvider> ResourceProviders = new List<IResourceProvider>();
	}

	public class ResourceproviderComparer : IComparer<IResourceProvider>
	{
		private static Dictionary<Type, int> types = new Dictionary<Type, int>
		{
			{
				typeof(GeneratorSolar),
				1
			},
			{
				typeof(GeneratorPower),
				2
			},
			{
				typeof(GeneratorCapacitor),
				3
			}
		};

		public int Compare(IResourceProvider x, IResourceProvider y)
		{
			if (x.Equals(y))
			{
				return 0;
			}
			if (types.ContainsKey(x.GetType()) && !types.ContainsKey(y.GetType()))
			{
				return -1;
			}
			if (!types.ContainsKey(x.GetType()) && types.ContainsKey(y.GetType()))
			{
				return 1;
			}
			if ((!types.ContainsKey(x.GetType()) && !types.ContainsKey(y.GetType())) || x.GetType() == y.GetType())
			{
				return 1;
			}
			return types[x.GetType()] > types[y.GetType()] || (types[x.GetType()] == types[y.GetType()] && x is VesselComponent component && y is VesselComponent vesselComponent && component.ParentVessel != vesselComponent.ParentVessel) ? 1 : -1;
		}
	}

	private static List<Type> consumersOrder;

	private static Dictionary<short, StructureSceneData> structureDefs;

	private Dictionary<IResourceProvider, DistributionNode> resourceProviderNodes = new Dictionary<IResourceProvider, DistributionNode>();

	private Dictionary<VesselObjectID, Generator> idGenerators = new Dictionary<VesselObjectID, Generator>();

	private Dictionary<VesselObjectID, VesselComponent> idMachineryPartSlots = new Dictionary<VesselObjectID, VesselComponent>();

	private Dictionary<VesselObjectID, SubSystem> idSubSystems = new Dictionary<VesselObjectID, SubSystem>();

	private Dictionary<VesselObjectID, Room> idRooms = new Dictionary<VesselObjectID, Room>();

	private Dictionary<VesselObjectID, ResourceContainer> idResourceContainers = new Dictionary<VesselObjectID, ResourceContainer>();

	private Dictionary<VesselObjectID, Door> idDoors = new Dictionary<VesselObjectID, Door>();

	private Dictionary<long, DistributionNode> distributionNodes = new Dictionary<long, DistributionNode>();

	private Dictionary<Room, CompoundRoom> compoundRooms = new Dictionary<Room, CompoundRoom>();

	private Dictionary<Player, Room> playerRooms = new Dictionary<Player, Room>();

	private DateTime prevSystemsUpdateTime;

	public Dictionary<DistributionSystemType, float> AvailableResourceCapacities = new Dictionary<DistributionSystemType, float>();

	public Dictionary<DistributionSystemType, float> AvailableResourceQuantities = new Dictionary<DistributionSystemType, float>();

	private bool isCompoundDM;

	private bool initialize = true;

	private SpaceObjectVessel parentVessel;

	static DistributionManager()
	{
		consumersOrder = new List<Type>
		{
			typeof(VesselBaseSystem),
			typeof(SubSystemRefinery),
			typeof(SubSystemFTL),
			typeof(SubSystemEngine)
		};
		structureDefs = new Dictionary<short, StructureSceneData>();
		consumersOrder = consumersOrder.OrderByDescending((Type m) => consumersOrder.IndexOf(m)).ToList();
		foreach (StructureSceneData s in StaticData.StructuresDataList)
		{
			structureDefs[s.ItemID] = s;
		}
	}

	public float PressureEquilizationTime(VesselObjectID doorID, out int direction, out float airSpeed)
	{
		airSpeed = 0f;
		direction = 0;
		Door door = GetDoor(doorID);
		if (door == null)
		{
			return 0f;
		}
		float area = door.PassageArea;
		CompoundRoom cr1 = null;
		CompoundRoom cr2 = null;
		float qty = 0f;
		float rate = 1f;
		if (door.Room1 != null)
		{
			compoundRooms.TryGetValue(door.Room1, out cr1);
		}
		if (door.Room2 != null)
		{
			compoundRooms.TryGetValue(door.Room2, out cr2);
		}
		if (cr1 == cr2)
		{
			return 0f;
		}
		if (cr1 != null && cr2 != null)
		{
			float sumArea = 0f;
			if (cr1.AirPressure <= float.Epsilon)
			{
				foreach (Door d in cr1.AirConsumers.Where((IAirConsumer m) => m is Door door1 && door1.isExternal && !door1.IsSealed))
				{
					sumArea += d.PassageArea;
				}
				if (area > sumArea && sumArea > 0f)
				{
					area = sumArea;
				}
				direction = 2;
				qty = cr2.Volume * cr2.AirPressure;
				rate = (float)(0.61 * (double)area * System.Math.Sqrt((double)(2f * cr2.AirPressure * 100000f) / 1.225));
			}
			else if (cr2.AirPressure <= float.Epsilon)
			{
				foreach (Door d2 in cr2.AirConsumers.Where((IAirConsumer m) => m is Door door1 && door1.isExternal && !door1.IsSealed))
				{
					sumArea += d2.PassageArea;
				}
				if (area > sumArea && sumArea > 0f)
				{
					area = sumArea;
				}
				direction = 1;
				qty = cr1.Volume * cr1.AirPressure;
				rate = (float)(0.61 * (double)area * System.Math.Sqrt((double)(2f * cr1.AirPressure * 100000f) / 1.225));
			}
			if (direction == 0)
			{
				if (cr1.AirPressure > cr2.AirPressure)
				{
					direction = 1;
				}
				else
				{
					direction = 2;
				}
				qty = System.Math.Abs((cr2.Volume * (cr1.Volume * cr1.AirPressure) - cr1.Volume * (cr2.Volume * cr2.AirPressure)) / (cr1.Volume + cr2.Volume));
				rate = (float)(0.61 * (double)area * System.Math.Sqrt(2.0 * System.Math.Abs((double)((cr1.AirPressure - cr2.AirPressure) * 100000f) / 1.225)));
			}
		}
		else if (cr1 != null && cr1.AirPressure > float.Epsilon)
		{
			direction = 1;
			qty = cr1.Volume * cr1.AirPressure;
			rate = (float)(0.61 * (double)area * System.Math.Sqrt((double)(2f * cr1.AirPressure * 100000f) / 1.225));
		}
		else if (cr2 != null && cr2.AirPressure > float.Epsilon)
		{
			direction = 2;
			qty = cr2.Volume * cr2.AirPressure;
			rate = (float)(0.61 * (double)area * System.Math.Sqrt((double)(2f * cr2.AirPressure * 100000f) / 1.225));
		}
		float time;
		if (rate <= float.Epsilon || (double)(time = qty / rate) < 0.05)
		{
			direction = 0;
			return 0f;
		}
		airSpeed = rate * (2f / (area * area));
		return time;
	}

	private void InitInstance()
	{
	}

	public DistributionManager(SpaceObjectVessel vessel, bool linkDockedVessels = false)
	{
		isCompoundDM = linkDockedVessels;
		InitInstance();
		AddShipDataStructure(vessel, vessel.VesselData.SceneID);
		if (isCompoundDM)
		{
			LinkNodesInDockedVessels(vessel, new List<SpaceObjectVessel>());
		}
		parentVessel = vessel;
		prevSystemsUpdateTime = DateTime.UtcNow;
		UpdateConnections();
	}

	private void LinkNodesInDockedVessels(SpaceObjectVessel vessel, List<SpaceObjectVessel> traversedVessels)
	{
		if (traversedVessels.Contains(vessel))
		{
			return;
		}
		traversedVessels.Add(vessel);
		foreach (VesselDockingPort port in vessel.DockingPorts)
		{
			if (!port.DockingStatus)
			{
				continue;
			}
			DistributionNode node1 = distributionNodes[vessel.GUID];
			VesselDockingPort dockedPort = port.DockedVessel.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == port.DockedToID.InSceneID);
			DistributionNode node2 = distributionNodes[port.DockedVessel.GUID];
			LinkDistributionNodes(node1, node2);
			int[] doorsIDs = port.DoorsIDs;
			for (int i = 0; i < doorsIDs.Length; i++)
			{
				short door1Id = (short)doorsIDs[i];
				VesselObjectID id1 = new VesselObjectID(vessel.GUID, door1Id);
				VesselObjectID matchingID = null;
				Door door1 = GetDoor(id1);
				if (door1 == null || !door1.isExternal)
				{
					continue;
				}
				double minDist = 100.0;
				Door matchingDoor = null;
				int[] doorsIDs2 = dockedPort.DoorsIDs;
				for (int j = 0; j < doorsIDs2.Length; j++)
				{
					short door2Id = (short)doorsIDs2[j];
					VesselObjectID id2 = new VesselObjectID(port.DockedVessel.GUID, door2Id);
					Door door2 = GetDoor(id2);
					if (door2 != null)
					{
						double dist = (door1.PositionRelativeToDockingPort - QuaternionD.AngleAxis(180.0, Vector3D.Up) * door2.PositionRelativeToDockingPort).Magnitude;
						if (dist <= (double)port.DoorPairingDistance && dist < minDist)
						{
							matchingDoor = door2;
							matchingID = id2;
						}
					}
				}
				if (matchingDoor != null && matchingDoor.isExternal)
				{
					PairDoors(door1, matchingDoor);
				}
			}
			LinkNodesInDockedVessels(port.DockedVessel, traversedVessels);
		}
	}

	private void PairDoors(Door door1, Door door2)
	{
		door1.PairedDoorID = door2.ID;
		door2.PairedDoorID = door1.ID;
		if (door1.LockedAutoToggle)
		{
			door1.IsLocked = false;
		}
		if (door2.LockedAutoToggle)
		{
			door2.IsLocked = false;
		}
		if (door1.Room1 != null)
		{
			Room room2 = null;
			if (door2.Room1 != null)
			{
				room2 = door2.Room1;
				door2.Room2 = door1.Room1;
			}
			else if (door2.Room2 != null)
			{
				room2 = door2.Room2;
				door2.Room1 = door1.Room1;
			}
			door1.Room2 = room2;
			LinkRooms(room2, door1.Room1);
		}
		else if (door1.Room2 != null)
		{
			Room room = null;
			if (door2.Room1 != null)
			{
				room = door2.Room1;
				door2.Room2 = door1.Room2;
			}
			else if (door2.Room2 != null)
			{
				room = door2.Room2;
				door2.Room1 = door1.Room2;
			}
			door1.Room1 = room;
			LinkRooms(room, door1.Room2);
		}
	}

	private void LinkDistributionNodes(DistributionNode node1, DistributionNode node2)
	{
		node1.LinkedNodes.Add(node2);
		node2.LinkedNodes.Add(node1);
	}

	private void LinkRooms(Room room1, Room room2)
	{
		room1.LinkedRooms.Add(room2);
		room2.LinkedRooms.Add(room1);
		if (room1.GravityAutoToggle)
		{
			room1.UseGravity = !room1.HasExternalDoor;
		}
		if (room2.GravityAutoToggle)
		{
			room2.UseGravity = !room2.HasExternalDoor;
		}
	}

	public void UnpairAllDoors()
	{
		foreach (KeyValuePair<VesselObjectID, Door> idDoor in idDoors)
		{
			Door door = idDoor.Value;
			if (door.LockedAutoToggle)
			{
				door.IsLocked = true;
			}
			if (door.Room1 != null && door.Room1.GravityAutoToggle)
			{
				door.Room1.UseGravity = false;
			}
			if (door.Room2 != null && door.Room2.GravityAutoToggle)
			{
				door.Room2.UseGravity = false;
			}
			if (door.PairedDoorID != null && idDoors.TryGetValue(door.PairedDoorID, out var otherDoor))
			{
				UnpairDoor(otherDoor);
			}
			UnpairDoor(door);
		}
	}

	private void UnpairDoor(Door door)
	{
		VesselObjectID room1ID = idRooms.FirstOrDefault((KeyValuePair<VesselObjectID, Room> m) => m.Value == door.Room1).Key;
		VesselObjectID room2ID = idRooms.FirstOrDefault((KeyValuePair<VesselObjectID, Room> m) => m.Value == door.Room2).Key;
		if (room1ID != null && room2ID != null && (room1ID.VesselGUID != door.ID.VesselGUID || room2ID.VesselGUID != door.ID.VesselGUID))
		{
			door.Room1.LinkedRooms.Remove(door.Room2);
			door.Room2.LinkedRooms.Remove(door.Room1);
			if (room1ID.VesselGUID != door.ID.VesselGUID)
			{
				door.Room1 = null;
			}
			else if (room2ID.VesselGUID != door.ID.VesselGUID)
			{
				door.Room2 = null;
			}
		}
		door.PairedDoorID = null;
	}

	public Door GetDoor(VesselObjectID sid)
	{
		if (idDoors.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	public Room GetRoom(VesselObjectID sid)
	{
		if (idRooms.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	public SubSystem GetSubSystem(VesselObjectID sid)
	{
		if (idSubSystems.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	public Generator GetGenerator(VesselObjectID sid)
	{
		if (idGenerators.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	public VesselComponent GetVesselComponent(VesselObjectID sid)
	{
		if (idSubSystems.TryGetValue(sid, out var ss))
		{
			return ss;
		}
		if (idGenerators.TryGetValue(sid, out var gen))
		{
			return gen;
		}
		return null;
	}

	public ResourceContainer GetResourceContainer(VesselObjectID sid)
	{
		if (idResourceContainers.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	public VesselComponent GetVesselComponentByPartSlot(VesselObjectID sid)
	{
		if (idMachineryPartSlots.TryGetValue(sid, out var ret))
		{
			return ret;
		}
		return null;
	}

	private DistributionNode AddShipDataStructure(SpaceObjectVessel vessel, GameScenes.SceneId sceneID)
	{
		StructureSceneData structureSceneData = structureDefs[(short)sceneID];
		foreach (RoomData roomData2 in structureSceneData.Rooms)
		{
			VesselObjectID id4 = new VesselObjectID(vessel.GUID, roomData2.InSceneID);
			Room room3 = vessel.Rooms.Find((Room m) => m.ID.Equals(id4));
			if (room3 != null)
			{
				idRooms[id4] = room3;
			}
		}
		foreach (DoorData dd in structureSceneData.Doors)
		{
			if (!dd.IsSealable)
			{
				continue;
			}
			VesselObjectID id5 = new VesselObjectID(vessel.GUID, dd.InSceneID);
			Door door;
			if (isCompoundDM)
			{
				door = vessel.DistributionManager.GetDoor(id5);
			}
			else
			{
				door = vessel.Doors.Find((Door m) => m.ID.Equals(id5));
				door.Room1 = GetRoom(new VesselObjectID(vessel.GUID, dd.Room1ID));
				door.Room2 = GetRoom(new VesselObjectID(vessel.GUID, dd.Room2ID));
				if (door.Room1 != null)
				{
					door.Room1.Doors.Add(door);
					if (door.isExternal)
					{
						door.Room1.AddAirConsumer(door);
					}
				}
				if (door.Room2 != null)
				{
					door.Room2.Doors.Add(door);
					if (door.isExternal)
					{
						door.Room2.AddAirConsumer(door);
					}
				}
			}
			idDoors[id5] = door;
		}
		DistributionNode node = new DistributionNode();
		foreach (RoomData roomData in structureSceneData.Rooms)
		{
			Room room2 = idRooms[new VesselObjectID(vessel.GUID, roomData.InSceneID)];
			if (roomData.ParentRoomID > 0 && idRooms.TryGetValue(new VesselObjectID(vessel.GUID, roomData.ParentRoomID), out var parentRoom))
			{
				LinkRooms(room2, parentRoom);
				parentRoom.LinkedRooms.Add(room2);
				room2.LinkedRooms.Add(parentRoom);
			}
			node.ResourceUsers.Add(room2);
		}
		foreach (ResourceContainerData rcData in structureSceneData.ResourceContainers)
		{
			VesselObjectID id3 = new VesselObjectID(vessel.GUID, rcData.InSceneID);
			ResourceContainer rc = !isCompoundDM ? rcData.DistributionSystemType != DistributionSystemType.Air ? new ResourceContainer(vessel, id3, rcData) : new ResourceContainerAirTank(vessel, id3, rcData) : vessel.DistributionManager.GetResourceContainer(id3);
			node.ResourceProviders.Add(rc);
			node.ResourceUsers.Add(rc);
			idResourceContainers[id3] = rc;
			resourceProviderNodes[rc] = node;
		}
		foreach (SubSystemData ssData in structureSceneData.SubSystems)
		{
			VesselObjectID id2 = new VesselObjectID(vessel.GUID, ssData.InSceneID);
			SubSystem ss;
			if (isCompoundDM)
			{
				ss = vessel.DistributionManager.GetSubSystem(id2);
			}
			else
			{
				VesselComponent vc2 = vessel.Systems.Find((VesselComponent m) => m.ID.InSceneID == ssData.InSceneID);
				if (vc2 == null || !(vc2 is SubSystem system))
				{
					continue;
				}
				ss = system;
				if (ss is SubSystemRCS rcs)
				{
					vessel.RCS = rcs;
				}
				else if (ss is SubSystemEngine engine)
				{
					vessel.Engine = engine;
				}
				else if (ss is SubSystemFTL ftl)
				{
					vessel.FTL = ftl;
				}
				else if (ss is SubSystemRefinery refinery)
				{
					vessel.Refinery = refinery;
				}
				else if (ss is VesselBaseSystem baseSystem)
				{
					vessel.VesselBaseSystem = baseSystem;
				}
				else if (ss is SubSystemFabricator fabricator)
				{
					vessel.Fabricator = fabricator;
				}
				foreach (int resourceContainer in ssData.ResourceContainers)
				{
					short isid2 = (short)resourceContainer;
					VesselObjectID rcID2 = new VesselObjectID(vessel.GUID, isid2);
					ResourceContainer cr2 = null;
					if (idResourceContainers.TryGetValue(rcID2, out cr2))
					{
						if (!ss.ResourceContainers.ContainsKey(cr2.OutputType))
						{
							ss.ResourceContainers[cr2.OutputType] = new HashSet<ResourceContainer>();
						}
						ss.ResourceContainers[cr2.OutputType].Add(cr2);
					}
				}
				if (ssData.RoomID > 0)
				{
					Room room = idRooms[new VesselObjectID(vessel.GUID, ssData.RoomID)];
					room.VesselComponents.Add(ss);
					ss.Room = room;
					if (ss is ILifeSupportDevice device)
					{
						room.LifeSupportDevices.Add(device);
					}
				}
			}
			node.ResourceUsers.Add(ss);
			idSubSystems[id2] = ss;
			if (ssData.MachineryPartSlots == null)
			{
				continue;
			}
			foreach (int machineryPartSlot in ssData.MachineryPartSlots)
			{
				short msId2 = (short)machineryPartSlot;
				if (structureSceneData.AttachPoints.Find((BaseAttachPointData m) => m.InSceneID == msId2) is MachineryPartSlotData slotData2)
				{
					VesselObjectID sid2 = new VesselObjectID(vessel.GUID, msId2);
					idMachineryPartSlots[sid2] = ss;
					if (!isCompoundDM)
					{
						ss.InitMachineryPartSlot(sid2, null, slotData2);
					}
				}
			}
		}
		foreach (GeneratorData gData in structureSceneData.Generators)
		{
			VesselObjectID id = new VesselObjectID(vessel.GUID, gData.InSceneID);
			Generator gen;
			if (isCompoundDM)
			{
				gen = vessel.DistributionManager.GetGenerator(id);
			}
			else
			{
				VesselComponent vc = vessel.Systems.Find((VesselComponent m) => m.ID.InSceneID == gData.InSceneID);
				if (vc == null || !(vc is Generator generator))
				{
					continue;
				}
				gen = generator;
				if (gen is GeneratorCapacitor capacitor)
				{
					vessel.Capacitor = capacitor;
				}
				foreach (int resourceContainer2 in gData.ResourceContainers)
				{
					short isid = (short)resourceContainer2;
					VesselObjectID rcID = new VesselObjectID(vessel.GUID, isid);
					ResourceContainer cr = null;
					if (idResourceContainers.TryGetValue(rcID, out cr))
					{
						if (!gen.ResourceContainers.ContainsKey(cr.OutputType))
						{
							gen.ResourceContainers[cr.OutputType] = new HashSet<ResourceContainer>();
						}
						gen.ResourceContainers[cr.OutputType].Add(cr);
					}
				}
			}
			node.ResourceProviders.Add(gen);
			node.ResourceUsers.Add(gen);
			idGenerators[id] = gen;
			resourceProviderNodes[gen] = node;
			if (gData.MachineryPartSlots == null)
			{
				continue;
			}
			foreach (int machineryPartSlot2 in gData.MachineryPartSlots)
			{
				short msId = (short)machineryPartSlot2;
				if (structureSceneData.AttachPoints.Find((BaseAttachPointData m) => m.InSceneID == msId) is MachineryPartSlotData slotData)
				{
					VesselObjectID sid = new VesselObjectID(vessel.GUID, msId);
					idMachineryPartSlots[sid] = gen;
					if (!isCompoundDM)
					{
						gen.InitMachineryPartSlot(sid, null, slotData);
					}
				}
			}
		}
		distributionNodes[vessel.GUID] = node;
		if (isCompoundDM)
		{
			foreach (VesselDockingPort port in vessel.DockingPorts)
			{
				if (port.DockingStatus && !distributionNodes.ContainsKey(port.DockedVessel.GUID))
				{
					AddShipDataStructure(port.DockedVessel, port.DockedVessel.VesselData.SceneID);
				}
			}
		}
		return node;
	}

	public void FabricateItem(ItemType itemType, ICargo fromCargo)
	{
		if (parentVessel != null && parentVessel.Fabricator != null && fromCargo != null)
		{
		}
	}

	public List<GeneratorDetails> GetGeneratorsDetails(bool changedOnly, long vesselGUID = -1L)
	{
		List<GeneratorDetails> ret = new List<GeneratorDetails>();
		foreach (KeyValuePair<VesselObjectID, Generator> kv in idGenerators)
		{
			if (((changedOnly && kv.Value.StatusChanged) || !changedOnly) && (vesselGUID == -1 || kv.Key.VesselGUID == vesselGUID))
			{
				GeneratorDetails gd = new GeneratorDetails
				{
					InSceneID = kv.Key.InSceneID,
					Status = kv.Value.Status,
					SecondaryStatus = kv.Value.SecondaryStatus,
					Output = kv.Value.Output,
					MaxOutput = kv.Value.MaxOutput,
					OutputRate = kv.Value.FixedConsumption && kv.Value.Status == SystemStatus.OnLine && kv.Value.OperationRate > float.Epsilon ? 1f : kv.Value.OperationRate,
					InputFactor = kv.Value.InputFactor,
					PowerInputFactor = kv.Value.PowerInputFactor,
					AutoRestart = kv.Value.CanReactivate,
					AuxDetails = kv.Value.GetAuxDetails()
				};
				ret.Add(gd);
				kv.Value.StatusChanged = false;
			}
		}
		return ret;
	}

	public List<SubSystemDetails> GetSubSystemsDetails(bool changedOnly, long vesselGUID = -1L)
	{
		List<SubSystemDetails> ret = new List<SubSystemDetails>();
		foreach (KeyValuePair<VesselObjectID, SubSystem> kv in idSubSystems)
		{
			if (((changedOnly && kv.Value.StatusChanged) || !changedOnly) && (vesselGUID == -1 || kv.Key.VesselGUID == vesselGUID))
			{
				ret.Add(new SubSystemDetails
				{
					InSceneID = kv.Key.InSceneID,
					Status = kv.Value.Status,
					SecondaryStatus = kv.Value.SecondaryStatus,
					OperationRate = kv.Value.FixedConsumption && kv.Value.Status == SystemStatus.OnLine && kv.Value.OperationRate > float.Epsilon ? 1f : kv.Value.OperationRate,
					InputFactor = kv.Value.InputFactor,
					PowerInputFactor = kv.Value.PowerInputFactor,
					AutoRestart = kv.Value.CanReactivate,
					AuxDetails = kv.Value.GetAuxDetails()
				});
				kv.Value.StatusChanged = false;
			}
		}
		return ret;
	}

	public List<RoomDetails> GetRoomsDetails(bool changedOnly, long vesselGUID = -1L)
	{
		List<RoomDetails> ret = new List<RoomDetails>();
		foreach (KeyValuePair<VesselObjectID, Room> kv in idRooms)
		{
			if (((changedOnly && kv.Value.StatusChanged) || !changedOnly) && (vesselGUID == -1 || kv.Key.VesselGUID == vesselGUID))
			{
				ret.Add(kv.Value.GetDetails());
				kv.Value.StatusChanged = false;
			}
		}
		return ret;
	}

	public List<DoorDetails> GetDoorsDetails(bool changedOnly, long vesselGUID = -1L)
	{
		List<DoorDetails> ret = new List<DoorDetails>();
		foreach (KeyValuePair<VesselObjectID, Door> kv in idDoors)
		{
			if (((changedOnly && kv.Value.StatusChanged) || !changedOnly) && (vesselGUID == -1 || kv.Key.VesselGUID == vesselGUID))
			{
				ret.Add(kv.Value.GetDetails());
				kv.Value.StatusChanged = false;
			}
		}
		return ret;
	}

	public List<ResourceContainerDetails> GetResourceContainersDetails(bool changedOnly, long vesselGUID = -1L)
	{
		List<ResourceContainerDetails> ret = new List<ResourceContainerDetails>();
		foreach (KeyValuePair<VesselObjectID, ResourceContainer> kv in idResourceContainers)
		{
			if (((changedOnly && kv.Value.StatusChanged) || !changedOnly) && (vesselGUID == -1 || kv.Key.VesselGUID == vesselGUID))
			{
				ret.Add(kv.Value.GetDetails());
				kv.Value.StatusChanged = false;
			}
		}
		return ret;
	}

	public List<ResourceContainer> GetResourceContainers()
	{
		return new List<ResourceContainer>(idResourceContainers.Values);
	}

	public List<VesselComponent> GetGenerators()
	{
		return new List<VesselComponent>(idGenerators.Values);
	}

	public List<VesselComponent> GetSubSystems()
	{
		return new List<VesselComponent>(idSubSystems.Values);
	}

	public List<VesselComponent> GetVesselComponents()
	{
		return GetGenerators().Concat(GetSubSystems()).ToList();
	}

	public List<Room> GetRooms()
	{
		return new List<Room>(idRooms.Values);
	}

	public void UpdateSystems(bool connectionsChanged = true, bool compoundRoomsChanged = true)
	{
		double duration = initialize ? 0.0 : (DateTime.UtcNow - prevSystemsUpdateTime).TotalSeconds;
		initialize = false;
		prevSystemsUpdateTime = DateTime.UtcNow;
		if (isCompoundDM)
		{
			parentVessel.DistributionManager.prevSystemsUpdateTime = prevSystemsUpdateTime;
			foreach (SpaceObjectVessel v2 in parentVessel.AllDockedVessels)
			{
				v2.DistributionManager.prevSystemsUpdateTime = prevSystemsUpdateTime;
			}
		}
		Dictionary<CompoundRoom, float[]> prevRoomValues = new Dictionary<CompoundRoom, float[]>();
		IEnumerable<CompoundRoom> cavs = compoundRooms.Values.Distinct();
		foreach (CompoundRoom cav in cavs)
		{
			prevRoomValues[cav] = new float[2] { cav.AirQuality, cav.AirPressure };
		}
		Dictionary<ResourceContainer, float> prevQuantities = idResourceContainers.Values.ToDictionary((ResourceContainer k) => k, (ResourceContainer v) => v.Compartments.Sum((CargoCompartmentData c) => c.Resources.Sum((CargoResourceData r) => r.Quantity)));
		Dictionary<GeneratorCapacitor, float> prevCapacities = (from m in idGenerators.Values
			where m is GeneratorCapacitor
			select m as GeneratorCapacitor).ToDictionary((GeneratorCapacitor k) => k, (GeneratorCapacitor v) => v.Capacity);
		foreach (Generator gen in idGenerators.Values)
		{
			gen.Update(duration);
		}
		foreach (SubSystem ss in idSubSystems.Values)
		{
			ss.Update(duration);
		}
		if (compoundRoomsChanged)
		{
			CreateCompoundRooms();
		}
		IEnumerable<CompoundRoom> compRooms = compoundRooms.Values.Distinct();
		Dictionary<CompoundRoom, float> prevAirQualities = compRooms.ToDictionary((CompoundRoom k) => k, (CompoundRoom v) => v.AirQuality);
		Dictionary<CompoundRoom, float> prevAirPressures = compRooms.ToDictionary((CompoundRoom k) => k, (CompoundRoom v) => v.AirPressure);
		UpdateCompoundRooms((float)duration);
		if (connectionsChanged)
		{
			UpdateConnections();
		}
		UpdateConsumers((float)duration);
		foreach (CompoundRoom cr in compRooms)
		{
			cr.AirQualityChangeRate = duration > 0.0 ? (float)((double)(cr.AirQuality - prevAirQualities[cr]) / duration) : 0f;
			cr.AirPressureChangeRate = duration > 0.0 ? (float)((double)(cr.AirPressure - prevAirPressures[cr]) / duration) : 0f;
		}
		foreach (KeyValuePair<GeneratorCapacitor, float> kvCap in prevCapacities)
		{
			kvCap.Key.CapacityChangeRate = duration > 0.0 ? (float)((double)(kvCap.Key.Capacity - kvCap.Value) / duration) : 0f;
		}
		foreach (KeyValuePair<ResourceContainer, float> kvRc in prevQuantities)
		{
			if (duration <= 0.0)
			{
				kvRc.Key.QuantityChangeRate = 0f;
				continue;
			}
			kvRc.Key.QuantityChangeRate = (float)((double)(kvRc.Key.Compartments.Sum((CargoCompartmentData c) => c.Resources.Sum((CargoResourceData r) => r.Quantity)) - kvRc.Value) / duration);
		}
	}

	private void UpdateConnections()
	{
		foreach (SubSystem ss in idSubSystems.Values)
		{
			ss.ConnectedProviders.Clear();
		}
		foreach (IResourceProvider rp2 in resourceProviderNodes.Keys)
		{
			if (rp2 is IResourceConsumer consumer)
			{
				consumer.ConnectedProviders.Clear();
			}
		}
		foreach (Room room in idRooms.Values)
		{
			room.ConnectedProviders.Clear();
		}
		foreach (KeyValuePair<IResourceProvider, DistributionNode> kv in resourceProviderNodes)
		{
			IResourceProvider rp = kv.Key;
			DistributionNode str = kv.Value;
			UpdateConnectedConsumers(rp, str);
		}
	}

	private void UpdateConsumers(float duration)
	{
		Dictionary<IResourceProvider, float> reservedCapacities = new Dictionary<IResourceProvider, float>();
		Dictionary<ResourceContainer, float> reservedQuantities = new Dictionary<ResourceContainer, float>();
		foreach (Generator gen2 in idGenerators.Values)
		{
			gen2.CheckStatus(1f, duration, standby: true, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (SubSystem ss in idSubSystems.Values.Where((SubSystem m) => m is VesselBaseSystem))
		{
			ss.CheckStatus(1f, duration, standby: true, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (SubSystem ss2 in idSubSystems.Values.Where((SubSystem m) => !(m is VesselBaseSystem)))
		{
			ss2.CheckStatus(1f, duration, standby: true, ref reservedCapacities, ref reservedQuantities);
		}
		reservedCapacities = new Dictionary<IResourceProvider, float>();
		reservedQuantities = new Dictionary<ResourceContainer, float>();
		foreach (SubSystem pc in from m in idSubSystems.Values
			where m.IsPowerConsumer && !(m is VesselBaseSystem)
			orderby consumersOrder.IndexOf(m.GetType()) descending
			select m)
		{
			pc.CheckStatus(pc.OperationRate, duration, pc.SecondaryStatus == SystemSecondaryStatus.Idle, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (SubSystem pc2 in idSubSystems.Values.Where((SubSystem m) => m is VesselBaseSystem))
		{
			pc2.CheckStatus(pc2.OperationRate, duration, pc2.SecondaryStatus == SystemSecondaryStatus.Idle, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (Generator gen4 in idGenerators.Values.Where((Generator m) => m.IsPowerConsumer && m.SecondaryStatus == SystemSecondaryStatus.Idle))
		{
			gen4.CheckStatus(1f, duration, standby: true, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (GeneratorCapacitor cap2 in from m in idGenerators.Values
			where m is GeneratorCapacitor
			select m as GeneratorCapacitor into m
			orderby m.Capacity / m.MaxCapacity
			select m)
		{
			if (cap2.Status == SystemStatus.OnLine && cap2.Capacity < cap2.MaxCapacity)
			{
				float sum = reservedCapacities.Where((KeyValuePair<IResourceProvider, float> m) => m.Key.OutputType == DistributionSystemType.Power).Sum((KeyValuePair<IResourceProvider, float> n) => n.Value);
				string debugText = null;
				cap2.CheckAvailableResources(1f, duration, standby: false, ref reservedCapacities, ref reservedQuantities, ref debugText);
				float diff = reservedCapacities.Where((KeyValuePair<IResourceProvider, float> m) => m.Key.OutputType == DistributionSystemType.Power).Sum((KeyValuePair<IResourceProvider, float> n) => n.Value) - sum;
				cap2.Capacity = MathHelper.Clamp(cap2.Capacity + diff * duration, 0f, cap2.MaxCapacity);
			}
		}
		foreach (SubSystem ss3 in idSubSystems.Values.Where((SubSystem m) => !m.IsPowerConsumer))
		{
			ss3.CheckStatus(ss3.OperationRate, duration, ss3.SecondaryStatus == SystemSecondaryStatus.Idle, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (ResourceContainer rc3 in idResourceContainers.Values.Where((ResourceContainer m) => m.NominalInput > 0f))
		{
			UpdateConsumerResourceContainer(rc3, duration, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (Room room in idRooms.Values)
		{
			UpdateConsumerRoom(room, duration, ref reservedCapacities, ref reservedQuantities);
			UpdateConsumerRoomFilter(room, duration, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (Generator gen3 in idGenerators.Values.Where((Generator m) => !m.IsPowerConsumer && m.SecondaryStatus == SystemSecondaryStatus.Idle))
		{
			gen3.CheckStatus(1f, duration, standby: true, ref reservedCapacities, ref reservedQuantities);
		}
		foreach (DistributionSystemType dst in Enum.GetValues(typeof(DistributionSystemType)))
		{
			AvailableResourceCapacities[dst] = 0f;
			AvailableResourceQuantities[dst] = 0f;
		}
		foreach (ResourceContainer rc2 in idResourceContainers.Values)
		{
			AvailableResourceCapacities[rc2.OutputType] += rc2.MaxOutput;
			if (rc2.IsInUse && rc2.GetCompartment().Resources.Count > 0)
			{
				AvailableResourceQuantities[rc2.OutputType] += rc2.GetCompartment().Resources[0].Quantity;
			}
		}
		foreach (Generator rp2 in idGenerators.Values)
		{
			AvailableResourceCapacities[rp2.OutputType] += rp2.MaxOutput;
		}
		foreach (IResourceProvider rp in resourceProviderNodes.Keys)
		{
			rp.Output = 0f;
		}
		foreach (KeyValuePair<IResourceProvider, float> kv2 in reservedCapacities)
		{
			kv2.Key.Output = kv2.Value;
			AvailableResourceCapacities[kv2.Key.OutputType] -= kv2.Key.Output;
		}
		foreach (Generator gen in idGenerators.Values)
		{
			if (!reservedCapacities.TryGetValue(gen, out var reserved))
			{
				((IResourceProvider)gen).Output = 0f;
			}
			else if (gen is GeneratorCapacitor cap && reserved > float.Epsilon)
			{
				cap.Capacity = MathHelper.Clamp(cap.Capacity - reserved * duration, 0f, float.MaxValue);
			}
		}
		foreach (ResourceContainer rc in idResourceContainers.Values)
		{
			if (!reservedCapacities.ContainsKey(rc))
			{
				((IResourceProvider)rc).Output = 0f;
			}
		}
		foreach (KeyValuePair<ResourceContainer, float> kv in reservedQuantities)
		{
			kv.Key.ConsumeResource(kv.Value);
		}
	}

	private void UpdateConsumerResourceContainer(ResourceContainer rc, float duration, ref Dictionary<IResourceProvider, float> reservedCapacities, ref Dictionary<ResourceContainer, float> reservedQuantities)
	{
		float resNeeded = rc.Compartments[0].Capacity - rc.Compartments[0].Resources.Sum((CargoResourceData m) => m.Quantity);
		if (!rc.ConnectedProviders.TryGetValue(rc.OutputType, out var resourceProviders))
		{
			return;
		}
		foreach (Generator gen in resourceProviders.Where((IResourceProvider m) => m is Generator generator && m.OutputType == rc.OutputType && generator.Status == SystemStatus.OnLine))
		{
			reservedCapacities.TryGetValue(gen, out var alreadyReserved);
			float capacityAvailable = gen.MaxOutput - alreadyReserved;
			float qty = System.Math.Min(System.Math.Min(resNeeded, capacityAvailable), rc.NominalInput) * duration;
			if (capacityAvailable <= float.Epsilon || qty <= float.Epsilon)
			{
				continue;
			}
			string dummyDebugText = "";
			float tempQty = qty;
			bool hasResources = false;
			for (int i = 0; i < 5; i++)
			{
				Dictionary<IResourceProvider, float> tempReservedCapacities = new Dictionary<IResourceProvider, float>(reservedCapacities);
				Dictionary<ResourceContainer, float> tempReservedQuantities = new Dictionary<ResourceContainer, float>(reservedQuantities);
				bool canWork = gen.CheckAvailableResources(MathHelper.Clamp(tempQty / gen.MaxOutput, 0f, 1f), duration, standby: false, ref tempReservedCapacities, ref tempReservedQuantities, ref dummyDebugText);
				float increment = qty / (float)System.Math.Pow(2.0, i + 1);
				if (canWork)
				{
					hasResources = true;
					reservedCapacities = tempReservedCapacities;
					reservedQuantities = tempReservedQuantities;
					if (tempQty == qty)
					{
						break;
					}
					tempQty += increment;
				}
				else
				{
					tempQty -= increment;
				}
			}
			if (hasResources)
			{
				reservedCapacities[gen] = alreadyReserved + tempQty;
				rc.ChangeQuantityBy(0, rc.Compartments[0].Resources[0].ResourceType, tempQty);
				if (rc is ResourceContainerAirTank airTank)
				{
					airTank.AirQuality = MathHelper.Clamp((tempQty * 1f + airTank.Compartments[0].Resources[0].Quantity * airTank.AirQuality) / (tempQty + airTank.Compartments[0].Resources[0].Quantity), 0f, 1f);
				}
			}
		}
	}

	private void UpdateConsumerRoom(Room room, float duration, ref Dictionary<IResourceProvider, float> reservedCapacities, ref Dictionary<ResourceContainer, float> reservedQuantities)
	{
		float targetPressure = room.AirPressure;
		if (room.TargetPressure.HasValue)
		{
			targetPressure = room.TargetPressure.Value;
		}
		else
		{
			if (room.EquilizePressureRoom == null)
			{
				return;
			}
			targetPressure = room.EquilizePressureRoom.AirPressure;
		}
		if (targetPressure.IsEpsilonEqual(room.AirPressure) || (targetPressure < 0f && room.AirPressure <= float.Epsilon))
		{
			room.TargetPressure = null;
			room.EquilizePressureRoom = null;
			room.StatusChanged = true;
			return;
		}
		float prevAirPressure = room.AirPressure;
		if (targetPressure > room.AirPressure)
		{
			if (room.CompoundRoom.AirConsumers.FirstOrDefault((IAirConsumer m) => m.AffectsQuantity) == null)
			{
				float airNeeded = room.CompoundRoom.Volume * (targetPressure - room.AirPressure);
				if (!room.ConnectedProviders.TryGetValue(DistributionSystemType.Air, out var resourceProviders))
				{
					return;
				}
				foreach (IResourceProvider rp in resourceProviders.Where((IResourceProvider m) => !(m is Generator generator) || generator.Status == SystemStatus.OnLine))
				{
					reservedCapacities.TryGetValue(rp, out var reservedCapacity);
					float capacityAvailable = rp.MaxOutput - reservedCapacity;
					if (rp is Generator || !(rp is ResourceContainerAirTank airTank2))
					{
						continue;
					}

					reservedQuantities.TryGetValue(airTank2, out var reservedQuantity);
					CargoResourceData resource = airTank2.Compartments[0].Resources.FirstOrDefault((CargoResourceData m) => m.ResourceType == ResourceType.Air);
					if (resource == null)
					{
						continue;
					}
					float quantityAvailable = resource.Quantity - reservedQuantity;
					float minQty = System.Math.Min(System.Math.Min(System.Math.Min(quantityAvailable, airNeeded), room.PressurizeSpeed * duration), (targetPressure - room.AirPressure) * room.CompoundRoom.Volume);
					if (minQty > float.Epsilon)
					{
						float qty2 = System.Math.Abs(airTank2.ChangeQuantityBy(0, resource.ResourceType, 0f - minQty));
						float oldAirQty2 = room.CompoundRoom.Volume * room.CompoundRoom.AirPressure;
						room.CompoundRoom.AirQuality = (qty2 * airTank2.AirQuality + oldAirQty2 * room.AirQuality) / (qty2 + oldAirQty2);
						room.CompoundRoom.AirPressure += qty2 / room.CompoundRoom.Volume;
						if (room.AirPressure + float.Epsilon >= targetPressure)
						{
							break;
						}
					}
				}
			}
		}
		else if (targetPressure >= 0f)
		{
			if (!room.ConnectedProviders.TryGetValue(DistributionSystemType.Air, out var resourceProviders2))
			{
				return;
			}
			float airToDisplace = System.Math.Min(room.CompoundRoom.Volume * room.AirPressure, room.DepressurizeSpeed * duration);
			foreach (ResourceContainerAirTank airTank in resourceProviders2.Where((IResourceProvider m) => m is ResourceContainerAirTank))
			{
				float fillQty = System.Math.Min(airToDisplace, (room.AirPressure - targetPressure) * room.CompoundRoom.Volume);
				float oldAirQty = airTank.Compartments[0].Resources[0].Quantity;
				float qty = airTank.ChangeQuantityBy(0, ResourceType.Air, fillQty);
				if (oldAirQty <= float.Epsilon)
				{
					airTank.AirQuality = room.AirQuality;
				}
				else if (qty > float.Epsilon)
				{
					airTank.AirQuality = (qty * room.AirQuality + oldAirQty * airTank.AirQuality) / (qty + oldAirQty);
				}
				room.CompoundRoom.AirPressure -= qty / room.CompoundRoom.Volume;
				airToDisplace -= qty;
				if (airToDisplace <= float.Epsilon)
				{
					break;
				}
			}
		}
		else
		{
			targetPressure = 0f;
			room.CompoundRoom.AirPressure -= room.VentSpeed * duration / room.CompoundRoom.Volume;
		}
		if (room.AirPressure.IsEpsilonEqual(prevAirPressure))
		{
			room.TargetPressure = null;
			room.EquilizePressureRoom = null;
			room.StatusChanged = true;
		}
	}

	private void UpdateConsumerRoomFilter(Room room, float duration, ref Dictionary<IResourceProvider, float> reservedCapacities, ref Dictionary<ResourceContainer, float> reservedQuantities)
	{
		float qtyForScrubbing = room.CompoundRoom.Volume * room.CompoundRoom.AirPressure * (1f - room.CompoundRoom.AirQuality);
		if (!room.AirFiltering || qtyForScrubbing <= float.Epsilon || room.CompoundRoom.AirQuality + float.Epsilon >= 1f || !room.ConnectedProviders.TryGetValue(DistributionSystemType.ScrubbedAir, out var resourceProviders))
		{
			return;
		}
		foreach (GeneratorScrubber scrubber in resourceProviders.Where((IResourceProvider m) => m is GeneratorScrubber scrubber && scrubber.Status == SystemStatus.OnLine))
		{
			reservedCapacities.TryGetValue(scrubber, out var alreadyReserved);
			float scrubberCap = scrubber.MaxOutput - alreadyReserved;
			float qty = System.Math.Min(scrubberCap, qtyForScrubbing);
			if (qty <= float.Epsilon)
			{
				continue;
			}
			string dummyDebugText = "";
			float tempQty = qty;
			bool hasResources = false;
			for (int i = 0; i < 5; i++)
			{
				Dictionary<IResourceProvider, float> tempReservedCapacities = new Dictionary<IResourceProvider, float>(reservedCapacities);
				Dictionary<ResourceContainer, float> tempReservedQuantities = new Dictionary<ResourceContainer, float>(reservedQuantities);
				bool canWork = scrubber.CheckAvailableResources(MathHelper.Clamp(tempQty / scrubber.MaxOutput, 0f, 1f), duration, standby: false, ref tempReservedCapacities, ref tempReservedQuantities, ref dummyDebugText);
				float increment = qty / (float)System.Math.Pow(2.0, i + 1);
				if (canWork)
				{
					hasResources = true;
					if (!reservedCapacities.ContainsKey(scrubber))
					{
						reservedCapacities = tempReservedCapacities;
						reservedQuantities = tempReservedQuantities;
					}
					if (tempQty == qty)
					{
						break;
					}
					tempQty += increment;
				}
				else
				{
					tempQty -= increment;
				}
			}
			if (!hasResources || !(room.CompoundRoom.AirQuality < 1f))
			{
				continue;
			}
			reservedCapacities[scrubber] = alreadyReserved + tempQty;
			float scrubQty = tempQty / (1f - room.CompoundRoom.AirQuality);
			float airQty = room.CompoundRoom.Volume * room.CompoundRoom.AirPressure;
			room.CompoundRoom.AirQuality = airQty > float.Epsilon ? (scrubQty + (airQty - scrubQty) * room.CompoundRoom.AirQuality) / airQty : 0f;
			List<MachineryPart> cartridges = scrubber.MachineryParts.Values.Where((MachineryPart m) => m != null && m.PartType == MachineryPartType.CarbonFilters).ToList();
			if (cartridges.Count > 0)
			{
				float healthDec = scrubber.ScrubberCartridgeConsumption * tempQty / (float)cartridges.Count;
				foreach (MachineryPart cartridge in cartridges)
				{
					float prevHealth = cartridge.Health;
					cartridge.Health -= healthDec;
					if ((int)prevHealth != (int)cartridge.Health || (prevHealth > 0f && cartridge.Health == 0f) || (cartridge.Health != prevHealth && Server.SolarSystemTime - cartridge.DynamicObj.LastStatsSendTime > 10.0))
					{
						cartridge.DynamicObj.SendStatsToClient();
					}
				}
			}
			qtyForScrubbing -= scrubQty;
			if (!(qtyForScrubbing <= float.Epsilon))
			{
				continue;
			}
			break;
		}
	}

	private void UpdateConnectedConsumers(IResourceProvider resourceProvider, DistributionNode node, HashSet<DistributionNode> traversedNodes = null)
	{
		if (traversedNodes == null)
		{
			traversedNodes = new HashSet<DistributionNode>();
		}
		if (!traversedNodes.Add(node))
		{
			return;
		}
		HashSet<IResourceUser> exclusiveConsumers = new HashSet<IResourceUser>();
		HashSet<IResourceUser> consumers = new HashSet<IResourceUser>();
		foreach (IResourceUser resourceUser2 in node.ResourceUsers)
		{
			if (resourceUser2 is IResourceConsumer consumer && consumer.ResourceRequirements.ContainsKey(resourceProvider.OutputType))
			{
				HashSet<ResourceContainer> containers = null;
				consumer.ResourceContainers.TryGetValue(resourceProvider.OutputType, out containers);
				if (resourceProvider is ResourceContainer && containers != null && containers.Contains(resourceProvider))
				{
					exclusiveConsumers.Add(consumer);
				}
				else if (containers == null)
				{
					consumers.Add(consumer);
				}
			}
			else if ((resourceProvider is Generator && resourceUser2 is ResourceContainer container && container.NominalInput > 0f && container.OutputType == resourceProvider.OutputType) || (resourceUser2 is Room && resourceProvider.OutputType == DistributionSystemType.Air) || (resourceUser2 is Room && resourceProvider.OutputType == DistributionSystemType.ScrubbedAir))
			{
				consumers.Add(resourceUser2);
			}
		}
		HashSet<IResourceUser> list = exclusiveConsumers.Count > 0 ? exclusiveConsumers : consumers;
		foreach (IResourceUser resourceUser in list)
		{
			resourceProvider.ConnectedConsumers.Add(resourceUser);
			if (!(resourceUser is GeneratorCapacitor) || !(resourceProvider is GeneratorCapacitor))
			{
				SortedSet<IResourceProvider> resourceProviders = null;
				if (!resourceUser.ConnectedProviders.TryGetValue(resourceProvider.OutputType, out resourceProviders))
				{
					resourceProviders = resourceUser.ConnectedProviders[resourceProvider.OutputType] = new SortedSet<IResourceProvider>(new ResourceproviderComparer());
				}
				if (!resourceProviders.Contains(resourceProvider))
				{
					resourceProviders.Add(resourceProvider);
				}
			}
		}
		foreach (DistributionNode i in node.LinkedNodes)
		{
			UpdateConnectedConsumers(resourceProvider, i, traversedNodes);
		}
	}

	public void RemovePlayerFromRoom(Player player)
	{
		try
		{
			if (playerRooms.TryGetValue(player, out var fromRoom))
			{
				fromRoom.RemoveAirConsumer(player);
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	private void CreateCompoundRooms()
	{
		short compoundRoomId = 0;
		compoundRooms = new Dictionary<Room, CompoundRoom>();
		foreach (Room room in idRooms.Values)
		{
			if (compoundRooms.ContainsKey(room))
			{
				continue;
			}
			CompoundRoom compoundRoom = new CompoundRoom(room, compoundRoomId++);
			foreach (Room r in compoundRoom.Rooms)
			{
				compoundRooms[r] = compoundRoom;
			}
		}
	}

	private void UpdateCompoundRooms(float duration)
	{
		foreach (CompoundRoom cav in compoundRooms.Values.Distinct())
		{
			float qualityLoss = 0f;
			float quantityLoss = 0f;
			List<AirConsumerFire> firesToRemove = new List<AirConsumerFire>();
			foreach (IAirConsumer cons2 in cav.AirConsumers.Where((IAirConsumer m) => m is AirConsumerFire))
			{
				if (cav.FireCanBurn)
				{
					qualityLoss += cav.AirPressure > 0f ? cons2.AirQualityDegradationRate / cav.Volume / cav.AirPressure * duration : 0f;
					quantityLoss += cons2.AirQuantityDecreaseRate * duration;
				}
				else if (!(cons2 as AirConsumerFire).Persistent)
				{
					firesToRemove.Add((AirConsumerFire)cons2);
				}
			}
			cav.AirQuality -= qualityLoss;
			cav.AirPressure = (cav.AirPressure * cav.Volume - quantityLoss) / cav.Volume;
			foreach (AirConsumerFire fire in firesToRemove)
			{
				cav.RemoveAirConsumer(fire);
			}
			qualityLoss = 0f;
			quantityLoss = 0f;
			if (duration > 0f)
			{
				foreach (IAirConsumer cons in cav.AirConsumers.Where((IAirConsumer m) => !(m is AirConsumerFire)))
				{
					qualityLoss += cav.AirPressure > 0f ? cons.AirQualityDegradationRate / cav.Volume / cav.AirPressure * duration : 0f;
					quantityLoss += cons.AirQuantityDecreaseRate * duration;
				}
			}
			cav.AirQuality -= qualityLoss;
			cav.AirPressure = (cav.AirPressure * cav.Volume - quantityLoss) / cav.Volume;
		}
	}

	public static Dictionary<DistributionSystemType, ResourceRequirement> ResourceRequirementsToDictionary(ResourceRequirement[] rrArray)
	{
		Dictionary<DistributionSystemType, ResourceRequirement> dict = new Dictionary<DistributionSystemType, ResourceRequirement>();
		if (rrArray != null)
		{
			foreach (ResourceRequirement rr in rrArray)
			{
				dict[rr.ResourceType] = rr;
			}
		}
		return dict;
	}
}
