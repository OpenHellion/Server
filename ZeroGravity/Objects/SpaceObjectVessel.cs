using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BulletSharp;
using BulletSharp.Math;
using OpenHellion.Net;
using ZeroGravity.BulletPhysics;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public abstract class SpaceObjectVessel : ArtificialBody
{
	public class SpawnObjectsWithChance
	{
		public int InSceneID;

		public float Chance;
	}

	private class DockUndockPlayerData
	{
		private class PlayerItem
		{
			public Player Player;

			public SpaceObjectVessel Parent;

			public Vector3D PosFromParent;

			public QuaternionD RotFromParent;
		}

		private readonly List<PlayerItem> players = new List<PlayerItem>();

		public static DockUndockPlayerData GetPlayerData(params SpaceObjectVessel[] vessels)
		{
			DockUndockPlayerData retVal = new DockUndockPlayerData();
			foreach (SpaceObjectVessel vs in vessels)
			{
				SpaceObjectVessel ves = vs;
				if (ves.DockedToMainVessel != null)
				{
					ves = ves.DockedToMainVessel;
				}
				foreach (Player pl2 in ves.VesselCrew)
				{
					retVal.players.Add(new PlayerItem
					{
						Player = pl2,
						Parent = ves,
						PosFromParent = pl2.LocalPosition + ves.VesselData.CollidersCenterOffset.ToVector3D(),
						RotFromParent = pl2.LocalRotation
					});
				}
				foreach (SpaceObjectVessel childVes in ves.AllDockedVessels)
				{
					foreach (Player pl in childVes.VesselCrew)
					{
						retVal.players.Add(new PlayerItem
						{
							Player = pl,
							Parent = childVes,
							PosFromParent = childVes.RelativeRotationFromMainParent * (pl.LocalPosition + ves.VesselData.CollidersCenterOffset.ToVector3D() - childVes.RelativePositionFromMainParent),
							RotFromParent = pl.LocalRotation * childVes.RelativeRotationFromMainParent.Inverse()
						});
					}
				}
			}
			return retVal;
		}

		public void ModifyPlayersPositionAndRotation()
		{
			if (players == null || players.Count == 0)
			{
				return;
			}
			foreach (PlayerItem it in players)
			{
				Vector3D posDiff = Vector3D.Zero;
				QuaternionD rotDiff = QuaternionD.Identity;
				if (it.Parent.DockedToMainVessel != null)
				{
					posDiff = -it.Parent.DockedToMainVessel.VesselData.CollidersCenterOffset.ToVector3D() + it.Parent.RelativePositionFromMainParent + it.Parent.RelativeRotationFromMainParent * it.PosFromParent - it.Player.LocalPosition;
					rotDiff = it.Player.LocalRotation.Inverse() * (it.Parent.RelativeRotationFromMainParent * it.RotFromParent);
				}
				else
				{
					posDiff = -it.Parent.VesselData.CollidersCenterOffset.ToVector3D() + it.PosFromParent - it.Player.LocalPosition;
					rotDiff = it.Player.LocalRotation.Inverse() * it.RotFromParent;
				}
				it.Player.ModifyLocalPositionAndRotation(posDiff, rotDiff);
				if (NetworkController.IsPlayerConnected(it.Player.Guid) && it.Player.IsAlive && it.Player.EnvironmentReady && it.Player.PlayerReady)
				{
					it.Player.SetDockUndockCorrection(posDiff, rotDiff);
				}
			}
		}
	}

	public List<VesselPrimitiveColliderData> PrimitiveCollidersData = new List<VesselPrimitiveColliderData>();

	public List<VesselMeshColliderData> MeshCollidersData = new List<VesselMeshColliderData>();

	public RigidBody RigidBody;

	public static double ArenaRescueTime = TimeSpan.FromMinutes(30.0).TotalSeconds;

	public Vector3D RelativePositionFromMainParent;

	public QuaternionD RelativeRotationFromMainParent;

	public Vector3D RelativePositionFromParent;

	public QuaternionD RelativeRotationFromParent;

	public SpaceObjectVessel DockedToMainVessel { get; private set; }

	public SpaceObjectVessel DockedToVessel;

	public List<SpaceObjectVessel> AllDockedVessels = new List<SpaceObjectVessel>();

	public List<SpaceObjectVessel> DockedVessels = new List<SpaceObjectVessel>();

	public VesselData VesselData;

	public List<SpawnObjectsWithChance> SpawnChance = new List<SpawnObjectsWithChance>();

	public List<Door> Doors = new List<Door>();

	public ImmutableArray<VesselDockingPort> DockingPorts;

	public List<NameTagData> NameTags = new List<NameTagData>();

	public List<Room> Rooms = new List<Room>();

	public List<VesselComponent> Systems = new List<VesselComponent>();

	public List<VesselRepairPoint> RepairPoints = new List<VesselRepairPoint>();

	public float MaxHealth;

	protected float _Health;

	public float BaseArmor;

	public float AddedArmor;

	public bool InvulnerableWhenDocked;

	public float Temperature;

	public double Mass;

	public float HeatCollectionFactor;

	public float HeatDissipationFactor;

	public List<ShipSpawnPoint> SpawnPoints = new List<ShipSpawnPoint>();

	public HashSet<Player> VesselCrew = new HashSet<Player>();

	public List<AuthorizedPerson> AuthorizedPersonel = new List<AuthorizedPerson>();

	public DistributionManager DistributionManager = null;

	public DistributionManager CompoundDistributionManager;

	public Dictionary<VesselObjectID, AttachPointType> AttachPointsTypes = new Dictionary<VesselObjectID, AttachPointType>();

	public Dictionary<int, VesselAttachPoint> AttachPoints = new Dictionary<int, VesselAttachPoint>();

	public bool IsPrefabStationVessel;

	public bool IsInvulnerable;

	public bool DockingControlsDisabled;

	public bool AutoStabilizationDisabled;

	public bool SecurityPanelsLocked;

	public long StartingSetId = -1L;

	public double DecayGraceTimer;

	public double JunkItemsCleanupTimer = Server.JunkItemsCleanupInterval;

	public SubSystemRCS RCS = null;

	public SubSystemEngine Engine = null;

	public SubSystemFTL FTL = null;

	public SubSystemRefinery Refinery = null;

	public SubSystemFabricator Fabricator = null;

	public GeneratorCapacitor Capacitor = null;

	public CargoBay CargoBay = null;

	public VesselBaseSystem VesselBaseSystem;

	public string EmblemId = "";

	public List<SceneTriggerExecutor> SceneTriggerExecutors = new List<SceneTriggerExecutor>();

	public VesselDamageType LastVesselDamageType;

	public bool IsDebrisFragment = false;

	protected double? prevDestructionSolarSystemTime = null;

	public SelfDestructTimer SelfDestructTimer;

	public QuestTrigger.QuestTriggerID QuestTriggerID = null;

	public bool ConnectionsChanged;

	public GameScenes.SceneId SceneID => VesselData != null ? VesselData.SceneID : GameScenes.SceneId.None;

	public bool IsDocked => DockedToMainVessel != null;

	public bool IsMainVessel => MainVessel == this;

	public SpaceObjectVessel MainVessel
	{
		get
		{
			if (DockedToMainVessel != null)
			{
				return DockedToMainVessel;
			}
			return this;
		}
	}

	public List<SpaceObjectVessel> AllVessels
	{
		get
		{
			List<SpaceObjectVessel> allVessels = new List<SpaceObjectVessel>();
			allVessels.Add(MainVessel);
			allVessels.AddRange(MainVessel.AllDockedVessels);
			return allVessels;
		}
	}

	public string FullName => VesselData != null ? VesselData.VesselRegistration + " " + VesselData.VesselName : "";

	public float Health
	{
		get
		{
			return _Health;
		}
	}

	public bool HasSpawnPoints => SpawnPoints is { Count: > 0 };

	public bool HasSecuritySystem { get; protected set; }

	public float Armor => BaseArmor + AddedArmor;

	public override Vector3D Position
	{
		get
		{
			if (IsMainVessel)
			{
				return base.Position;
			}
			return MainVessel.Position + QuaternionD.LookRotation(MainVessel.Forward, MainVessel.Up) * RelativePositionFromMainParent;
		}
	}

	public override Vector3D Forward
	{
		get
		{
			if (IsMainVessel)
			{
				return base.Forward;
			}
			return RelativeRotationFromMainParent * MainVessel.Forward;
		}
	}

	public override Vector3D Up
	{
		get
		{
			if (IsMainVessel)
			{
				return base.Up;
			}
			return RelativeRotationFromMainParent * MainVessel.Up;
		}
	}

	public bool EngineOnLine
	{
		get
		{
			if (Engine != null)
			{
				return Engine.Status == SystemStatus.OnLine;
			}
			return false;
		}
	}

	public float EngineAcceleration
	{
		get
		{
			if (Engine != null)
			{
				return (float)(Engine.Acceleration * Mass / MainVessel.GetCompoundMass());
			}
			return 0f;
		}
	}

	public float EngineReverseAcceleration
	{
		get
		{
			if (Engine != null)
			{
				return (float)(Engine.ReverseAcceleration * Mass / MainVessel.GetCompoundMass());
			}
			return 0f;
		}
	}

	public float RCSAcceleration
	{
		get
		{
			if (RCS != null)
			{
				double compoundMass = MainVessel.GetCompoundMass();
				return (float)(RCS.Acceleration * Mass / compoundMass);
			}
			return 0f;
		}
	}

	public float RCSRotationAcceleration
	{
		get
		{
			if (RCS != null)
			{
				return (float)(RCS.RotationAcceleration * Mass / MainVessel.GetCompoundMass());
			}
			return 0f;
		}
	}

	public float RCSRotationStabilization
	{
		get
		{
			if (RCS != null)
			{
				return (float)(RCS.RotationStabilization * Mass / MainVessel.GetCompoundMass());
			}
			return 0f;
		}
	}

	public DistributionManager MainDistributionManager
	{
		get
		{
			if (IsMainVessel)
			{
				if (CompoundDistributionManager != null)
				{
					return CompoundDistributionManager;
				}
				return DistributionManager;
			}
			return MainVessel.MainDistributionManager;
		}
	}

	public bool IsWarping => MainVessel.CurrentCourse is { IsInProgress: true } || MainVessel.AllDockedVessels.FirstOrDefault((SpaceObjectVessel m) => m.CurrentCourse is { IsInProgress: true }) != null;

	public float ExposureDamage => StaticData.GetVesselExposureDamage(MainVessel.Orbit.Position.Magnitude);

	public float BaseSunExposure => StaticData.GetBaseSunExposure(MainVessel.Orbit.Position.Magnitude);

	public abstract void AddPlayerToCrew(Player pl);

	public abstract void RemovePlayerFromCrew(Player pl, bool checkDetails = false);

	public abstract bool HasPlayerInCrew(Player pl);

	public bool HasSpawnPointsInHierarchy()
	{
		if (SpawnPoints is { Count: > 0 })
		{
			return true;
		}
		if (DockedToMainVessel != null)
		{
			foreach (SpaceObjectVessel itemA in DockedToMainVessel.AllDockedVessels)
			{
				if (itemA.SpawnPoints is { Count: > 0 })
				{
					return true;
				}
			}
		}
		else if (AllDockedVessels.Count > 0)
		{
			foreach (SpaceObjectVessel itemB in AllDockedVessels)
			{
				if (itemB.SpawnPoints is { Count: > 0 })
				{
					return true;
				}
			}
		}
		return false;
	}

	public ShipSpawnPoint GetPlayerSpawnPoint(Player pl)
	{
		if (SpawnPoints.Count > 0)
		{
			foreach (ShipSpawnPoint point in SpawnPoints)
			{
				if ((point.Player == null && point.Type == SpawnPointType.SimpleSpawn) || point.Player == pl || point.InvitedPlayerId == pl.PlayerId)
				{
					return point;
				}
			}
		}
		return null;
	}

	public bool HasEmptySimpleSpawnPoint()
	{
		return SpawnPoints != null && SpawnPoints.Find((ShipSpawnPoint m) => m.Type == SpawnPointType.SimpleSpawn && m.Player == null) != null;
	}

	public SpaceObjectVessel(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid, initializeOrbit, position, velocity, forward, up)
	{
	}

	public ObjectTransform GetObjectTransform()
	{
		ObjectTransform objTrans = new ObjectTransform
		{
			GUID = Guid,
			Type = SpaceObjectType.Ship,
			Forward = Forward.ToFloatArray(),
			Up = Up.ToFloatArray()
		};
		if (Orbit.IsOrbitValid)
		{
			objTrans.Orbit = new OrbitData
			{
				ParentGUID = Orbit.Parent.CelestialBody.GUID
			};
			Orbit.FillOrbitData(ref objTrans.Orbit);
		}
		else
		{
			objTrans.Realtime = new RealtimeData
			{
				ParentGUID = Orbit.Parent.CelestialBody.GUID,
				Position = (Position - Orbit.Parent.Position).ToArray(),
				Velocity = Velocity.ToArray()
			};
		}
		if (CurrentCourse != null)
		{
			objTrans.Maneuver = CurrentCourse.CurrentData();
			objTrans.Forward = Forward.ToFloatArray();
			objTrans.Up = Up.ToFloatArray();
		}
		return objTrans;
	}

	public void ResetDockedToVessel()
	{
		foreach (SpaceObjectVessel ves in DockedVessels)
		{
			ves.DockedToVessel = this;
			ves.ResetDockedToVessel();
		}
	}

	public void RecreateDockedVesselsTree()
	{
		DockedToMainVessel = null;
		DockedToVessel = null;
		RecreateDockedVesselsTree(this, null);
	}

	// Warning: Recursive function!
	private void RecreateDockedVesselsTree(SpaceObjectVessel mainVessel, SpaceObjectVessel parentVessel)
	{
		AllDockedVessels.Clear();
		DockedVessels.Clear();
		foreach (VesselDockingPort port in DockingPorts)
		{
			if (port.DockedVessel != null && port.DockedVessel != parentVessel)
			{
				port.DockedVessel.DockedToMainVessel = mainVessel;
				port.DockedVessel.DockedToVessel = this;
				mainVessel.AllDockedVessels.Add(port.DockedVessel);
				DockedVessels.Add(port.DockedVessel);
				port.DockedVessel.RecreateDockedVesselsTree(mainVessel, this);
			}
		}
	}

	public void DbgLogDockedVesseslTree()
	{
		SpaceObjectVessel mainVessel = null;
		if (IsDocked)
		{
			mainVessel = DockedToMainVessel;
		}
		else if (AllDockedVessels.Count > 0)
		{
			mainVessel = this;
		}
		mainVessel?.DbgLogDockedVesslesTreeWorker(1);
	}

	private void DbgLogDockedVesslesTreeWorker(int padding)
	{
		foreach (SpaceObjectVessel ves in DockedVessels)
		{
			ves.DbgLogDockedVesslesTreeWorker(padding + 1);
		}
	}

	public void FitMachineryPart(VesselObjectID slotID, MachineryPart part)
	{
		DistributionManager.GetVesselComponentByPartSlot(slotID)?.FitPartToSlot(slotID, part);
	}

	public void SetPhysicsParameters()
	{
		if (RigidBody != null)
		{
			Quaternion qua = BulletHelper.LookRotation(Forward.ToVector3(), Up.ToVector3());
			BulletSharp.Math.Matrix position = BulletHelper.AffineTransformation(1f, qua, Position.ToVector3());
			RigidBody.MotionState = new DefaultMotionState(position);
			if (!RigidBody.IsActive)
			{
				RigidBody.Activate(forceActivation: true);
			}
			RigidBody.LinearVelocity = Velocity.ToVector3();
			RigidBody.AngularVelocity = AngularVelocity.ToVector3();
		}
	}

	public void ReadPhysicsParameters()
	{
		if (RigidBody == null)
		{
			return;
		}

		PhysicsVelocityDifference = RigidBody.LinearVelocity.ToVector3D() - Velocity;
		PhysicsRotationDifference = QuaternionD.LookRotation(Forward, Up).Inverse() * (RigidBody.AngularVelocity.ToVector3D() - AngularVelocity) * (180.0 / System.Math.PI);
	}

	public void RemoveMachineryPart(VesselObjectID slotID)
	{
		DistributionManager.GetVesselComponentByPartSlot(slotID)?.RemovePartFromSlot(slotID);
	}

	public virtual void SendSpawnMessage(long clientID, bool isDummy)
	{
	}

	public virtual void SetRadius(double radius)
	{
		Radius = radius;
	}

	public List<PairedDoorsDetails> GetPairedDoors(VesselDockingPort port)
	{
		List<PairedDoorsDetails> list = new List<PairedDoorsDetails>();
		int[] doorsIDs = port.DoorsIDs;
		for (int i = 0; i < doorsIDs.Length; i++)
		{
			short doorID = (short)doorsIDs[i];
			Door door = DistributionManager.GetDoor(new VesselObjectID(port.ID.VesselGUID, doorID));
			if (door != null)
			{
				list.Add(new PairedDoorsDetails
				{
					DoorID = door.ID,
					PairedDoorID = door.PairedDoorID
				});
			}
		}
		return list;
	}

	private List<string> ReturnTags(string tag)
	{
		List<string> r = new List<string>();
		if (tag.IsNullOrEmpty())
		{
			r.Add("None");
		}
		else
		{
			string[] s = tag.Split(';');
			string[] array = s;
			foreach (string a in array)
			{
				if (a.IsNullOrEmpty())
				{
					r.Add("None");
				}
				else
				{
					r.Add(a);
				}
			}
		}
		return r;
	}

	private bool CompareTags(List<string> shipTags, List<string> objectTags)
	{
		foreach (string shipTag in shipTags)
		{
			foreach (string objectTag in objectTags)
			{
				if (shipTag == objectTag || objectTag == "*")
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool CheckTag(string tag, SpawnSettingsCase ssCase = SpawnSettingsCase.EnableIf)
	{
		bool match = CompareTags(ReturnTags(VesselData.Tag), ReturnTags(tag));
		return (match && ssCase == SpawnSettingsCase.EnableIf) || (!match && ssCase == SpawnSettingsCase.DisableIf);
	}

	private void CopyAuthorizedPersonelListFromShip(SpaceObjectVessel fromVessel)
	{
		if (HasSecuritySystem && fromVessel != this && !GameScenes.Ranges.IsShip(SceneID) && !GameScenes.Ranges.IsShip(fromVessel.SceneID))
		{
			AuthorizedPersonel.Clear();
			AuthorizedPersonel = new List<AuthorizedPerson>(fromVessel.AuthorizedPersonel);
		}
	}

	public void CopyAuthorizedPersonelListToChildren()
	{
		if (DockedToMainVessel != null)
		{
			foreach (SpaceObjectVessel ves2 in DockedToMainVessel.AllDockedVessels)
			{
				ves2.CopyAuthorizedPersonelListFromShip(this);
			}
			return;
		}
		if (AllDockedVessels.Count <= 0)
		{
			return;
		}
		foreach (SpaceObjectVessel ves in AllDockedVessels)
		{
			ves.CopyAuthorizedPersonelListFromShip(this);
		}
	}

	private void UpdateAuthorizationData(Player pl)
	{
		AuthorizedPerson ap = AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == pl.PlayerId);
		if (ap != null)
		{
			ap.PlayerId = pl.PlayerId;
			ap.Name = pl.Name;
		}
	}

	public bool AddAuthorizedPerson(Player executingPl, Player pl, string name, AuthorizedPersonRank rank)
	{
		if (executingPl == null || pl == null)
		{
			return false;
		}

		UpdateAuthorizationData(executingPl);

		AuthorizedPerson commander = AuthorizedPersonel.Find((AuthorizedPerson m) => m.Rank == AuthorizedPersonRank.CommandingOfficer);
		AuthorizedPerson officer = AuthorizedPersonel.Find((AuthorizedPerson m) => m.Rank == AuthorizedPersonRank.ExecutiveOfficer);

		bool isCommander = commander == null || commander.PlayerId == executingPl.PlayerId;
		bool isOfficer = officer == null || officer.PlayerId == executingPl.PlayerId;

		bool addModifyPlayer = false;
		if (rank == AuthorizedPersonRank.CommandingOfficer && isCommander)
		{
			if (commander != null && commander.PlayerId != pl.PlayerId)
			{
				AddModifyPlayerPosition(commander.PlayerId, commander.Name, AuthorizedPersonRank.Crewman);
			}

			addModifyPlayer = true;
		}
		else if (rank == AuthorizedPersonRank.ExecutiveOfficer && (isOfficer || isCommander))
		{
			if (officer != null && officer.PlayerId != pl.PlayerId)
			{
				AddModifyPlayerPosition(officer.PlayerId, officer.Name, AuthorizedPersonRank.Crewman);
			}

			addModifyPlayer = true;
		}
		else if (rank == AuthorizedPersonRank.Crewman && (isOfficer || isCommander))
		{
			addModifyPlayer = true;
		}

		if (addModifyPlayer)
		{
			AddModifyPlayerPosition(pl.PlayerId, name, rank);
			CopyAuthorizedPersonelListToChildren();

			return true;
		}

		return false;
	}

	private void AddModifyPlayerPosition(string playerId, string name, AuthorizedPersonRank rank)
	{
		AuthorizedPerson existing = AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == playerId);
		if (existing != null)
		{
			existing.Rank = rank;
			existing.PlayerId = playerId;
			existing.Name = name;
		}
		else
		{
			AuthorizedPersonel.Add(new AuthorizedPerson
			{
				Rank = rank,
				PlayerId = playerId,
				Name = name
			});
		}
	}

	public bool RemoveAuthorizedPerson(Player executingPl, Player pl)
	{
		if (executingPl == null || pl == null)
		{
			return false;
		}

		UpdateAuthorizationData(pl);

		AuthorizedPerson existing = AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == pl.PlayerId);
		if (existing == null)
		{
			return false;
		}
		bool isCommander = AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == executingPl.PlayerId && m.Rank == AuthorizedPersonRank.CommandingOfficer) != null;
		bool isOfficer = AuthorizedPersonel.Find((AuthorizedPerson m) => m.PlayerId == executingPl.PlayerId && m.Rank == AuthorizedPersonRank.ExecutiveOfficer) != null;
		bool hasRights = false;
		if (executingPl == pl)
		{
			hasRights = true;
		}
		else if (existing.Rank == AuthorizedPersonRank.ExecutiveOfficer && isCommander)
		{
			hasRights = true;
		}
		else if (existing.Rank == AuthorizedPersonRank.Crewman && (isCommander || isOfficer))
		{
			hasRights = true;
		}
		if (hasRights)
		{
			AuthorizedPersonel.Remove(existing);
			CopyAuthorizedPersonelListToChildren();
			return true;
		}
		return false;
	}

	public bool ChangeVesselName(Player pl, string newName)
	{
		if (pl == null || AuthorizedPersonel.Find((AuthorizedPerson m) => (m.PlayerId == pl.PlayerId || m.PlayerId == pl.PlayerId) && m.Rank == AuthorizedPersonRank.CommandingOfficer) == null)
		{
			return false;
		}
		VesselData.VesselName = newName;
		return true;
	}

	public bool ClearSecuritySystem(Player pl)
	{
		if (pl == null || pl.ItemInHands == null || !ItemTypeRange.IsHackingTool(pl.ItemInHands.Type) || pl.Parent != this)
		{
			return false;
		}
		AuthorizedPersonel.Clear();
		CopyAuthorizedPersonelListToChildren();
		pl.ItemInHands.ChangeStats(new DisposableHackingToolStats
		{
			Use = true
		});
		return true;
	}

	public VesselSecurityData GetVesselSecurityData(bool includeName = false)
	{
		List<VesselSecurityAuthorizedPerson> authPersonel = new List<VesselSecurityAuthorizedPerson>();
		foreach (AuthorizedPerson per in AuthorizedPersonel)
		{
			Player pl = Server.Instance.GetPlayerFromPlayerId(per.PlayerId);
			authPersonel.Add(new VesselSecurityAuthorizedPerson
			{
				PlayerId = per.PlayerId,
				Name = pl != null ? pl.Name : per.Name,
				Rank = per.Rank
			});
		}
		if (includeName)
		{
			return new VesselSecurityData
			{
				VesselName = VesselData.VesselName,
				AuthorizedPersonel = authPersonel
			};
		}
		return new VesselSecurityData
		{
			AuthorizedPersonel = authPersonel
		};
	}

	public async Task SendSecurityResponse(bool includeVesselName, bool sendForAllChildren = true)
	{
		if (HasSecuritySystem)
		{
			await NetworkController.SendToClientsSubscribedTo(new VesselSecurityResponse
			{
				VesselGUID = Guid,
				Data = GetVesselSecurityData(includeVesselName)
			}, -1L, this);
		}
		if (!sendForAllChildren)
		{
			return;
		}
		if (DockedToMainVessel != null)
		{
			foreach (SpaceObjectVessel ves2 in DockedToMainVessel.AllDockedVessels)
			{
				await ves2.SendSecurityResponse(includeVesselName, sendForAllChildren: false);
			}
			return;
		}
		if (AllDockedVessels.Count <= 0)
		{
			return;
		}
		foreach (SpaceObjectVessel ves in AllDockedVessels)
		{
			await ves.SendSecurityResponse(includeVesselName, sendForAllChildren: false);
		}
	}

	public virtual Task UpdateVesselSystems()
	{
		return Task.FromException(new NotImplementedException());
	}

	public override async Task UpdateTimers(double deltaTime)
	{
		await base.UpdateTimers(deltaTime);
		if (IsMainVessel && ((Server.JunkItemsCleanupScope == 1 && IsPrefabStationVessel) || Server.JunkItemsCleanupScope == 2))
		{
			JunkItemsCleanupTimer -= deltaTime;
			if (JunkItemsCleanupTimer <= 0.0)
			{
				JunkItemsCleanupTimer = Server.JunkItemsCleanupInterval;
				await JunkItemsCleanup();
			}
		}
	}

	public async Task SetHealthAsync(float newValue)
	{
		await ChangeHealthBy(newValue - Health, null, VesselRepairPoint.Priority.None, force: true);
	}

	public virtual Task<float> ChangeHealthBy(float value, List<VesselRepairPoint> repairPoints = null, VesselRepairPoint.Priority damagePiority = VesselRepairPoint.Priority.None, bool force = false, VesselDamageType damageType = VesselDamageType.None, double time = 1.0)
	{
		LastVesselDamageType = VesselDamageType.None;
		return Task.FromResult(0f);
	}

	public async Task DamageVesselsInExplosionRadius()
	{
		SelfDestructTimer selfDestructTimer = SelfDestructTimer;
		double radiusMultiplier;
		double damageMultiplier;
		if (selfDestructTimer is { Time: 0f })
		{
			radiusMultiplier = Server.SelfDestructExplosionRadiusMultiplier;
			damageMultiplier = Server.SelfDestructExplosionDamageMultiplier;
			if (DockedToVessel is Ship)
			{
				DockedToVessel.SelfDestructTimer = new SelfDestructTimer(DockedToVessel, MathHelper.RandomRange(1f, 3f));
			}
			foreach (SpaceObjectVessel ves in DockedVessels)
			{
				ves.SelfDestructTimer = new SelfDestructTimer(ves, MathHelper.RandomRange(1f, 3f));
			}
		}
		else
		{
			radiusMultiplier = IsDebrisFragment ? Server.DebrisVesselExplosionRadiusMultiplier : Server.VesselExplosionRadiusMultiplier;
			damageMultiplier = IsDebrisFragment ? Server.DebrisVesselExplosionDamageMultiplier : Server.VesselExplosionDamageMultiplier;
		}
		float radius = (float)((System.Math.Sqrt(Mass / 1000.0) + (Engine is { Status: SystemStatus.OnLine } ? 10 : 0) + (FTL is
		{
			Status: SystemStatus.OnLine
		} ? 20 : 0)) * radiusMultiplier);
		float baseDamage = (float)(MaxHealth * damageMultiplier);
		ArtificialBody[] artificialBodieslsInRange = Server.Instance.SolarSystem.GetArtificialBodieslsInRange(this, radius);
		foreach (ArtificialBody ab in artificialBodieslsInRange)
		{
			if (ab is SpaceObjectVessel ves2)
			{
				float dist2 = (float)(Position - ves2.Position).Magnitude;
				float ratio2 = MathHelper.Clamp((radius - dist2) / radius, 0f, 1f);
				await ves2.ChangeHealthBy((0f - ratio2) * baseDamage, null, VesselRepairPoint.Priority.External, force: false, VesselDamageType.NearbyVesselExplosion);
				Vector3D thrust2 = (ves2.Position - Position).Normalized * 5.0 * ratio2;
				ves2.Rotation += new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()) * 5.0 * ratio2;
				ves2.Orbit.InitFromStateVectors(ves2.Orbit.Parent, ves2.Orbit.Position, ves2.Orbit.Velocity + thrust2, Server.Instance.SolarSystem.CurrentTime, areValuesRelative: false);
				await ves2.DisableStabilization(disableForChildren: true, updateBeforeDisable: false);
			}
			else if (ab is Pivot pivot)
			{
				Vector3D pos = pivot.Position + pivot.Child.LocalRotation * pivot.Child.LocalPosition;
				float dist = (float)(Position - pos).Magnitude;
				float ratio = MathHelper.Clamp((radius - dist) / radius, 0f, 1f);
				if (pivot.Child is Player player)
				{
					await player.Stats.TakeDamage(HurtType.Explosion, ratio * baseDamage);
				}
				Vector3D thrust = (pos - Position).Normalized * 10.0 * ratio;
				pivot.Orbit.InitFromStateVectors(pivot.Orbit.Parent, pivot.Orbit.Position, pivot.Orbit.Velocity + thrust, Server.Instance.SolarSystem.CurrentTime, areValuesRelative: false);
			}
		}
	}

	public double GetCompoundMass()
	{
		return MainVessel.Mass + MainVessel.AllDockedVessels.Sum((SpaceObjectVessel m) => m.Mass);
	}

	public List<VesselRepairPointDetails> GetVesselRepairPointsDetails(bool changedOnly)
	{
		List<VesselRepairPointDetails> list = new List<VesselRepairPointDetails>();
		foreach (VesselRepairPoint rp in RepairPoints)
		{
			if (!changedOnly || rp.StatusChanged)
			{
				list.Add(rp.GetDetails());
				rp.StatusChanged = false;
			}
		}
		return list;
	}

	internal void UndockAll()
	{
		List<ShipStatsMessage> ssmList = new List<ShipStatsMessage>();
		foreach (SpaceObjectVessel v in DockedVessels.Where((SpaceObjectVessel m) => m is Ship))
		{
			VesselDockingPort dp2 = v.DockingPorts.First(m => m.DockingStatus && m.DockedVessel == this);
			if (dp2 != null)
			{
				SceneDockingPortDetails dpd2 = dp2.GetDetails();
				dpd2.DockingStatus = false;
				ssmList.Add(new ShipStatsMessage
				{
					GUID = v.Guid,
					VesselObjects = new VesselObjects
					{
						DockingPorts = new List<SceneDockingPortDetails> { dpd2 }
					}
				});
			}
		}
		if (DockedToVessel is Ship)
		{
			VesselDockingPort dp = DockingPorts.First(m => m.DockingStatus && m.DockedVessel == DockedToVessel);
			if (dp != null)
			{
				SceneDockingPortDetails dpd = dp.GetDetails();
				dpd.DockingStatus = false;
				ssmList.Add(new ShipStatsMessage
				{
					GUID = Guid,
					VesselObjects = new VesselObjects
					{
						DockingPorts = new List<SceneDockingPortDetails> { dpd }
					}
				});
			}
		}
		foreach (ShipStatsMessage ssm in ssmList)
		{
			if (Server.Instance.GetVessel(ssm.GUID) is Ship s)
			{
				s.ShipStatsMessageListener(ssm);
			}
		}
	}

	protected SubSystem createSubSystem(SubSystemData ssData)
	{
		VesselObjectID id = new VesselObjectID(Guid, ssData.InSceneID);
		if (ssData.Type == SubSystemType.Light)
		{
			return new SubSystemLights(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.EmergencyLight)
		{
			return new SubSystemEmergencyLights(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.RCS)
		{
			return new SubSystemRCS(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.Engine)
		{
			return new SubSystemEngine(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.FTL)
		{
			return new SubSystemFTL(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.Refinery)
		{
			return new SubSystemRefinery(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.VesselBasePowerConsumer)
		{
			return new VesselBaseSystem(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.Fabricator)
		{
			return new SubSystemFabricator(this, id, ssData);
		}
		if (ssData.Type == SubSystemType.Radar)
		{
			return new SubSystemRadar(this, id, ssData);
		}
		return new SubSystemGeneric(this, id, ssData);
	}

	protected async Task<Generator> CreateGeneratorAsync(GeneratorData genData)
	{
		VesselObjectID id = new VesselObjectID(Guid, genData.InSceneID);
		if (genData.Type == GeneratorType.Power)
		{
			return new GeneratorPower(this, id, genData);
		}
		if (genData.Type == GeneratorType.Air)
		{
			return new GeneratorAir(this, id, genData);
		}
		if (genData.Type == GeneratorType.AirScrubber)
		{
			return new GeneratorScrubber(this, id, genData);
		}
		if (genData.Type == GeneratorType.Capacitor)
		{
			return await GeneratorCapacitor.CreateAsync(this, id, genData);
		}
		if (genData.Type == GeneratorType.Solar)
		{
			return new GeneratorSolar(this, id, genData);
		}
		throw new Exception("Unsupported generator type " + genData.Type);
	}

	public void ResetDecayGraceTimer()
	{
		SpaceObjectVessel mainVessel = DockedToMainVessel != null ? DockedToMainVessel : this;
		mainVessel.DecayGraceTimer = Server.VesselDecayGracePeriod;
		foreach (SpaceObjectVessel v in mainVessel.AllDockedVessels)
		{
			v.DecayGraceTimer = Server.VesselDecayGracePeriod;
		}
	}

	public async Task JunkItemsCleanup()
	{
		if (MainVessel.VesselCrew.Count != 0 || MainVessel.AllDockedVessels.FirstOrDefault((SpaceObjectVessel m) => m.VesselCrew.Count > 0) != null)
		{
			return;
		}
		await RemoveAllLooseDynamicObjects(MainVessel);
		foreach (SpaceObjectVessel ves in MainVessel.AllDockedVessels)
		{
			await RemoveAllLooseDynamicObjects(ves);
		}
	}

	public static async Task RemoveAllLooseDynamicObjects(SpaceObjectVessel vessel)
	{
		List<DynamicObject> forRemoval = new List<DynamicObject>();
		foreach (DynamicObject dobj2 in vessel.DynamicObjects.Values)
		{
			if (dobj2.Item is { Slot: null, AttachPointID: null, AttachmentChangeTime: > 0.0 } && Server.SolarSystemTime - dobj2.Item.AttachmentChangeTime > Server.JunkItemsTimeToLive)
			{
				forRemoval.Add(dobj2);
			}
		}
		foreach (DynamicObject rem in forRemoval)
		{
			await rem.Destroy();
			vessel.DynamicObjects.TryRemove(rem.Guid, out var _);
		}
	}

	public void SetMainVessel()
	{
		Dictionary<VesselObjectID, SceneDockingPortDetails> details = [];
		foreach (VesselDockingPort dp in DockingPorts.Where((VesselDockingPort m) => m.DockedToID != null))
		{
			VesselDockingPort odp = dp.DockedVessel.DockingPorts.First((VesselDockingPort m) => m.ID.Equals(dp.DockedToID));
			details[odp.ID] = odp.GetDetails();
		}
		UndockAll();
		List<ShipStatsMessage> ssmList = [];
		foreach (KeyValuePair<VesselObjectID, SceneDockingPortDetails> kv in details)
		{
			SpaceObjectVessel ves = Server.Instance.GetVessel(kv.Key.VesselGUID);
			ssmList.Add(new ShipStatsMessage
			{
				GUID = ves.Guid,
				VesselObjects = new VesselObjects
				{
					DockingPorts = new List<SceneDockingPortDetails> { kv.Value }
				}
			});
		}
		foreach (ShipStatsMessage ssm in ssmList)
		{
			if (Server.Instance.GetVessel(ssm.GUID) is Ship s)
			{
				s.ShipStatsMessageListener(ssm);
			}
		}
	}

	protected override bool CheckPlanetDeath()
	{
		if (IsWarping && Server.CanWarpThroughCelestialBodies)
		{
			return false;
		}
		return base.CheckPlanetDeath();
	}

	public async Task<bool> DockToVessel(VesselDockingPort port, VesselDockingPort dockToPort, SpaceObjectVessel dockToVessel, bool disableStabilization = true, bool useCurrentSolarSystemTime = true, bool buildingStation = false)
	{
		if (port.ParentVessel.MainVessel == dockToVessel.MainVessel)
		{
			Debug.LogError("Circular docking");
			return false;
		}
		if (dockToPort == null || port == null || port.DockingStatus || dockToPort.DockingStatus)
		{
			Debug.LogError("DockToShip returned at start, check if some port IDs changed", Environment.StackTrace);
			return false;
		}
		if (disableStabilization)
		{
			if (DockedToMainVessel != null && (DockedToMainVessel.StabilizeToTargetObj != null || DockedToMainVessel.StabilizedToTargetChildren.Count > 0))
			{
				await DockedToMainVessel.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			}
			else if (DockedToMainVessel == null && (StabilizeToTargetObj != null || StabilizedToTargetChildren.Count > 0))
			{
				await DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			}
			if (dockToVessel.DockedToMainVessel != null && (dockToVessel.DockedToMainVessel.StabilizeToTargetObj != null || dockToVessel.DockedToMainVessel.StabilizedToTargetChildren.Count > 0))
			{
				await dockToVessel.DockedToMainVessel.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			}
			else if (dockToVessel.DockedToMainVessel == null && (dockToVessel.StabilizeToTargetObj != null || dockToVessel.StabilizedToTargetChildren.Count > 0))
			{
				await dockToVessel.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			}
		}

		if (dockToVessel.DockedToMainVessel == this)
		{
			Debug.LogErrorFormat("Vessel {0} is docked to itself.", FullName);
			return false;
		}

		if (dockToVessel.DockedToVessel == this)
		{
			Debug.LogErrorFormat("Vessel {0} is docked to itself.", FullName);
			return false;
		}

		port.DockedToID = dockToPort.ID;
		port.DockedVessel = dockToVessel;
		port.DockingStatus = true;
		dockToPort.DockedToID = port.ID;
		dockToPort.DockedVessel = this;
		dockToPort.DockingStatus = true;
		DockUndockPlayerData dupd = DockUndockPlayerData.GetPlayerData(this, dockToVessel);
		SpaceObjectVessel rBodyRemoveOld = dockToVessel.IsDocked ? dockToVessel.DockedToMainVessel : dockToVessel;
		SpaceObjectVessel rBodyRemoveNew = IsDocked ? DockedToMainVessel : this;
		SpaceObjectVessel newDockedToMainVessel = dockToVessel.IsDocked ? dockToVessel.DockedToMainVessel : dockToVessel;
		SpaceObjectVessel vesselWithSecuritySystem = newDockedToMainVessel.AllDockedVessels.Find((SpaceObjectVessel m) => m.HasSecuritySystem);
		newDockedToMainVessel.RecreateDockedVesselsTree();
		newDockedToMainVessel.DbgLogDockedVesseslTree();
		Orbit.SetLastChangeTime(Server.SolarSystemTime);
		SpaceObjectVessel dockedToMainVessel = DockedToMainVessel;
		DockedToMainVessel.CompoundDistributionManager = new DistributionManager(DockedToMainVessel, linkDockedVessels: true);
		DockedToMainVessel.ConnectionsChanged = true;
		dockedToMainVessel.ResetRelativePositionAndRotations();
		dockedToMainVessel.RecalculateRelativeTransforms(null);
		Server.Instance.PhysicsController.RemoveRigidBody(rBodyRemoveOld);
		Server.Instance.PhysicsController.RemoveRigidBody(rBodyRemoveNew);
		Vector3D oldCenterOffset = DockedToMainVessel.VesselData.CollidersCenterOffset.ToVector3D();
		dockedToMainVessel.RecalculateCenter();
		DockedToMainVessel.Orbit.RelativePosition -= QuaternionD.LookRotation(DockedToMainVessel.Forward, DockedToMainVessel.Up) * (oldCenterOffset - DockedToMainVessel.VesselData.CollidersCenterOffset.ToVector3D());
		DockedToMainVessel.Orbit.InitFromCurrentStateVectors(useCurrentSolarSystemTime ? Server.SolarSystemTime : 0.0);
		Server.Instance.PhysicsController.CreateAndAddRigidBody(DockedToMainVessel);
		DockedToMainVessel.SetPhysicsParameters();
		SceneTriggerExecutor closestExecutor = null;
		if (port.MergeExecutors != null && dockToPort.MergeExecutors != null)
		{
			foreach (KeyValuePair<SceneTriggerExecutor, Vector3D> exec in port.MergeExecutors)
			{
				if (exec.Key.IsMerged)
				{
					continue;
				}
				closestExecutor = null;
				double closestExecutorDistance = -1.0;
				foreach (KeyValuePair<SceneTriggerExecutor, Vector3D> execOther in dockToPort.MergeExecutors)
				{
					if (!execOther.Key.IsMerged)
					{
						double currDistance = (exec.Value - QuaternionD.AngleAxis(180.0, Vector3D.Up) * execOther.Value).Magnitude;
						if ((currDistance < closestExecutorDistance || closestExecutorDistance == -1.0) && exec.Key.AreStatesEqual(execOther.Key))
						{
							closestExecutorDistance = currDistance;
							closestExecutor = execOther.Key;
						}
					}
				}
				if (closestExecutor != null && closestExecutorDistance <= port.MergeExecutorsDistance)
				{
					closestExecutor.MergeWith(exec.Key);
				}
			}
		}
		vesselWithSecuritySystem?.CopyAuthorizedPersonelListToChildren();
		dupd.ModifyPlayersPositionAndRotation();
		await CheckMainPropulsionVessel();
		if (RCS != null)
		{
			await RCS.GoOffLine(autoRestart: false);
			if (this is Ship)
			{
				(this as Ship).ResetRotationAndThrust();
			}
		}
		if (dockToVessel.RCS != null)
		{
			await dockToVessel.RCS.GoOffLine(autoRestart: false);
			if (dockToVessel is Ship ship)
			{
				ship.ResetRotationAndThrust();
			}
		}
		if (!buildingStation)
		{
			RemoveFromSpawnSystem();
			dockToVessel.MainVessel.UpdateVesselData();
		}
		return true;
	}

	public void RemoveFromSpawnSystem()
	{
		SpawnManager.RemoveSpawnSystemObject(MainVessel, checkChildren: true);
		foreach (SpaceObjectVessel ves in MainVessel.AllDockedVessels)
		{
			SpawnManager.RemoveSpawnSystemObject(ves, checkChildren: true);
		}
	}

	public async Task<bool> UndockFromVessel(SpaceObjectVessel dockedToVessel, SceneDockingPortDetails details)
	{
		if (DockedToVessel == dockedToVessel)
		{
			VesselDockingPort port = DockingPorts.First((VesselDockingPort m) => m.DockedVessel == DockedToVessel);
			if (port != null)
			{
				VesselDockingPort dockedToPort = DockedToVessel.DockingPorts.First((VesselDockingPort m) => m.ID == port.DockedToID);
				if (dockedToPort != null)
				{
					return await UndockFromVessel(port, dockedToVessel, dockedToPort, details);
				}
			}
		}
		return false;
	}

	public async Task<bool> UndockFromVessel(VesselDockingPort port, SpaceObjectVessel dockedToVessel, VesselDockingPort dockedToPort, SceneDockingPortDetails details)
	{
		if (!port.DockingStatus || port.DockedToID == null || port.DockedVessel is not Ship)
		{
			return false;
		}
		if (dockedToVessel == null)
		{
			dockedToVessel = port.DockedVessel;
		}
		if (dockedToPort == null)
		{
			dockedToPort = dockedToVessel.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == port.DockedToID.InSceneID);
		}
		if (dockedToPort == null)
		{
			return false;
		}
		SpaceObjectVessel resultVesselMine = null;
		SpaceObjectVessel resultVesselOther = null;
		details.RelativePositionUpdate = [];
		details.RelativeRotationUpdate = [];
		SpaceObjectVessel oldMainVessel = DockedToMainVessel != null ? DockedToMainVessel : dockedToVessel.DockedToMainVessel;
		QuaternionD oldMainVesselRot = QuaternionD.LookRotation(oldMainVessel.Forward, oldMainVessel.Up);
		Vector3D oldCenterOffset = oldMainVessel.VesselData.CollidersCenterOffset.ToVector3D();
		Vector3D oldMainVessleRelPos = oldMainVessel.Orbit.RelativePosition;
		if (oldMainVessel.StabilizeToTargetObj != null)
		{
			await oldMainVessel.DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
		}
		port.DockedToID = null;
		port.DockedVessel = null;
		port.DockingStatus = false;
		dockedToPort.DockedToID = null;
		dockedToPort.DockedVessel = null;
		dockedToPort.DockingStatus = false;
		DockedVessels.Remove(dockedToVessel);
		dockedToVessel.DockedVessels.Remove(this);
		DockedToVessel = null;
		dockedToVessel.DockedToVessel = null;
		oldMainVessel.ResetDockedToVessel();
		DockUndockPlayerData dupd = DockUndockPlayerData.GetPlayerData(oldMainVessel);
		resultVesselMine = oldMainVessel;
		resultVesselOther = oldMainVessel.AllDockedVessels.FirstOrDefault((SpaceObjectVessel m) => m.DockedToVessel == null);
		if (resultVesselMine == null && DockedToMainVessel != null)
		{
			resultVesselMine = DockedToMainVessel;
		}
		if (resultVesselOther == null && dockedToVessel.DockedToMainVessel != null)
		{
			resultVesselOther = dockedToVessel.DockedToMainVessel;
		}
		Vector3D resShipMineMPRelPos = resultVesselMine.RelativePositionFromMainParent;
		Vector3D resShipOtherMPRelPos = resultVesselOther.RelativePositionFromMainParent;
		resultVesselMine.Orbit.CopyDataFrom(oldMainVessel.Orbit, Server.SolarSystemTime, exactCopy: true);
		resultVesselOther.Orbit.CopyDataFrom(oldMainVessel.Orbit, Server.SolarSystemTime, exactCopy: true);
		if (resultVesselMine != oldMainVessel)
		{
			resultVesselMine.Forward = oldMainVesselRot * resultVesselMine.RelativeRotationFromMainParent * Vector3D.Forward;
			resultVesselMine.Up = oldMainVesselRot * resultVesselMine.RelativeRotationFromMainParent * Vector3D.Up;
		}
		if (resultVesselOther != oldMainVessel)
		{
			resultVesselOther.Forward = oldMainVesselRot * resultVesselOther.RelativeRotationFromMainParent * Vector3D.Forward;
			resultVesselOther.Up = oldMainVesselRot * resultVesselOther.RelativeRotationFromMainParent * Vector3D.Up;
		}
		resultVesselMine.RecreateDockedVesselsTree();
		resultVesselOther.RecreateDockedVesselsTree();
		Server.Instance.PhysicsController.RemoveRigidBody(oldMainVessel);
		resultVesselMine.ResetRelativePositionAndRotations();
		resultVesselOther.ResetRelativePositionAndRotations();
		resultVesselMine.RecalculateRelativeTransforms(null);
		resultVesselOther.RecalculateRelativeTransforms(null);
		resultVesselMine.RecalculateCenter();
		resultVesselOther.RecalculateCenter();
		details.RelativePositionUpdate.Add(resultVesselMine.Guid, resShipMineMPRelPos.ToFloatArray());
		details.RelativeRotationUpdate.Add(resultVesselMine.Guid, QuaternionD.LookRotation(resultVesselMine.Forward, resultVesselMine.Up).ToFloatArray());
		details.RelativePositionUpdate.Add(resultVesselOther.Guid, resShipOtherMPRelPos.ToFloatArray());
		details.RelativeRotationUpdate.Add(resultVesselOther.Guid, QuaternionD.LookRotation(resultVesselOther.Forward, resultVesselOther.Up).ToFloatArray());
		resultVesselMine.Orbit.RelativePosition += oldMainVesselRot * (-oldCenterOffset + resShipMineMPRelPos + oldMainVesselRot.Inverse() * QuaternionD.LookRotation(resultVesselMine.Forward, resultVesselMine.Up) * resultVesselMine.VesselData.CollidersCenterOffset.ToVector3D());
		resultVesselOther.Orbit.RelativePosition += oldMainVesselRot * (-oldCenterOffset + resShipOtherMPRelPos + oldMainVesselRot.Inverse() * QuaternionD.LookRotation(resultVesselOther.Forward, resultVesselOther.Up) * resultVesselOther.VesselData.CollidersCenterOffset.ToVector3D());
		resultVesselMine.Orbit.RelativeVelocity += (resultVesselMine.Orbit.RelativePosition - resultVesselOther.Orbit.RelativePosition).Normalized * 0.15;
		resultVesselOther.Orbit.RelativeVelocity += (resultVesselOther.Orbit.RelativePosition - resultVesselMine.Orbit.RelativePosition).Normalized * 0.15;
		resultVesselMine.Orbit.InitFromCurrentStateVectors(Server.SolarSystemTime);
		resultVesselOther.Orbit.InitFromCurrentStateVectors(Server.SolarSystemTime);
		Server.Instance.PhysicsController.CreateAndAddRigidBody(resultVesselMine);
		resultVesselMine.SetPhysicsParameters();
		Server.Instance.PhysicsController.CreateAndAddRigidBody(resultVesselOther);
		resultVesselOther.SetPhysicsParameters();
		oldMainVessel.CompoundDistributionManager.UnpairAllDoors();
		oldMainVessel.CompoundDistributionManager = null;
		if (resultVesselMine.AllDockedVessels.Count > 0)
		{
			resultVesselMine.CompoundDistributionManager = new DistributionManager(resultVesselMine, linkDockedVessels: true);
		}
		else
		{
			resultVesselMine.CompoundDistributionManager = null;
		}
		resultVesselMine.ConnectionsChanged = true;
		if (resultVesselOther.AllDockedVessels.Count > 0)
		{
			resultVesselOther.CompoundDistributionManager = new DistributionManager(resultVesselOther, linkDockedVessels: true);
		}
		else
		{
			resultVesselOther.CompoundDistributionManager = null;
		}
		resultVesselOther.ConnectionsChanged = true;
		if (port.MergeExecutors != null)
		{
			foreach (KeyValuePair<SceneTriggerExecutor, Vector3D> exec in port.MergeExecutors)
			{
				if (exec.Key.IsMerged)
				{
					exec.Key.MergeWith(null);
				}
			}
		}
		resultVesselMine.DbgLogDockedVesseslTree();
		resultVesselOther.DbgLogDockedVesseslTree();
		details.CollidersCenterOffset = resultVesselMine.VesselData.CollidersCenterOffset;
		details.CollidersCenterOffsetOther = resultVesselOther.VesselData.CollidersCenterOffset;
		foreach (SpaceObjectVessel s2 in resultVesselMine.AllDockedVessels)
		{
			details.RelativePositionUpdate.Add(s2.Guid, s2.RelativePositionFromParent.ToFloatArray());
			details.RelativeRotationUpdate.Add(s2.Guid, s2.RelativeRotationFromParent.ToFloatArray());
		}
		foreach (SpaceObjectVessel s in resultVesselOther.AllDockedVessels)
		{
			details.RelativePositionUpdate.Add(s.Guid, s.RelativePositionFromParent.ToFloatArray());
			details.RelativeRotationUpdate.Add(s.Guid, s.RelativeRotationFromParent.ToFloatArray());
		}
		details.VesselOrbit = new OrbitData();
		resultVesselMine.Orbit.FillOrbitData(ref details.VesselOrbit, resultVesselMine);
		details.VesselOrbitOther = new OrbitData();
		resultVesselOther.Orbit.FillOrbitData(ref details.VesselOrbitOther, resultVesselOther);
		dupd.ModifyPlayersPositionAndRotation();
		if (resultVesselMine.IsPartOfSpawnSystem)
		{
			if (resultVesselMine.AllDockedVessels.Count == 0)
			{
				resultVesselMine.UpdateVesselData(null, Server.NameGenerator.GenerateObjectRegistration(resultVesselMine.ObjectType, resultVesselMine.Orbit.Parent.CelestialBody, resultVesselMine.VesselData.SceneID));
			}
			else if (resultVesselOther.AllDockedVessels.Count > 0 && !resultVesselMine.VesselData.VesselRegistration.ToLower().EndsWith("segment"))
			{
				resultVesselMine.UpdateVesselData(null, resultVesselMine.VesselData.VesselRegistration += " segment");
			}
		}
		if (resultVesselOther.IsPartOfSpawnSystem)
		{
			if (resultVesselOther.AllDockedVessels.Count == 0)
			{
				resultVesselOther.UpdateVesselData(null, Server.NameGenerator.GenerateObjectRegistration(resultVesselOther.ObjectType, resultVesselOther.Orbit.Parent.CelestialBody, resultVesselOther.VesselData.SceneID));
			}
			else if (resultVesselMine.AllDockedVessels.Count > 0 && !resultVesselOther.VesselData.VesselRegistration.ToLower().EndsWith("segment"))
			{
				resultVesselOther.UpdateVesselData(null, resultVesselOther.VesselData.VesselRegistration += " segment");
			}
		}
		resultVesselMine.UpdateVesselData();
		return true;
	}

	public async Task CheckMainPropulsionVessel()
	{
		if (!IsMainVessel && MainVessel.FTL is { Status: SystemStatus.OnLine })
		{
			await MainVessel.FTL.GoOffLine(autoRestart: false);
		}
		foreach (SpaceObjectVessel vessel in MainVessel.AllDockedVessels.Where((SpaceObjectVessel m) => m != this))
		{
			if (vessel.FTL is { Status: SystemStatus.OnLine })
			{
				await vessel.FTL.GoOffLine(autoRestart: false);
			}
		}
		if (!Server.Instance.WorldInitialized && FTL is { Status: SystemStatus.OnLine })
		{
			await FTL.GoOffLine(autoRestart: false);
		}
	}

	public void ResetRelativePositionAndRotations()
	{
		RelativePositionFromParent = Vector3D.Zero;
		RelativeRotationFromParent = QuaternionD.Identity;
		RelativePositionFromMainParent = Vector3D.Zero;
		RelativeRotationFromMainParent = QuaternionD.Identity;
	}

	public void RecalculateRelativeTransforms(SpaceObjectVessel parentVessel)
	{
		foreach (VesselDockingPort port in DockingPorts)
		{
			if (port.DockingStatus && port.DockedVessel != parentVessel)
			{
				VesselDockingPort childPort = port.DockedVessel.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == port.DockedToID.InSceneID);
				CalculateRelativeTransform(port, childPort, out var relativPos, out var relativRot);
				port.DockedVessel.RelativePositionFromParent = relativPos;
				port.DockedVessel.RelativeRotationFromParent = relativRot;
				if (IsDocked)
				{
					port.DockedVessel.RelativePositionFromMainParent = RelativePositionFromMainParent + RelativeRotationFromMainParent * relativPos;
					port.DockedVessel.RelativeRotationFromMainParent = RelativeRotationFromMainParent * relativRot;
				}
				else
				{
					port.DockedVessel.RelativePositionFromMainParent = relativPos;
					port.DockedVessel.RelativeRotationFromMainParent = relativRot;
				}
				(port.DockedVessel as Ship).RecalculateRelativeTransforms(this);
			}
		}
	}

	public void RecalculateCenter()
	{
		if (IsDocked)
		{
			return;
		}

		Vector3 maxValue;
		Vector3 minValue;
		BulletPhysicsController.ComplexBoundCalculation(this, out minValue, out maxValue);
		Vector3D CollidersCenterOffset = (minValue + maxValue).ToVector3D() / 2.0;
		Vector3D centerOffsetDiff = CollidersCenterOffset - (VesselData.CollidersCenterOffset != null ? VesselData.CollidersCenterOffset.ToVector3D() : Vector3D.Zero);
		VesselData.CollidersCenterOffset = CollidersCenterOffset.ToFloatArray();
		if (AllDockedVessels.Count <= 0)
		{
			return;
		}
		foreach (SpaceObjectVessel vess in AllDockedVessels)
		{
			vess.VesselData.CollidersCenterOffset = Vector3D.Zero.ToFloatArray();
		}
	}

	public static void CalculateRelativeTransform(VesselDockingPort parentPort, VesselDockingPort childPort, out Vector3D relativePosition, out QuaternionD relativeRotation)
	{
		relativeRotation = parentPort.Rotation * QuaternionD.AngleAxis(180.0, Vector3D.Up) * QuaternionD.Inverse(childPort.Rotation);
		relativePosition = parentPort.Position - relativeRotation * childPort.Position;
	}

	public static async Task<SpaceObjectVessel> CreateNew(GameScenes.SceneId sceneID, string registration = "", long GUID = -1L, List<long> nearArtificialBodyGUIDs = null, List<long> celestialBodyGUIDs = null, Vector3D? positionOffset = null, Vector3D? velocityAtPosition = null, QuaternionD? localRotation = null, string vesselTag = "", bool checkPosition = true, float? AsteroidResourcesMultiplier = null, double distanceFromSurfacePercMin = 0.03, double distanceFromSurfacePercMax = 0.3, SpawnRuleOrbit spawnRuleOrbit = null, double celestialBodyDeathDistanceMultiplier = 1.5, double artificialBodyDistanceCheck = 100.0)
	{
		if (GameScenes.Ranges.IsAsteroid(sceneID))
		{
			float? asteroidResourcesMultiplier = AsteroidResourcesMultiplier;
			return Asteroid.CreateNewAsteroid(sceneID, registration, GUID, nearArtificialBodyGUIDs, celestialBodyGUIDs, positionOffset, velocityAtPosition, localRotation, vesselTag, checkPosition, asteroidResourcesMultiplier, distanceFromSurfacePercMin, distanceFromSurfacePercMax, spawnRuleOrbit, celestialBodyDeathDistanceMultiplier, artificialBodyDistanceCheck);
		}
		return await Ship.CreateNewShip(sceneID, registration, GUID, nearArtificialBodyGUIDs, celestialBodyGUIDs, positionOffset, velocityAtPosition, localRotation, vesselTag, checkPosition, distanceFromSurfacePercMin, distanceFromSurfacePercMax, spawnRuleOrbit, celestialBodyDeathDistanceMultiplier, artificialBodyDistanceCheck);
	}

	public double GetDistanceFromPlayer(Player pl)
	{
		if (pl.Parent is Pivot)
		{
			return (pl.Parent.Position + pl.LocalPosition - Position).Magnitude;
		}
		return (pl.Parent.Position - Position).Magnitude;
	}

	public Player GetNearestPlayer()
	{
		double dist;
		return GetNearestPlayer(out dist);
	}

	public Player GetNearestPlayer(out double distance)
	{
		Player ret = null;
		double dist = double.MaxValue;
		distance = 0.0;
		foreach (Player pl in from m in Server.Instance.AllPlayers.Union(from m in NetworkController.GetAllConnectedPlayers() select m)
			where m.IsAlive
			select m)
		{
			double d = GetDistanceFromPlayer(pl);
			if (d < dist)
			{
				dist = d;
				distance = d;
				ret = pl;
			}
		}
		return ret;
	}

	public VesselObjects GetVesselObjects()
	{
		VesselObjects ss = new VesselObjects();
		ss.MiscStatuses = new VesselMiscStatuses();
		if (CurrentCourse != null && CurrentCourse.IsInProgress)
		{
			ss.MiscStatuses.CourseInProgress = ObjectCopier.DeepCopy(CurrentCourse.CurrentCourseItem);
		}
		else
		{
			ss.MiscStatuses.CourseInProgress = null;
		}
		ss.MiscStatuses.IsMatchedToTarget = StabilizeToTargetObj != null;
		ss.SecurityData = GetVesselSecurityData();
		ss.SubSystems = DistributionManager.GetSubSystemsDetails(changedOnly: false, -1L);
		ss.Generators = DistributionManager.GetGeneratorsDetails(changedOnly: false, -1L);
		ss.RoomTriggers = DistributionManager.GetRoomsDetails(changedOnly: false, -1L);
		ss.ResourceContainers = DistributionManager.GetResourceContainersDetails(changedOnly: false, -1L);
		ss.RepairPoints = GetVesselRepairPointsDetails(changedOnly: false);
		ss.NameTags = NameTags;
		ss.Doors = DistributionManager.GetDoorsDetails(changedOnly: false, -1L);
		ss.SceneTriggerExecutors = new List<SceneTriggerExecutorDetails>();
		if (SceneTriggerExecutors is { Count: > 0 })
		{
			foreach (SceneTriggerExecutor sc in SceneTriggerExecutors)
			{
				ss.SceneTriggerExecutors.Add(new SceneTriggerExecutorDetails
				{
					InSceneID = sc.InSceneID,
					CurrentStateID = sc.StateID,
					NewStateID = sc.StateID,
					PlayerThatActivated = sc.PlayerThatActivated
				});
			}
		}
		ss.DockingPorts = [];
		if (DockingPorts != null && DockingPorts.Length > 0)
		{
			foreach (VesselDockingPort port in DockingPorts)
			{
				if (!port.DockingStatus || (port.DockingStatus && port.DockedVessel == DockedToVessel && IsDocked))
				{
					ss.DockingPorts.Add(new SceneDockingPortDetails
					{
						ID = port.ID,
						DockedToID = port.DockedToID,
						Locked = port.Locked,
						DockingStatus = port.DockingStatus,
						RelativePosition = RelativePositionFromParent.ToFloatArray(),
						RelativeRotation = RelativeRotationFromParent.ToFloatArray(),
						CollidersCenterOffset = IsDocked ? DockedToMainVessel.VesselData.CollidersCenterOffset : VesselData.CollidersCenterOffset,
						ExecutorsMerge = port.GetMergedExecutors(null),
						PairedDoors = GetPairedDoors(port)
					});
				}
			}
		}
		if (CargoBay != null)
		{
			ss.CargoBay = CargoBay.GetDetails();
		}
		ss.SpawnWithChance = new List<SpawnObjectsWithChanceDetails>();
		foreach (SpawnObjectsWithChance d in SpawnChance)
		{
			ss.SpawnWithChance.Add(new SpawnObjectsWithChanceDetails
			{
				InSceneID = d.InSceneID,
				Chance = d.Chance
			});
		}
		ss.SpawnPoints = new List<SpawnPointStats>();
		foreach (ShipSpawnPoint sp in SpawnPoints)
		{
			ss.SpawnPoints.Add(new SpawnPointStats
			{
				InSceneID = sp.SpawnPointID,
				NewState = sp.State,
				NewType = sp.Type,
				PlayerGUID = sp.Player != null ? sp.Player.FakeGuid : -1,
				PlayerName = sp.Player != null ? sp.Player.Name : null,
				PlayerId = sp.Player != null ? sp.Player.PlayerId : null
			});
		}
		ss.EmblemId = EmblemId;
		return ss;
	}

	public void UpdateVesselData(string vesselName = null, string vesselRegistration = null)
	{
		VesselDataUpdate vdu = new VesselDataUpdate
		{
			GUID = Guid
		};
		bool update = false;
		if (vesselName != null)
		{
			VesselData.VesselName = vesselName;
			vdu.VesselName = vesselName;
			update = true;
		}
		if (vesselRegistration != null)
		{
			VesselData.VesselRegistration = vesselRegistration;
			vdu.VesselRegistration = vesselRegistration;
			update = true;
		}
		float newRadarSignature = GetCompoundRadarSignature();
		if (VesselData.RadarSignature != newRadarSignature)
		{
			VesselData.RadarSignature = newRadarSignature;
			vdu.RadarSignature = newRadarSignature;
			update = true;
		}
		bool newAlwaysVisible = AllVessels.FirstOrDefault((SpaceObjectVessel m) => m.IsAlwaysVisible) != null;
		if (VesselData.IsAlwaysVisible != newAlwaysVisible)
		{
			VesselData.IsAlwaysVisible = newAlwaysVisible;
			vdu.IsAlwaysVisible = newAlwaysVisible;
			update = true;
		}
		bool newDistress = AllVessels.FirstOrDefault((SpaceObjectVessel m) => m.IsDistressSignalActive) != null;
		if (VesselData.IsDistressSignalActive != newDistress)
		{
			VesselData.IsDistressSignalActive = newDistress;
			vdu.IsDistressSignalActive = newDistress;
			update = true;
		}
		if (update)
		{
			Server.Instance.VesselsDataUpdate[Guid] = vdu;
		}
	}
}
