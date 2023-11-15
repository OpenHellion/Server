using System;
using System.Collections.Generic;
using System.Linq;
using OpenHellion.Networking;
using ZeroGravity.BulletPhysics;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public class Asteroid : SpaceObjectVessel, IPersistantObject
{
	public int ColliderIndex = 1;

	public Dictionary<int, AsteroidMiningPoint> MiningPoints = new Dictionary<int, AsteroidMiningPoint>();

	public override SpaceObjectType ObjectType => SpaceObjectType.Asteroid;

	public Asteroid(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid, initializeOrbit, position, velocity, forward, up)
	{
		Mass = 100000000000.0;
	}

	public static Asteroid CreateNewAsteroid(GameScenes.SceneId sceneID, string registration = "", long asteroidGUID = -1L, List<long> nearArtificialBodyGUIDs = null, List<long> celestialBodyGUIDs = null, Vector3D? positionOffset = null, Vector3D? velocityAtPosition = null, QuaternionD? localRotation = null, string vesselTag = "", bool checkPosition = true, float? AsteroidResourcesMultiplier = null, double distanceFromSurfacePercMin = 0.03, double distanceFromSurfacePercMax = 0.3, SpawnRuleOrbit spawnRuleOrbit = null, double celestialBodyDeathDistanceMultiplier = 1.5, double artificialBodyDistanceCheck = 100.0, bool isDebrisFragment = false)
	{
		Vector3D astPos = Vector3D.Zero;
		Vector3D astVel = Vector3D.Zero;
		Vector3D astForward = Vector3D.Forward;
		Vector3D astUp = Vector3D.Up;
		OrbitParameters orbit = null;
		Asteroid asteroid = new Asteroid(asteroidGUID < 0 ? GUIDFactory.NextVesselGUID() : asteroidGUID, initializeOrbit: false, astPos, astVel, astForward, astUp);
		asteroid.VesselData = new VesselData
		{
			SceneID = sceneID,
			VesselRegistration = registration,
			VesselName = "",
			Tag = vesselTag,
			CollidersCenterOffset = Vector3D.Zero.ToFloatArray(),
			IsDebrisFragment = isDebrisFragment,
			CreationSolarSystemTime = Server.SolarSystemTime
		};
		asteroid.IsDebrisFragment = isDebrisFragment;
		asteroid.ReadInfoFromJson();
		asteroid.VesselData.RadarSignature = asteroid.RadarSignature;
		Server.Instance.PhysicsController.CreateAndAddRigidBody(asteroid);
		Server.Instance.SolarSystem.GetSpawnPosition(SpaceObjectType.Asteroid, asteroid.Radius, checkPosition, out astPos, out astVel, out astForward, out astUp, nearArtificialBodyGUIDs, celestialBodyGUIDs, positionOffset, velocityAtPosition, localRotation, distanceFromSurfacePercMin, distanceFromSurfacePercMax, spawnRuleOrbit, celestialBodyDeathDistanceMultiplier, artificialBodyDistanceCheck, out orbit);
		asteroid.InitializeOrbit(astPos, astVel, astForward, astUp, orbit);
		if (registration.IsNullOrEmpty())
		{
			asteroid.VesselData.VesselRegistration = Server.NameGenerator.GenerateObjectRegistration(SpaceObjectType.Asteroid, asteroid.Orbit.Parent.CelestialBody, sceneID);
		}
		if (asteroid.MiningPoints.Count > 0)
		{
			CelestialBody cb = asteroid.Orbit.Parent.CelestialBody;
			while (cb == null || cb.AsteroidResources == null || cb.AsteroidResources.Count == 0)
			{
				cb = cb.Orbit.Parent.CelestialBody;
			}
			List<ResourceMinMax> asteroidResources = cb.AsteroidResources;
			if (asteroidResources != null && asteroidResources.Count > 0)
			{
				List<KeyValuePair<ResourceType, float>> resources = (from m in cb.AsteroidResources
					select new KeyValuePair<ResourceType, float>(m.Type, MathHelper.RandomRange(m.Min, m.Max) * (AsteroidResourcesMultiplier.HasValue ? AsteroidResourcesMultiplier.Value : 1f)) into m
					orderby 0f - m.Value
					where m.Value > 0f
					select m).ToList();
				if (resources.Count > 0)
				{
					int index = 0;
					foreach (AsteroidMiningPoint amp in from m in asteroid.MiningPoints.Values
						orderby 0f - m.Size, MathHelper.RandomNextDouble()
						select m)
					{
						if (index >= resources.Count)
						{
							index = 0;
						}
						amp.ResourceType = resources.ElementAt(index++).Key;
						amp.GasBurstTimeMin = cb.AsteroidGasBurstTimeMin;
						amp.GasBurstTimeMax = cb.AsteroidGasBurstTimeMax;
					}
					foreach (ResourceType rt in asteroid.MiningPoints.Values.Select((AsteroidMiningPoint m) => m.ResourceType).Distinct())
					{
						float avail = resources.FirstOrDefault((KeyValuePair<ResourceType, float> m) => m.Key == rt).Value;
						List<AsteroidMiningPoint> miningPoints = asteroid.MiningPoints.Values.Where((AsteroidMiningPoint m) => m.ResourceType == rt).ToList();
						while (avail > 0f)
						{
							foreach (AsteroidMiningPoint mp in miningPoints)
							{
								float qty = 10f * mp.Size;
								mp.Quantity += qty;
								mp.MaxQuantity = mp.Quantity;
								avail -= qty;
								if (avail <= 0f)
								{
									break;
								}
							}
						}
					}
				}
			}
		}
		Server.Instance.Add(asteroid);
		asteroid.SetPhysicsParameters();
		return asteroid;
	}

	public Dictionary<ResourceType, float> GetAllResources()
	{
		Dictionary<ResourceType, float> retVal = new Dictionary<ResourceType, float>();
		foreach (AsteroidMiningPoint mp in MiningPoints.Values)
		{
			if (retVal.ContainsKey(mp.ResourceType))
			{
				retVal[mp.ResourceType] += mp.Quantity;
			}
			else
			{
				retVal.Add(mp.ResourceType, mp.Quantity);
			}
		}
		return retVal;
	}

	public override void AddPlayerToCrew(Player pl)
	{
		if (!VesselCrew.Contains(pl))
		{
			VesselCrew.Add(pl);
		}
	}

	public override void RemovePlayerFromCrew(Player pl, bool checkDetails = false)
	{
		VesselCrew.Remove(pl);
	}

	public override bool HasPlayerInCrew(Player pl)
	{
		return VesselCrew.Contains(pl);
	}

	public void ReadInfoFromJson()
	{
		AsteroidSceneData asd = StaticData.AsteroidDataList.Find((AsteroidSceneData x) => x.ItemID == (short)base.SceneID);
		Radius = asd.Radius;
		RadarSignature = asd.RadarSignature;
		foreach (AsteroidMiningPointData ampd in asd.MiningPoints)
		{
			MiningPoints[ampd.InSceneID] = new AsteroidMiningPoint(this, ampd);
		}
		if (asd.Colliders == null)
		{
			return;
		}
		if (asd.Colliders.PrimitiveCollidersData != null && asd.Colliders.PrimitiveCollidersData.Count > 0)
		{
			foreach (PrimitiveColliderData data2 in asd.Colliders.PrimitiveCollidersData)
			{
				if (data2.Type == ColliderDataType.Box)
				{
					PrimitiveCollidersData.Add(new VesselPrimitiveColliderData
					{
						Type = data2.Type,
						CenterPosition = data2.Center.ToVector3D(),
						Bounds = data2.Size.ToVector3D(),
						AffectingCenterOfMass = data2.AffectingCenterOfMass
					});
				}
				else if (data2.Type == ColliderDataType.Sphere)
				{
					PrimitiveCollidersData.Add(new VesselPrimitiveColliderData
					{
						Type = data2.Type,
						CenterPosition = data2.Center.ToVector3D(),
						Bounds = data2.Size.ToVector3D(),
						AffectingCenterOfMass = data2.AffectingCenterOfMass
					});
				}
			}
		}
		if (asd.Colliders.MeshCollidersData == null)
		{
			return;
		}
		foreach (MeshData data in asd.Colliders.MeshCollidersData)
		{
			MeshCollidersData.Add(new VesselMeshColliderData
			{
				AffectingCenterOfMass = data.AffectingCenterOfMass,
				ColliderID = ColliderIndex++,
				Indices = data.Indices,
				Vertices = data.Vertices.GetVertices(),
				CenterPosition = data.CenterPosition.ToVector3D(),
				Bounds = data.Bounds.ToVector3D(),
				Rotation = data.Rotation.ToQuaternionD(),
				Scale = data.Scale.ToVector3D()
			});
		}
	}

	public override void Destroy()
	{
		Server.Instance.PhysicsController.RemoveRigidBody(this);
		Server.Instance.Remove(this);
		DisconectListener();
		base.Destroy();
	}

	private void DisconectListener()
	{
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		bool isDummy = (pl.Position - Position).SqrMagnitude > 100000000.0;
		return new SpawnAsteroidResponseData
		{
			GUID = GUID,
			Data = VesselData,
			Radius = Radius,
			IsDummy = isDummy,
			MiningPoints = MiningPoints.Values.Select((AsteroidMiningPoint m) => m.GetDetails()).ToList()
		};
	}

	public override InitializeSpaceObjectMessage GetInitializeMessage()
	{
		InitializeSpaceObjectMessage msg = new InitializeSpaceObjectMessage();
		msg.GUID = GUID;
		msg.DynamicObjects = new List<DynamicObjectDetails>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			msg.DynamicObjects.Add(dobj.GetDetails());
		}
		msg.Corpses = new List<CorpseDetails>();
		foreach (Corpse cobj in Corpses.Values)
		{
			msg.Corpses.Add(cobj.GetDetails());
		}
		msg.Characters = new List<CharacterDetails>();
		foreach (Player pl in VesselCrew)
		{
			msg.Characters.Add(pl.GetDetails());
		}
		return msg;
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataAsteroid data = new PersistenceObjectDataAsteroid();
		data.GUID = GUID;
		data.OrbitData = new OrbitData();
		Orbit.FillOrbitData(ref data.OrbitData);
		data.Name = VesselData.VesselRegistration;
		data.Tag = VesselData.Tag;
		data.SceneID = base.SceneID;
		data.IsAlwaysVisible = base.IsAlwaysVisible;
		data.Forward = Forward.ToArray();
		data.Up = Up.ToArray();
		data.Rotation = Rotation.ToArray();
		data.MiningPoints = MiningPoints.Values.Select((AsteroidMiningPoint m) => m.GetDetails()).ToList();
		return data;
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			PersistenceObjectDataAsteroid data = persistenceData as PersistenceObjectDataAsteroid;
			VesselData = new VesselData();
			VesselData.CollidersCenterOffset = Vector3D.Zero.ToFloatArray();
			VesselData.VesselRegistration = data.Name;
			VesselData.Tag = data.Tag;
			VesselData.SceneID = data.SceneID;
			base.IsAlwaysVisible = data.IsAlwaysVisible;
			ReadInfoFromJson();
			VesselData.RadarSignature = RadarSignature;
			Server.Instance.PhysicsController.CreateAndAddRigidBody(this);
			InitializeOrbit(Vector3D.Zero, Vector3D.One, data.Forward.ToVector3D(), data.Up.ToVector3D());
			if (data.OrbitData != null)
			{
				Orbit.ParseNetworkData(data.OrbitData, resetOrbit: true);
			}
			Rotation = data.Rotation.ToVector3D();
			foreach (AsteroidMiningPointDetails det in data.MiningPoints)
			{
				if (MiningPoints.TryGetValue(det.InSceneID, out var mp))
				{
					mp.ResourceType = det.ResourceType;
					mp.Quantity = det.Quantity;
					mp.MaxQuantity = det.MaxQuantity;
				}
			}
			Server.Instance.Add(this);
			SetPhysicsParameters();
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public override void UpdateVesselSystems()
	{
		base.UpdateVesselSystems();
		foreach (AsteroidMiningPoint amp in MiningPoints.Values)
		{
			if (amp.CheckGasBurst())
			{
				NetworkController.Instance.SendToClientsSubscribedTo(new MiningPointStatsMessage
				{
					ID = amp.ID,
					GasBurst = true
				}, -1L, this);
				break;
			}
			if (amp.StatusChanged)
			{
				NetworkController.Instance.SendToClientsSubscribedTo(new MiningPointStatsMessage
				{
					ID = amp.ID,
					Quantity = amp.Quantity
				}, -1L, this);
				amp.StatusChanged = false;
			}
		}
	}
}
