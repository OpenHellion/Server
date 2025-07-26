using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenHellion.Net;
using ZeroGravity.BulletPhysics;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;
using System.Collections.Immutable;
using System.Diagnostics;

namespace ZeroGravity.Objects;

public class Ship : SpaceObjectVessel, IPersistantObject
{
	private bool isRcsOnline;

	public Vector3D RcsThrustVelocityDifference = Vector3D.Zero;

	public Vector3D RcsThrustDirection = Vector3D.Zero;

	public Vector3D ExtraRcsThrustVelocityDifference = Vector3D.Zero;

	private double rcsThrustResetTimer;

	private double rcsThrustResetTreshold = 0.2;

	public Vector3D EngineThrustVelocityDifference = Vector3D.Zero;

	public Vector3D ExtraEngineThrust = Vector3D.Zero;

	private double engineThrustPercentage;

	private double currentEngineThrustPerc;

	private bool isRotationOnline;

	public Vector3D RotationThrustVelocityDifference = Vector3D.Zero;

	public Vector3D RotationThrustDirection = Vector3D.Zero;

	public Vector3D ExtraRotationThrustVelocityDifference = Vector3D.Zero;

	private double rotationThrustResetTimer;

	private double rotationThrustResetTreshold = 0.5;

	private double autoStabilizeTimer;

	private double autoStabilizeTreshold = 60.0;

	private Vector3D stabilize = Vector3D.Zero;

	private double stabilizeResetTimer;

	private double stabilizeResetTreshold = 1.0;

	private double systemsUpdateTimer;

	public int ColliderIndex = 1;

	public bool CollectAtmosphere;

	public bool sendResourceUpdate;

	private bool rcsThrustChanged;

	private Vector3D? _currRcsMoveThrust;

	private Vector3D? _currRcsRotationThrust;

	private ManeuverCourse AutoActivateCourse;

	private float[] RadarSignatureHealthMultipliers;

	public static bool DoSpecialPrint = false;

	private string RescueShipTag = "";

	private GameScenes.SceneId RescueShipSceneID = GameScenes.SceneId.AltCorp_Shuttle_CECA;

	private double RespawnTimeForShip = 60.0;

	public double timePassedSinceRequest;

	public SpaceObjectVessel CurrentSpawnedShip;

	public const double vesselRequestDistanceThreshold = 5000.0;

	private TimeSpan lastShipCollisionMessageTime = Server.Instance.RunTime;

	public override SpaceObjectType ObjectType => SpaceObjectType.Ship;

	private Vector3D? CurrRcsMoveThrust
	{
		get
		{
			return _currRcsMoveThrust;
		}
		set
		{
			if (_currRcsMoveThrust != value)
			{
				_currRcsMoveThrust = value;
				rcsThrustChanged = true;
			}
		}
	}

	private Vector3D? CurrRcsRotationThrust
	{
		get
		{
			return _currRcsRotationThrust;
		}
		set
		{
			if (_currRcsRotationThrust != value)
			{
				_currRcsRotationThrust = value;
				rcsThrustChanged = true;
			}
		}
	}

	public override float RadarSignature
	{
		get
		{
			return MathHelper.Clamp(base.RadarSignature * RadarSignatureHealthMultipliers[RadarSignatureHealthMultiplierIndex] + Systems.Sum((VesselComponent m) => m.RadarSignature), 0f, float.MaxValue);
		}
		protected set
		{
			base.RadarSignature = value;
		}
	}

	public int RadarSignatureHealthMultiplierIndex => (int)System.Math.Floor(Health / MaxHealth * 9f);

	public float TimeToLive => (float)(Health / (ExposureDamage * Server.VesselDecayRateMultiplier));

	public Ship(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid, initializeOrbit, position, velocity, forward, up)
	{
		Radius = 100.0;
		EventSystem.AddListener<ShipStatsMessage>(ShipStatsMessageListener);
		EventSystem.AddListener<ManeuverCourseRequest>(ManeuverCourseRequestListener);
		EventSystem.AddListener<DistressCallRequest>(DistressCallRequestListener);
		EventSystem.AddListener<VesselRequest>(VesselRequestListener);
		EventSystem.AddListener<VesselSecurityRequest>(VesselSecurityRequestListener);
		EventSystem.AddListener<RoomPressureMessage>(RoomPressureMessageListener);
		EventSystem.AddListener<VesselRequest>(RecycleItemMessageListener);
	}

	public void ResetRotationAndThrust()
	{
		isRcsOnline = false;
		RcsThrustVelocityDifference = Vector3D.Zero;
		RcsThrustDirection = Vector3D.Zero;
		rcsThrustResetTimer = 0.0;
		EngineThrustVelocityDifference = Vector3D.Zero;
		engineThrustPercentage = 0.0;
		currentEngineThrustPerc = 0.0;
		isRotationOnline = false;
		RotationThrustVelocityDifference = Vector3D.Zero;
		RotationThrustDirection = Vector3D.Zero;
		rotationThrustResetTimer = 0.0;
		stabilize = Vector3D.Zero;
		stabilizeResetTimer = 0.0;
		Rotation = Vector3D.Zero;
		AngularVelocity = Vector3D.Zero;
	}

	private async void ManeuverCourseRequestListener(NetworkData data)
	{
		var request = data as ManeuverCourseRequest;
		if (request.ShipGUID != Guid)
		{
			return;
		}
		if (request.CourseItems is { Count: > 0 })
		{
			await DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			SetMainVessel();
			CurrentCourse = ManeuverCourse.ParseNetworkData(request, this);
			await CurrentCourse.ReadNextManeuverCourse();
			if (!request.Activate.HasValue)
			{
				AutoActivateCourse = CurrentCourse;
			}
		}
		if (request.Activate.HasValue && CurrentCourse != null && CurrentCourse.CourseGUID == request.CourseGUID)
		{
			await CurrentCourse.ToggleActivated(request.Activate.Value);
		}
		await CurrentCourse.SendCourseStartResponse();
	}

	private Vector3D getClampedVector(float[] vec)
	{
		Vector3D retVal = vec.ToVector3D();
		if (retVal.SqrMagnitude > 1.0)
		{
			return retVal.Normalized;
		}
		return retVal;
	}

	public bool CalculateEngineThrust(double timeDelta)
	{
		double currPerc = engineThrustPercentage;
		if (!EngineOnLine)
		{
			currPerc = 0.0;
		}
		if (currPerc.IsNotEpsilonZeroD() || currentEngineThrustPerc.IsNotEpsilonZeroD())
		{
			int thrustPercSign = 1;
			if ((currentEngineThrustPerc < 0.0 && currPerc >= -4.94065645841247E-324) || (currentEngineThrustPerc > 0.0 && currPerc > double.Epsilon))
			{
				thrustPercSign = 1;
			}
			else if ((currentEngineThrustPerc > 0.0 && currPerc <= double.Epsilon) || (currentEngineThrustPerc < 0.0 && currPerc < double.Epsilon))
			{
				thrustPercSign = -1;
			}
			currentEngineThrustPerc += timeDelta * Engine.AccelerationBuildup * thrustPercSign;
			if (thrustPercSign == 1 && currentEngineThrustPerc > currPerc)
			{
				currentEngineThrustPerc = currPerc;
			}
			else if (thrustPercSign == -1 && currentEngineThrustPerc < currPerc)
			{
				currentEngineThrustPerc = currPerc;
			}
			EngineThrustVelocityDifference = GetEngineThrust() * timeDelta;
			ResetAutoStabilizeTimer();
			return true;
		}
		if (ExtraEngineThrust.IsNotEpsilonZero())
		{
			EngineThrustVelocityDifference = ExtraEngineThrust * timeDelta;
			ExtraEngineThrust = Vector3D.Zero;
			return true;
		}
		return false;
	}

	public Vector3D GetEngineThrust()
	{
		return Forward * currentEngineThrustPerc * (currentEngineThrustPerc > 0.0 ? EngineAcceleration : EngineReverseAcceleration);
	}

	public bool CalculateRcsThrust(double timeDelta)
	{
		if (!isRcsOnline && ExtraRcsThrustVelocityDifference.IsEpsilonZero())
		{
			if (CurrRcsMoveThrust.HasValue)
			{
				CurrRcsMoveThrust = null;
			}
			return false;
		}
		RcsThrustVelocityDifference = ExtraRcsThrustVelocityDifference + RcsThrustDirection * RCSAcceleration * timeDelta * Server.RcsThrustMultiplier;
		ExtraRcsThrustVelocityDifference = Vector3D.Zero;
		rcsThrustResetTimer += timeDelta;
		CurrRcsMoveThrust = RcsThrustVelocityDifference;
		ResetAutoStabilizeTimer();
		if (rcsThrustResetTimer >= rcsThrustResetTreshold)
		{
			isRcsOnline = false;
		}
		return true;
	}

	public bool CalculateRotationThrust(double timeDelta)
	{
		if (!isRotationOnline && ExtraRotationThrustVelocityDifference.IsEpsilonZero())
		{
			if (CurrRcsRotationThrust.HasValue)
			{
				CurrRcsRotationThrust = null;
			}
			return false;
		}
		RotationThrustVelocityDifference = ExtraRotationThrustVelocityDifference + RotationThrustDirection * RCSRotationAcceleration * timeDelta * Server.RcsRotationMultiplier;
		ExtraRotationThrustVelocityDifference = Vector3D.Zero;
		rotationThrustResetTimer += timeDelta;
		CurrRcsRotationThrust = RotationThrustVelocityDifference;
		ResetAutoStabilizeTimer();
		if (rotationThrustResetTimer >= rotationThrustResetTreshold)
		{
			isRotationOnline = false;
		}
		return true;
	}

	public void AddDockedVesselsThrust(Ship ship, double timeDelta)
	{
		if (ship.CalculateEngineThrust(timeDelta))
		{
			ExtraEngineThrust += ship.GetEngineThrust();
		}
		if (ship.CalculateRcsThrust(timeDelta))
		{
			ExtraRcsThrustVelocityDifference += ship.RcsThrustVelocityDifference;
		}
		if (ship.CalculateRotationThrust(timeDelta))
		{
			ExtraRotationThrustVelocityDifference += ship.RotationThrustVelocityDifference;
		}
		ship.CalculateRotationDampen(timeDelta);
	}

	public bool CalculateRotationDampen(double timeDelta)
	{
		if (RCS == null)
		{
			return false;
		}
		if (stabilize.IsNotEpsilonZero())
		{
			if (IsMainVessel)
			{
				DampenRotation(stabilize, timeDelta, RCS.MaxOperationRate);
			}
			else
			{
				(MainVessel as Ship).DampenRotation(stabilize, timeDelta, RCS.MaxOperationRate, RCSRotationStabilization);
			}
			stabilizeResetTimer += timeDelta;
			if (stabilizeResetTimer >= stabilizeResetTreshold)
			{
				stabilize = Vector3D.Zero;
			}
			ResetAutoStabilizeTimer();
			return true;
		}
		return false;
	}

	public async Task<bool> CalculateAutoStabilizeRotation(double timeDelta)
	{
		if (Rotation.IsNotEpsilonZero() && !AutoStabilizationDisabled && !AllVessels.Any((SpaceObjectVessel m) => m.VesselCrew.FirstOrDefault((Player n) => n.IsPilotingVessel) != null))
		{
			SpaceObjectVessel rcsVessel = null;
			if (!IsPrefabStationVessel)
			{
				List<SpaceObjectVessel> vessels = AllDockedVessels.Where((SpaceObjectVessel m) => m.RCS != null).ToList();
				if (RCS != null)
				{
					vessels.Add(this);
				}
				foreach (SpaceObjectVessel ves in vessels.OrderBy((SpaceObjectVessel m) => m.RCS.MaxOperationRate).Reverse())
				{
					if (ves.RCS.CanWork(ves.RCS.MaxOperationRate, 1f, standby: false))
					{
						rcsVessel = ves;
						break;
					}
				}
			}
			autoStabilizeTimer += timeDelta;
			if (autoStabilizeTimer > autoStabilizeTreshold)
			{
				if (rcsVessel != null)
				{
					await rcsVessel.RCS.GoOnLine();
					DampenRotation(Vector3D.One, timeDelta, rcsVessel.RCS.MaxOperationRate, rcsVessel.RCSRotationStabilization);
					return true;
				}
				if (IsPrefabStationVessel)
				{
					DampenRotation(Vector3D.One, timeDelta, 0.5, 1.0);
					return true;
				}
			}
			return false;
		}
		ResetAutoStabilizeTimer();
		return false;
	}

	public void ResetAutoStabilizeTimer()
	{
		if (MainVessel is Ship)
		{
			(MainVessel as Ship).autoStabilizeTimer = 0.0;
		}
	}

	public async Task CheckThrustStatsMessage()
	{
		if (rcsThrustChanged)
		{
			ShipStatsMessage ssm = new ShipStatsMessage();
			ssm.GUID = Guid;
			ssm.ThrustStats = new RcsThrustStats
			{
				MoveTrust = CurrRcsMoveThrust.HasValue ? CurrRcsMoveThrust.Value.ToFloatArray() : null,
				RotationTrust = CurrRcsRotationThrust.HasValue ? CurrRcsRotationThrust.Value.ToFloatArray() : null
			};
			rcsThrustChanged = false;
			await NetworkController.SendToClientsSubscribedTo(ssm, -1L, this);
		}
	}

	public async void ShipStatsMessageListener(NetworkData data)
	{
		var message = data as ShipStatsMessage;
		if (message.GUID != Guid)
		{
			return;
		}
		bool sendShipStatsMsg = false;
		ShipStatsMessage retMsg = new ShipStatsMessage
		{
			GUID = Guid,
			Temperature = Temperature,
			Health = Health,
			Armor = Armor,
			VesselObjects = new VesselObjects()
		};
		Player pl = Server.Instance.GetPlayer(message.Sender);
		bool requestEngine = EngineOnLine;
		bool requestRCS = message.Thrust != null || message.Rotation != null || message.AutoStabilize != null || (message.TargetStabilizationGUID.HasValue && message.Thrust == null);
		bool canUseEngine = false;
		bool canUseRCS = false;
		bool updateDM = false;
		if (requestEngine || requestRCS)
		{
			if (Engine != null && requestEngine)
			{
				Engine.ThrustActive = true;
				updateDM = true;
			}
			if (RCS != null && requestRCS && RCS.Status != SystemStatus.OnLine)
			{
				await RCS.GoOnLine();
				updateDM = true;
			}
			if (updateDM)
			{
				await MainDistributionManager.UpdateSystems(connectionsChanged: false, compoundRoomsChanged: false);
			}
			canUseEngine = Engine is { Status: SystemStatus.OnLine };
			canUseRCS = RCS is { Status: SystemStatus.OnLine };
		}
		if (Engine != null && message.EngineThrustPercentage.HasValue)
		{
			engineThrustPercentage = message.EngineThrustPercentage.Value;
			retMsg.EngineThrustPercentage = (float)engineThrustPercentage;
			Engine.RequiredThrust = (float)System.Math.Abs(engineThrustPercentage);
			Engine.ReverseThrust = engineThrustPercentage < 0.0;
			sendShipStatsMsg = true;
		}
		if (RCS != null && canUseRCS)
		{
			float opRateThr = 0f;
			float opRateRot = 0f;
			if (message.Thrust != null)
			{
				Vector3D thr = message.Thrust.ToVector3D();
				if (thr.SqrMagnitude > 1.0)
				{
					thr = thr.Normalized;
				}
				RcsThrustDirection = thr * RCS.MaxOperationRate;
				opRateThr = (float)RcsThrustDirection.Magnitude / RCS.MaxOperationRate;
				if (!RcsThrustDirection.IsEpsilonEqual(Vector3D.Zero, 0.0001))
				{
					rcsThrustResetTimer = 0.0;
					isRcsOnline = true;
					retMsg.TargetStabilizationGUID = -1L;
					sendShipStatsMsg = true;
				}
				else
				{
					isRcsOnline = false;
				}
			}
			if (message.Rotation != null && (CurrentCourse == null || !CurrentCourse.IsInProgress))
			{
				Vector3D rot = message.Rotation.ToVector3D();
				if (rot.SqrMagnitude > 1.0)
				{
					rot = rot.Normalized;
				}
				RotationThrustDirection = rot * RCS.MaxOperationRate;
				opRateThr = (float)RotationThrustDirection.Magnitude / RCS.MaxOperationRate;
				if (!RotationThrustDirection.IsEpsilonEqual(Vector3D.Zero, 0.0001))
				{
					rotationThrustResetTimer = 0.0;
					isRotationOnline = true;
				}
				else
				{
					isRotationOnline = false;
				}
			}
			if (message.AutoStabilize != null)
			{
				stabilize = message.AutoStabilize.ToVector3D();
				stabilizeResetTimer = 0.0;
				RCS.OperationRate = RCS.MaxOperationRate;
			}
			if (RCS.OperationRate == 0f)
			{
				RCS.OperationRate = System.Math.Max(opRateThr, opRateRot);
			}
			if (message.TargetStabilizationGUID.HasValue && message.Thrust == null)
			{
				if (Server.Instance.GetObject(message.TargetStabilizationGUID.Value) is SpaceObjectVessel target && StabilizeToTarget(target))
				{
					retMsg.TargetStabilizationGUID = target.Guid;
				}
				else
				{
					retMsg.TargetStabilizationGUID = -1L;
				}
				sendShipStatsMsg = true;
			}
		}
		if (message.VesselObjects == null)
		{
			return;
		}
		bool dmUpdate = false;
		bool dmUpdateConn = false;
		bool dmUpdateCav = false;
		if (message.VesselObjects.RoomTriggers is { Count: > 0 })
		{
			dmUpdate = true;
			foreach (RoomDetails trigData in message.VesselObjects.RoomTriggers)
			{
				Room room = MainDistributionManager.GetRoom(new VesselObjectID(Guid, trigData.InSceneID));
				if (room != null)
				{
					room.UseGravity = trigData.UseGravity;
					room.AirFiltering = trigData.AirFiltering;
				}
			}
		}
		if (message.VesselObjects.Generators is { Count: > 0 })
		{
			dmUpdate = true;
			foreach (GeneratorDetails gd in message.VesselObjects.Generators)
			{
				Generator gen = MainDistributionManager.GetGenerator(new VesselObjectID(Guid, gd.InSceneID));
				await gen.SetDetails(gd);
			}
		}
		if (message.VesselObjects.SubSystems is { Count: > 0 })
		{
			dmUpdate = true;
			foreach (SubSystemDetails ssd in message.VesselObjects.SubSystems)
			{
				SubSystem ss = MainDistributionManager.GetSubSystem(new VesselObjectID(Guid, ssd.InSceneID));
				await ss.SetDetails(ssd);
			}
		}
		if (message.VesselObjects.Doors != null)
		{
			List<DoorDetails> doorsChanged = new List<DoorDetails>();
			foreach (DoorDetails dd2 in message.VesselObjects.Doors)
			{
				Door door2 = Doors.Find((Door m) => m.ID.InSceneID == dd2.InSceneID);
				if (door2 != null && (door2.HasPower != dd2.HasPower || door2.IsLocked != dd2.IsLocked || door2.IsOpen != dd2.IsOpen))
				{
					bool prevSealed = door2.IsSealed;
					door2.HasPower = dd2.HasPower;
					door2.IsLocked = dd2.IsLocked;
					door2.IsOpen = dd2.IsOpen;
					DoorDetails doorDetails2 = door2.GetDetails();
					dmUpdate = true;
					dmUpdateCav = true;
					if (!dd2.EquilizePressure && !door2.IsSealed && prevSealed)
					{
						VesselObjectID id = new VesselObjectID(Guid, dd2.InSceneID);
						doorDetails2.PressureEquilizationTime = MainDistributionManager.PressureEquilizationTime(id, out doorDetails2.AirFlowDirection, out doorDetails2.AirSpeed);
					}
					doorsChanged.Add(doorDetails2);
					door2.StatusChanged = false;
				}
			}
			if (doorsChanged.Count > 0)
			{
				retMsg.VesselObjects.Doors = doorsChanged;
				sendShipStatsMsg = true;
			}
		}
		if (message.VesselObjects.SceneTriggerExecutors != null)
		{
			List<SceneTriggerExecutorDetails> SceneTriggerExecutorsChanged = [];
			foreach (SceneTriggerExecutorDetails executorDetails in message.VesselObjects.SceneTriggerExecutors)
			{
				if (executorDetails == null) return;

				SceneTriggerExecutor executor = SceneTriggerExecutors.Find((SceneTriggerExecutor m) => m.InSceneID == executorDetails.InSceneID);
				if (executor != null)
				{
					SceneTriggerExecutorsChanged.Add(executor.ChangeState(message.Sender, executorDetails));
				}
				else
				{
					Debug.LogWarningFormat("Could not find scene trigger executor requested by client in state. Id: {0}.", executorDetails.InSceneID);
				}
			}
			if (SceneTriggerExecutorsChanged.Count > 0)
			{
				retMsg.VesselObjects.SceneTriggerExecutors = SceneTriggerExecutorsChanged;
				sendShipStatsMsg = true;
			}
		}
		if (message.VesselObjects.AttachPoints != null)
		{
			foreach (AttachPointDetails apd in message.VesselObjects.AttachPoints)
			{
				if (apd.AuxDetails is MachineryPartSlotAuxDetails details)
				{
					VesselObjectID slotKey = new VesselObjectID(Guid, apd.InSceneID);
					MainDistributionManager.GetVesselComponentByPartSlot(slotKey)?.SetMachineryPartSlotActive(slotKey, details.IsActive);
				}
			}
		}
		if (message.VesselObjects.DockingPorts != null)
		{
			List<SceneDockingPortDetails> dockingPortsChanged = new List<SceneDockingPortDetails>();
			foreach (SceneDockingPortDetails stDetails in message.VesselObjects.DockingPorts)
			{
				VesselDockingPort port = DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == stDetails.ID.InSceneID);
				if (port == null || stDetails.DockedToID == null || !Server.Instance.DoesObjectExist(stDetails.DockedToID.VesselGUID))
				{
					continue;
				}
				if (stDetails.DockingStatus)
				{
					Ship dockToShip = Server.Instance.GetVessel(stDetails.DockedToID.VesselGUID) as Ship;
					VesselDockingPort parentPort = dockToShip.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == stDetails.DockedToID.InSceneID);
					System.Diagnostics.Debug.Assert(parentPort != null);
					if (!await DockToVessel(port, parentPort, dockToShip))
					{
						continue;
					}
					stDetails.CollidersCenterOffset = DockedToMainVessel.VesselData.CollidersCenterOffset;
					stDetails.RelativePositionUpdate = [];
					stDetails.RelativeRotationUpdate = [];
					foreach (Ship s in DockedToMainVessel.AllDockedVessels.Cast<Ship>())
					{
						stDetails.RelativePositionUpdate.Add(s.Guid, s.RelativePositionFromParent.ToFloatArray());
						stDetails.RelativeRotationUpdate.Add(s.Guid, s.RelativeRotationFromParent.ToFloatArray());
					}
					stDetails.ExecutorsMerge = port.GetMergedExecutors(parentPort);
					stDetails.PairedDoors = GetPairedDoors(port);
					stDetails.RelativePosition = RelativePositionFromParent.ToFloatArray();
					stDetails.RelativeRotation = RelativeRotationFromParent.ToFloatArray();
					dockingPortsChanged.Add(stDetails);
				}
				else if (port.DockedToID != null)
				{
					Ship dockedToShip = port.DockedVessel as Ship;
					VesselDockingPort dockedToPort = dockedToShip.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == port.DockedToID.InSceneID);
					System.Diagnostics.Debug.Assert(dockedToPort != null);
					if (!await UndockFromVessel(port, dockedToShip, dockedToPort, stDetails))
					{
						continue;
					}
					stDetails.ExecutorsMerge = new List<ExecutorMergeDetails>();
					foreach (SceneTriggerExecutor exec in port.MergeExecutors.Keys)
					{
						stDetails.ExecutorsMerge.Add(new ExecutorMergeDetails
						{
							ParentTriggerID = new VesselObjectID(Guid, exec.InSceneID)
						});
					}
					stDetails.RelativePosition = RelativeRotationFromParent.ToFloatArray();
					stDetails.RelativeRotation = RelativeRotationFromParent.ToFloatArray();
					dockingPortsChanged.Add(stDetails);
				}
				else
				{
					Debug.LogWarning("Tried to undock non-docked docking port", port.ID);
				}
			}
			if (dockingPortsChanged.Count > 0)
			{
				retMsg.VesselObjects.DockingPorts = dockingPortsChanged;
				sendShipStatsMsg = true;
				foreach (SceneDockingPortDetails sdp in dockingPortsChanged)
				{
					if (sdp.PairedDoors == null)
					{
						continue;
					}
					foreach (PairedDoorsDetails pdd in sdp.PairedDoors)
					{
						Door door = Doors.Find((Door m) => m.ID.Equals(pdd.DoorID));
						if (!door.LockedAutoToggle)
						{
							continue;
						}
						bool locked = pdd.PairedDoorID == null;
						if (door.IsLocked != locked)
						{
							door.IsLocked = locked;
							if (retMsg.VesselObjects.Doors == null)
							{
								retMsg.VesselObjects.Doors = new List<DoorDetails>();
							}
							DoorDetails doorDetails = door.GetDetails();
							DoorDetails dd = retMsg.VesselObjects.Doors.Find((DoorDetails m) => m.InSceneID == doorDetails.InSceneID);
							if (dd != null)
							{
								dd.IsLocked = door.IsLocked;
							}
							else
							{
								retMsg.VesselObjects.Doors.Add(doorDetails);
							}
						}
					}
				}
			}
		}
		if (message.VesselObjects.SpawnPoints != null)
		{
			List<SpawnPointStats> spawnPointsChanged = new List<SpawnPointStats>();
			foreach (SpawnPointStats stats in message.VesselObjects.SpawnPoints)
			{
				ShipSpawnPoint sp = SpawnPoints.Find((ShipSpawnPoint m) => m.SpawnPointID == stats.InSceneID);
				if (sp != null)
				{
					SpawnPointStats changed = await sp.SetStats(stats, pl);
					if (changed != null)
					{
						spawnPointsChanged.Add(changed);
					}
				}
			}
			if (spawnPointsChanged.Count > 0)
			{
				retMsg.VesselObjects.SpawnPoints = spawnPointsChanged;
				sendShipStatsMsg = true;
			}
		}
		if (message.VesselObjects.EmblemId != null)
		{
			EmblemId = message.VesselObjects.EmblemId;
			retMsg.VesselObjects.EmblemId = EmblemId;
			sendShipStatsMsg = true;
		}
		if (dmUpdate)
		{
			await MainDistributionManager.UpdateSystems(dmUpdateConn, dmUpdateCav);
			retMsg.VesselObjects.SubSystems = MainDistributionManager.GetSubSystemsDetails(changedOnly: true, Guid);
			sendShipStatsMsg |= retMsg.VesselObjects.SubSystems.Count > 0;
			retMsg.VesselObjects.Generators = MainDistributionManager.GetGeneratorsDetails(changedOnly: true, Guid);
			sendShipStatsMsg |= retMsg.VesselObjects.Generators.Count > 0;
			retMsg.VesselObjects.RoomTriggers = MainDistributionManager.GetRoomsDetails(changedOnly: true, Guid);
			sendShipStatsMsg |= retMsg.VesselObjects.RoomTriggers.Count > 0;
			retMsg.VesselObjects.ResourceContainers = MainDistributionManager.GetResourceContainersDetails(changedOnly: true, Guid);
			sendShipStatsMsg |= retMsg.VesselObjects.ResourceContainers.Count > 0;
		}
		if (message.GatherAtmosphere.HasValue)
		{
			CollectAtmosphere = message.GatherAtmosphere.Value;
		}
		if (message.SelfDestructTime.HasValue)
		{
			if (message.SelfDestructTime.Value >= 0f)
			{
				SelfDestructTimer = new SelfDestructTimer(this, message.SelfDestructTime.Value);
			}
			else
			{
				SelfDestructTimer = null;
			}
			sendShipStatsMsg = true;
		}
		if (sendShipStatsMsg)
		{
			await NetworkController.SendToClientsSubscribedTo(retMsg, -1L, this);
		}
	}

	public async Task SendCollision(double vel, double impulse, double time, long otherShipGUID)
	{
		if (time <= double.Epsilon)
		{
			time = 1.0;
		}
		float dmg = (float)(impulse / time / 10000.0 * (IsDebrisFragment ? Server.DebrisVesselCollisionDamageMultiplier : Server.VesselCollisionDamageMultiplier));
		if (dmg < 0.1)
		{
			return;
		}
		SpaceObjectVessel otherVessel = Server.Instance.GetVessel(otherShipGUID);
		List<SpaceObjectVessel> list = [this, .. AllDockedVessels];
		SpaceObjectVessel vessel = list.OrderBy((SpaceObjectVessel x) => MathHelper.RandomNextDouble()).First();
		await vessel.ChangeHealthBy(0f - dmg, null, VesselRepairPoint.Priority.External, force: false, otherVessel is
		{
			IsDebrisFragment: true
		} ? VesselDamageType.LargeDebrisHit : VesselDamageType.Collision);
		if (otherVessel != null)
		{
			list = [otherVessel, .. otherVessel.AllDockedVessels];
			vessel = list.OrderBy((SpaceObjectVessel x) => MathHelper.RandomNextDouble()).First();
			await vessel.ChangeHealthBy(0f - dmg, null, VesselRepairPoint.Priority.External, force: false, IsDebrisFragment ? VesselDamageType.LargeDebrisHit : VesselDamageType.Collision);
		}
		if ((Server.Instance.RunTime - lastShipCollisionMessageTime).TotalSeconds > 0.1)
		{
			lastShipCollisionMessageTime = Server.Instance.RunTime;
			ShipCollisionMessage scm = new ShipCollisionMessage
			{
				CollisionVelocity = (float)vel,
				ShipOne = Guid,
				ShipTwo = otherShipGUID
			};
			await NetworkController.SendToClientsSubscribedTo(scm, -1L, this, otherShipGUID != -1 ? Server.Instance.GetVessel(otherShipGUID) : null);
		}
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		if (IsDocked && DockedToMainVessel is Ship ship)
		{
			return ship.GetSpawnResponseData(pl);
		}
		bool isDummy = (pl.Position - Position).SqrMagnitude > 100000000.0;
		return new SpawnShipResponseData
		{
			GUID = Guid,
			Data = VesselData,
			VesselObjects = GetVesselObjects(),
			IsDummy = isDummy,
			DockedVessels = GetDockedVesselsData(),
			DockingControlsDisabled = DockingControlsDisabled,
			SecurityPanelsLocked = SecurityPanelsLocked
		};
	}

	public List<DockedVesselData> GetDockedVesselsData()
	{
		if (AllDockedVessels == null || AllDockedVessels.Count == 0)
		{
			return null;
		}
		List<DockedVesselData> retVal = new List<DockedVesselData>();
		foreach (SpaceObjectVessel ves in AllDockedVessels)
		{
			if (ves.ObjectType == SpaceObjectType.Ship)
			{
				Ship sh = ves as Ship;
				retVal.Add(new DockedVesselData
				{
					GUID = sh.Guid,
					Data = sh.VesselData,
					VesselObjects = sh.GetVesselObjects(),
					DockingControlsDisabled = sh.DockingControlsDisabled,
					SecurityPanelsLocked = sh.SecurityPanelsLocked
				});
			}
		}
		return retVal;
	}

	public override InitializeSpaceObjectMessage GetInitializeMessage()
	{
		InitializeSpaceObjectMessage msg = new InitializeSpaceObjectMessage();
		msg.GUID = Guid;
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
		InitializeShipAuxData auxData = new InitializeShipAuxData();
		auxData.VesselObjects = GetVesselObjects();
		msg.AuxData = auxData;
		return msg;
	}

	private async Task FillShipData(GameScenes.SceneId sceneID, List<StructureSceneData> structureSceneDataList, bool loadDynamicObjects = true, float? health = null)
	{
		StructureSceneData data = ObjectCopier.DeepCopy(structureSceneDataList.Find((StructureSceneData m) => m.ItemID == (short)sceneID));
		if (data == null)
		{
			return;
		}
		RadarSignatureHealthMultipliers = ObjectCopier.Copy(data.RadarSignatureHealthMultipliers);
		RadarSignature = data.RadarSignature;
		HasSecuritySystem = data.HasSecuritySystem;
		MaxHealth = data.MaxHealth;
		BaseArmor = data.BaseArmor;
		InvulnerableWhenDocked = data.InvulnerableWhenDocked;
		if (health.HasValue)
		{
			await SetHealthAsync(health.Value);
		}
		else
		{
			await SetHealthAsync(data.Health);
			if (data.SpawnSettings != null)
			{
				SceneSpawnSettings[] spawnSettings = data.SpawnSettings;
				foreach (SceneSpawnSettings sss in spawnSettings)
				{
					if (CheckTag(sss.Tag, sss.Case))
					{
						await SetHealthAsync(MaxHealth * MathHelper.RandomRange(sss.MinHealth, sss.MaxHealth));
						break;
					}
				}
			}
		}
		if (data.AdditionalTags != null && data.AdditionalTags.Length != 0)
		{
			TagChance tc = data.AdditionalTags[MathHelper.RandomRange(0, data.AdditionalTags.Length)];
			if (MathHelper.RandomNextDouble() < tc.Chance)
			{
				if (VesselData.Tag == null)
				{
					VesselData.Tag = "";
				}
				VesselData vesselData = VesselData;
				vesselData.Tag = vesselData.Tag + (VesselData.Tag == "" ? "" : ";") + tc.Tag;
			}
		}
		if (data.Doors is { Count: > 0 })
		{
			foreach (DoorData door in data.Doors)
			{
				Doors.Add(new Door
				{
					ID = new VesselObjectID(Guid, door.InSceneID),
					IsSealable = door.IsSealable,
					HasPower = door.HasPower,
					IsLocked = door.IsLocked,
					IsOpen = door.IsOpen,
					LockedAutoToggle = door.LockedAutoToggle,
					PassageArea = door.PassageArea,
					PositionRelativeToDockingPort = door.PositionRelativeToDockingPort.ToVector3D()
				});
			}
		}
		if (data.CargoBay != null)
		{
			CargoBay = new CargoBay(this, data.CargoBay.CargoCompartments)
			{
				InSceneID = data.CargoBay.InSceneID
			};
		}
		if (data.NameTags is { Count: > 0 })
		{
			foreach (NameTagData ntd in data.NameTags)
			{
				NameTags.Add(ntd);
			}
		}
		if (data.Rooms is { Count: > 0 })
		{
			foreach (RoomData roomData in data.Rooms)
			{
				Room room = new Room
				{
					ID = new VesselObjectID(Guid, roomData.InSceneID),
					ParentVessel = this,
					UseGravity = roomData.UseGravity,
					AirFiltering = roomData.AirFiltering,
					Volume = roomData.Volume,
					GravityAutoToggle = roomData.GravityAutoToggle,
					AirPressure = roomData.AirPressure,
					AirQuality = roomData.AirQuality,
					PressurizeSpeed = roomData.PressurizeSpeed,
					DepressurizeSpeed = roomData.DepressurizeSpeed,
					VentSpeed = roomData.VentSpeed
				};
				Rooms.Add(room);
			}
		}
		if (data.SceneTriggerExecutors is { Count: > 0 })
		{
			foreach (SceneTriggerExecutorData std2 in data.SceneTriggerExecutors)
			{
				if (std2.TagAction != 0)
				{
					bool hasTag2 = CheckTag(std2.Tags);
					if ((std2.TagAction == TagAction.EnableIfTagIs && !hasTag2) || (std2.TagAction == TagAction.DisableIfTagIs && hasTag2))
					{
						continue;
					}
				}
				SceneTriggerExecutor ex2 = new SceneTriggerExecutor
				{
					InSceneID = std2.InSceneID,
					ParentShip = this
				};
				ex2.SetStates(std2.States, std2.DefaultStateID);
				if (std2.ProximityTriggers is { Count: > 0 })
				{
					ex2.ProximityTriggers = new Dictionary<int, SceneTriggerProximity>();
					foreach (SceneTriggerProximityData prox in std2.ProximityTriggers)
					{
						ex2.ProximityTriggers.Add(prox.TriggerID, new SceneTriggerProximity
						{
							TriggerID = prox.TriggerID,
							ActiveStateID = prox.ActiveStateID,
							InactiveStateID = prox.InactiveStateID,
							ObjectsInTrigger = new List<long>()
						});
					}
				}
				SceneTriggerExecutors.Add(ex2);
			}
		}
		if (data.DockingPorts is { Count: > 0 })
		{
			List<VesselDockingPort> dockingPorts = [];
			foreach (SceneDockingPortData std in data.DockingPorts)
			{
				VesselObjectID id = new VesselObjectID(Guid, std.InSceneID);
				VesselDockingPort newPort = new VesselDockingPort
				{
					ParentVessel = this,
					ID = id,
					DockedVessel = null,
					DockingStatus = false,
					DockedToID = null,
					Position = std.Position.ToVector3D(),
					Rotation = std.Rotation.ToQuaternionD(),
					DoorsIDs = std.DoorsIDs,
					DoorPairingDistance = std.DoorPairingDistance,
					OrderID = std.OrderID,
					Locked = std.Locked,
					MergeExecutors = new Dictionary<SceneTriggerExecutor, Vector3D>(),
					MergeExecutorsDistance = std.MergeExecutorDistance
				};
				foreach (SceneDockingPortExecutorMerge mer in std.MergeExecutors)
				{
					SceneTriggerExecutor ex = SceneTriggerExecutors.Find((SceneTriggerExecutor m) => m.InSceneID == mer.InSceneID);
					newPort.MergeExecutors.Add(ex, mer.Position.ToVector3D());
				}
				dockingPorts.Add(newPort);
			}
			DockingPorts = ImmutableArray.Create(dockingPorts.ToArray());
		}
		if (data.SpawnPoints is { Count: > 0 })
		{
			foreach (SpawnPointData spd in data.SpawnPoints)
			{
				if (spd.TagAction != 0)
				{
					bool hasTag = CheckTag(spd.Tags);
					if ((spd.TagAction == TagAction.EnableIfTagIs && !hasTag) || (spd.TagAction == TagAction.DisableIfTagIs && hasTag))
					{
						continue;
					}
				}
				SceneTriggerExecutor executor = spd.ExecutorID > 0 ? SceneTriggerExecutors.Find((SceneTriggerExecutor m) => m.InSceneID == spd.ExecutorID) : null;
				ShipSpawnPoint newSpawnPoint = new ShipSpawnPoint
				{
					SpawnPointID = spd.InSceneID,
					Type = spd.Type,
					Executor = executor,
					ExecutorStateID = spd.ExecutorStateID,
					Ship = this,
					State = SpawnPointState.Unlocked,
					Player = null
				};
				if (newSpawnPoint.Executor != null)
				{
					executor.SpawnPoint = newSpawnPoint;
					newSpawnPoint.ExecutorOccupiedStateIDs = new List<int>(spd.ExecutorOccupiedStateIDs);
				}
				SpawnPoints.Add(newSpawnPoint);
			}
		}
		if (data.AttachPoints is { Count: > 0 })
		{
			foreach (BaseAttachPointData apd2 in data.AttachPoints)
			{
				AttachPointsTypes[new VesselObjectID(Guid, apd2.InSceneID)] = apd2.AttachPointType;
				AttachPoints.Add(apd2.InSceneID, new VesselAttachPoint
				{
					Vessel = this,
					InSceneID = apd2.InSceneID,
					Type = apd2.AttachPointType,
					ItemTypes = apd2.ItemTypes != null ? new List<ItemType>(apd2.ItemTypes) : new List<ItemType>(),
					GenericSubTypes = apd2.GenericSubTypes != null ? new List<GenericItemSubType>(apd2.GenericSubTypes) : new List<GenericItemSubType>(),
					MachineryPartTypes = apd2.MachineryPartTypes != null ? new List<MachineryPartType>(apd2.MachineryPartTypes) : new List<MachineryPartType>(),
					Item = null
				});
			}
		}
		if (data.SubSystems is { Count: > 0 })
		{
			foreach (SubSystemData ssd in data.SubSystems)
			{
				Systems.Add(createSubSystem(ssd));
			}
		}
		if (data.Generators is { Count: > 0 })
		{
			foreach (GeneratorData gd in data.Generators)
			{
				Systems.Add(await CreateGeneratorAsync(gd));
			}
		}
		if (data.RepairPoints is { Count: > 0 })
		{
			float maxHealth2 = (float)System.Math.Ceiling(MaxHealth / data.RepairPoints.Count);
			foreach (VesselRepairPointData rpd in data.RepairPoints)
			{
				RepairPoints.Add(await VesselRepairPoint.CreateVesselRepairPointAsync(this, rpd, maxHealth2));
			}
			float h = Health;
			foreach (VesselRepairPoint rp in from m in RepairPoints.ToArray()
				orderby MathHelper.RandomNextInt()
				select m)
			{
				if (h > rp.MaxHealth)
				{
					await rp.SetHealthAsync(rp.MaxHealth);
					h -= rp.MaxHealth;
					continue;
				}
				await rp.SetHealthAsync(h);
				break;
			}
		}
		if (data.SpawnObjectChanceData is { Count: > 0 })
		{
			foreach (SpawnObjectsWithChanceData d in data.SpawnObjectChanceData)
			{
				float cha = (float)MathHelper.RandomNextDouble();
				SpawnChance.Add(new SpawnObjectsWithChance
				{
					InSceneID = d.InSceneID,
					Chance = cha
				});
			}
		}
		if (!loadDynamicObjects || data.DynamicObjects is not { Count: > 0 })
		{
			return;
		}
		double rand = 0.0;
		bool spawnDobj = false;
		float respawnTime = -1f;
		float maxHealth = -1f;
		float minHealth = -1f;
		float wearMultiplier = 1f;
		foreach (DynamicObjectSceneData dosd in data.DynamicObjects)
		{
			rand = MathHelper.RandomNextDouble();
			spawnDobj = false;
			respawnTime = -1f;
			maxHealth = -1f;
			minHealth = -1f;
			wearMultiplier = 1f;
			if (dosd.SpawnSettings != null && dosd.SpawnSettings.Length != 0)
			{
				DynaminObjectSpawnSettings[] spawnSettings2 = dosd.SpawnSettings;
				foreach (DynaminObjectSpawnSettings doss in spawnSettings2)
				{
					if (!doss.Tag.IsNullOrEmpty() && CheckTag(doss.Tag, doss.Case) && (doss.SpawnChance < 0f || doss.SpawnChance > rand))
					{
						spawnDobj = true;
						respawnTime = doss.RespawnTime;
						maxHealth = doss.MaxHealth;
						minHealth = doss.MinHealth;
						wearMultiplier = doss.WearMultiplier;
						break;
					}
				}
			}
			if (spawnDobj)
			{
				DynamicObject dobj = await DynamicObject.CreateDynamicObjectAsync(dosd, this, -1L);
				dobj.RespawnTime = respawnTime;
				dobj.SpawnMaxHealth = maxHealth;
				dobj.SpawnMinHealth = minHealth;
				dobj.SpawnWearMultiplier = wearMultiplier;
				if (dobj.Item is not null && maxHealth >= 0f && minHealth >= 0f)
				{
					IDamageable idmg = dobj.Item;
					idmg.Health = (int)(idmg.MaxHealth * MathHelper.Clamp(MathHelper.RandomRange(minHealth, maxHealth), 0f, 1f));
				}
				AttachPointDetails apd = null;
				if (dosd.AttachPointInSceneId > 0 && dobj.Item != null)
				{
					apd = new AttachPointDetails
					{
						InSceneID = dosd.AttachPointInSceneId
					};
					dobj.Item.SetAttachPoint(apd);
				}
				if (dobj.Item is MachineryPart part)
				{
					part.WearMultiplier = wearMultiplier;
				}
				dobj.APDetails = apd;
			}
		}
	}

	private async Task CreateShipData(string shipRegistration, string shipTag, GameScenes.SceneId shipItemID, bool loadDynamicObjects, float? health = null)
	{
		VesselData = new VesselData
		{
			Id = Guid,
			VesselRegistration = shipRegistration,
			VesselName = "",
			Tag = shipTag,
			SceneID = shipItemID,
			IsDebrisFragment = IsDebrisFragment,
			CreationSolarSystemTime = Server.SolarSystemTime
		};
		await FillShipData(VesselData.SceneID, StaticData.StructuresDataList, loadDynamicObjects, health);
		VesselData.RadarSignature = RadarSignature;
		ReadBoundsAndMass(VesselData.SceneID, Vector3D.Zero);
		RecalculateCenter();
	}

	private void ReadBoundsAndMass(GameScenes.SceneId sceneID, Vector3D connectionOffset)
	{
		StructureSceneData sceneData = StaticData.StructuresDataList.Find((StructureSceneData m) => m.ItemID == (short)sceneID);
		if (sceneData == null)
		{
			return;
		}
		Mass += sceneData.Mass > 0f ? sceneData.Mass : 1f;
		HeatCollectionFactor = sceneData.HeatCollectionFactor;
		HeatDissipationFactor = sceneData.HeatDissipationFactor;
		if (sceneData.Colliders == null)
		{
			return;
		}
		if (sceneData.Colliders.PrimitiveCollidersData is { Count: > 0 })
		{
			foreach (PrimitiveColliderData data2 in sceneData.Colliders.PrimitiveCollidersData)
			{
				PrimitiveCollidersData.Add(new VesselPrimitiveColliderData
				{
					Type = data2.Type,
					CenterPosition = data2.Center.ToVector3D() + connectionOffset,
					Bounds = data2.Size.ToVector3D(),
					AffectingCenterOfMass = data2.AffectingCenterOfMass,
					ColliderID = ColliderIndex++
				});
			}
		}
		if (sceneData.Colliders.MeshCollidersData == null)
		{
			return;
		}
		foreach (MeshData data in sceneData.Colliders.MeshCollidersData)
		{
			MeshCollidersData.Add(new VesselMeshColliderData
			{
				AffectingCenterOfMass = data.AffectingCenterOfMass,
				ColliderID = ColliderIndex++,
				Indices = data.Indices,
				Vertices = data.Vertices.GetVertices(),
				CenterPosition = data.CenterPosition.ToVector3D() + connectionOffset,
				Bounds = data.Bounds.ToVector3D(),
				Rotation = data.Rotation.ToQuaternionD(),
				Scale = data.Scale.ToVector3D()
			});
		}
	}

	public static async Task<Ship> CreateNewShip(GameScenes.SceneId sceneID, string registration = "", long shipID = -1L, List<long> nearArtificialBodyGUIDs = null, List<long> celestialBodyGUIDs = null, Vector3D? positionOffset = null, Vector3D? velocityAtPosition = null, QuaternionD? localRotation = null, string vesselTag = "", bool checkPosition = true, double distanceFromSurfacePercMin = 0.03, double distanceFromSurfacePercMax = 0.3, SpawnRuleOrbit spawnRuleOrbit = null, double celestialBodyDeathDistanceMultiplier = 1.5, double artificialBodyDistanceCheck = 100.0, float? health = null, bool isDebrisFragment = false)
	{
		Vector3D shipPos = Vector3D.Zero;
		Vector3D shipVel = Vector3D.Zero;
		Vector3D shipForward = Vector3D.Forward;
		Vector3D shipUp = Vector3D.Up;
		Ship newShip = new Ship(shipID < 0 ? GUIDFactory.NextVesselGUID() : shipID, initializeOrbit: false, shipPos, shipVel, shipForward, shipUp)
		{
			IsDebrisFragment = isDebrisFragment
		};
		await newShip.CreateShipData(registration, vesselTag, sceneID, loadDynamicObjects: true, health);
		newShip.DistributionManager = new DistributionManager(newShip);
		Server.Instance.PhysicsController.CreateAndAddRigidBody(newShip);
		Server.Instance.SolarSystem.GetSpawnPosition(SpaceObjectType.Ship, newShip.Radius, checkPosition, out shipPos, out shipVel, out shipForward, out shipUp, nearArtificialBodyGUIDs, celestialBodyGUIDs, positionOffset, velocityAtPosition, localRotation, distanceFromSurfacePercMin, distanceFromSurfacePercMax, spawnRuleOrbit, celestialBodyDeathDistanceMultiplier, artificialBodyDistanceCheck, out OrbitParameters orbit);
		newShip.InitializeOrbit(shipPos, shipVel, shipForward, shipUp, orbit);
		if (registration.IsNullOrEmpty())
		{
			newShip.VesselData.VesselRegistration = Server.NameGenerator.GenerateObjectRegistration(SpaceObjectType.Ship, newShip.Orbit.Parent.CelestialBody, sceneID);
		}
		foreach (DynamicObject dobj in newShip.DynamicObjects.Values)
		{
			if (dobj.Item is { AttachPointID: not null })
			{
				VesselComponent comp = newShip.MainDistributionManager.GetVesselComponentByPartSlot(dobj.Item.AttachPointID);
				if (comp != null && dobj.Item is MachineryPart part)
				{
					comp.FitPartToSlot(part.AttachPointID, part);
				}
			}
		}
		await newShip.MainDistributionManager.UpdateSystems();
		Server.Instance.Add(newShip);
		newShip.SetPhysicsParameters();
		return newShip;
	}

	public override void AddPlayerToCrew(Player pl)
	{
		if (!VesselCrew.Contains(pl))
		{
			VesselCrew.Add(pl);
			RemovePlayerFromExecutors(pl);
		}
	}

	public override void RemovePlayerFromCrew(Player pl, bool checkDetails = false)
	{
		VesselCrew.Remove(pl);
		if (checkDetails)
		{
			RemovePlayerFromExecutors(pl);
		}
	}

	public void RemovePlayerFromExecutors(Player pl)
	{
		List<SceneTriggerExecutorDetails> executors = new List<SceneTriggerExecutorDetails>();
		foreach (SceneTriggerExecutor ex in SceneTriggerExecutors)
		{
			SceneTriggerExecutorDetails det = ex.RemovePlayerFromExecutor(pl);
			if (det == null)
			{
				det = ex.RemovePlayerFromProximity(pl);
			}
			if (det != null)
			{
				executors.Add(det);
			}
		}
		if (executors.Count > 0)
		{
			ShipStatsMessage retStatsMsg = new ShipStatsMessage();
			retStatsMsg.VesselObjects = new VesselObjects();
			retStatsMsg.GUID = Guid;
			retStatsMsg.VesselObjects.SceneTriggerExecutors = executors;
			ShipStatsMessageListener(retStatsMsg);
		}
	}

	public override bool HasPlayerInCrew(Player pl)
	{
		return VesselCrew.Contains(pl);
	}

	public void RemovePlayerFromRoom(Player pl)
	{
		MainDistributionManager.RemovePlayerFromRoom(pl);
	}

	public override async Task UpdateTimers(double deltaTime)
	{
		await base.UpdateTimers(deltaTime);
		if (SelfDestructTimer != null)
		{
			SelfDestructTimer.Update();
			if (SelfDestructTimer.Time == 0f)
			{
				SelfDestructTimer = null;
				if (DockedToVessel is Ship)
				{
					DockedToVessel.SelfDestructTimer = new SelfDestructTimer(this, MathHelper.RandomRange(1f, 3f));
				}
				foreach (SpaceObjectVessel ves in DockedVessels.Where((SpaceObjectVessel m) => m is Ship))
				{
					ves.SelfDestructTimer = new SelfDestructTimer(ves, MathHelper.RandomRange(1f, 3f));
				}
				await ChangeHealthBy(0f - MaxHealth, null, VesselRepairPoint.Priority.None, force: true, VesselDamageType.SelfDestruct);
			}
		}
		if (VesselCrew.Count > 0)
		{
			Temperature = SpaceExposureTemperature(Temperature, HeatCollectionFactor, HeatDissipationFactor, (float)Mass, deltaTime);
		}
		systemsUpdateTimer += deltaTime;
		if (CurrentCourse != null && AutoActivateCourse == CurrentCourse && CurrentCourse.StartSolarSystemTime > Server.SolarSystemTime && CurrentCourse.StartSolarSystemTime <= Server.SolarSystemTime + 1.0)
		{
			await AutoActivateCourse.ToggleActivated(activate: true);
			AutoActivateCourse = null;
		}
	}

	public override async Task UpdateVesselSystems()
	{
		if (IsMainVessel)
		{
			await MainDistributionManager.UpdateSystems(ConnectionsChanged, ConnectionsChanged);
			ConnectionsChanged = false;
		}
		ShipStatsMessage ssm = new ShipStatsMessage();
		ssm.GUID = Guid;
		ssm.Temperature = Temperature;
		ssm.Health = Health;
		ssm.Armor = Armor;
		ssm.VesselObjects = new VesselObjects();
		ssm.VesselObjects.SubSystems = DistributionManager.GetSubSystemsDetails(changedOnly: true, Guid);
		ssm.VesselObjects.Generators = DistributionManager.GetGeneratorsDetails(changedOnly: true, Guid);
		ssm.VesselObjects.RoomTriggers = DistributionManager.GetRoomsDetails(changedOnly: true, Guid);
		ssm.VesselObjects.ResourceContainers = DistributionManager.GetResourceContainersDetails(changedOnly: true, Guid);
		ssm.VesselObjects.RepairPoints = GetVesselRepairPointsDetails(changedOnly: true);
		ssm.VesselObjects.Doors = DistributionManager.GetDoorsDetails(changedOnly: true, Guid);
		if (SelfDestructTimer != null && prevDestructionSolarSystemTime != SelfDestructTimer.DestructionSolarSystemTime)
		{
			prevDestructionSolarSystemTime = SelfDestructTimer.DestructionSolarSystemTime;
			ssm.SelfDestructTime = SelfDestructTimer?.Time;
		}
		await NetworkController.SendToClientsSubscribedTo(ssm, -1L, this);
		foreach (DynamicObject dobj in DynamicObjects.Values.Where((DynamicObject x) => x.Item != null && x.Item.AttachPointType != AttachPointType.None))
		{
			if (dobj.Item.AttachPointType == AttachPointType.BatteryRechargePoint && dobj.Item is Battery bat)
			{
				await bat.ChangeQuantity(bat.ChargeAmount);
			}
		}
	}

	public override async Task Destroy()
	{
		Server.Instance.PhysicsController.RemoveRigidBody(this);
		Server.Instance.Remove(this);
		DisconectListener();
		await base.Destroy();
	}

	private void DisconectListener()
	{
		EventSystem.RemoveListener<ShipStatsMessage>(ShipStatsMessageListener);
		EventSystem.RemoveListener<ManeuverCourseRequest>(ManeuverCourseRequestListener);
		EventSystem.RemoveListener<DistressCallRequest>(DistressCallRequestListener);
		EventSystem.RemoveListener<VesselRequest>(VesselRequestListener);
		EventSystem.RemoveListener<VesselSecurityRequest>(VesselSecurityRequestListener);
		EventSystem.RemoveListener<RoomPressureMessage>(RoomPressureMessageListener);
		EventSystem.RemoveListener<VesselRequest>(RecycleItemMessageListener);
	}

	~Ship()
	{
		DisconectListener();
	}

	public void DampenRotation(Vector3D stabilizeAxes, double timeDelta, double stabilizationMultiplier = 1.0, double? rotationStabilization = null)
	{
		if (!rotationStabilization.HasValue)
		{
			rotationStabilization = RCS == null && IsPrefabStationVessel ? 1f : RCSRotationStabilization;
		}
		double stabilizationValue = rotationStabilization.Value * stabilizationMultiplier * timeDelta * Server.RcsRotationMultiplier;
		Vector3D oldRotation = Rotation;
		if (Rotation.X > 0.0)
		{
			Rotation.X = MathHelper.Clamp(Rotation.X - stabilizationValue * stabilizeAxes.X, 0.0, Rotation.X);
		}
		else
		{
			Rotation.X = MathHelper.Clamp(Rotation.X + stabilizationValue * stabilizeAxes.X, Rotation.X, 0.0);
		}
		if (Rotation.Y > 0.0)
		{
			Rotation.Y = MathHelper.Clamp(Rotation.Y - stabilizationValue * stabilizeAxes.Y, 0.0, Rotation.Y);
		}
		else
		{
			Rotation.Y = MathHelper.Clamp(Rotation.Y + stabilizationValue * stabilizeAxes.Y, Rotation.Y, 0.0);
		}
		if (Rotation.Z > 0.0)
		{
			Rotation.Z = MathHelper.Clamp(Rotation.Z - stabilizationValue * stabilizeAxes.Z, 0.0, Rotation.Z);
		}
		else
		{
			Rotation.Z = MathHelper.Clamp(Rotation.Z + stabilizationValue * stabilizeAxes.Z, Rotation.Z, 0.0);
		}
		if (!CurrRcsRotationThrust.HasValue)
		{
			CurrRcsRotationThrust = Rotation - oldRotation;
		}
	}

	public void GatherAtmosphere()
	{
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataShip data = new PersistenceObjectDataShip();
		data.GUID = Guid;
		data.Health = Health;
		data.IsInvulnerable = IsInvulnerable;
		data.DockingControlsDisabled = DockingControlsDisabled;
		data.SecurityPanelsLocked = SecurityPanelsLocked;
		data.OrbitData = new OrbitData();
		Orbit.FillOrbitData(ref data.OrbitData);
		data.Forward = Forward.ToArray();
		data.Up = Up.ToArray();
		data.Rotation = Rotation.ToArray();
		data.Registration = VesselData.VesselRegistration;
		data.Name = VesselData.VesselName;
		data.EmblemId = EmblemId;
		data.Tag = VesselData.Tag;
		data.SceneID = SceneID;
		data.timePassedSinceShipCall = timePassedSinceRequest;
		data.IsDistressSignalActive = IsDistressSignalActive;
		data.IsAlwaysVisible = IsAlwaysVisible;
		data.IsPrefabStationVessel = IsPrefabStationVessel;
		data.SelfDestructTimer = SelfDestructTimer?.GetData();
		data.AuthorizedPersonel = AuthorizedPersonel;
		data.StartingSetId = StartingSetId;
		if (DockedToVessel != null)
		{
			foreach (VesselDockingPort port in DockingPorts)
			{
				if (port.DockedVessel == DockedToVessel)
				{
					data.DockedToShipGUID = DockedToVessel.Guid;
					data.DockedPortID = port.ID.InSceneID;
					data.DockedToPortID = port.DockedToID.InSceneID;
					break;
				}
			}
		}
		if (StabilizeToTargetObj != null)
		{
			data.StabilizeToTargetGUID = StabilizeToTargetObj.Guid;
			data.StabilizeToTargetPosition = StabilizeToTargetRelPosition.ToArray();
		}
		if (IsMainVessel && CurrentCourse != null && CurrentCourse.IsInProgress)
		{
			data.CourseInProgress = CurrentCourse.CurrentCourseItem;
		}
		data.DynamicObjects = new List<PersistenceObjectData>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			data.DynamicObjects.Add(dobj.Item != null ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
		}
		if (CargoBay != null)
		{
			data.CargoBay = CargoBay.GetPersistenceData() as PersistenceObjectDataCargo;
		}
		data.ResourceTanks = new List<PersistenceObjectDataCargo>();
		foreach (ResourceContainer rc in DistributionManager.GetResourceContainers())
		{
			data.ResourceTanks.Add(rc.GetPersistenceData() as PersistenceObjectDataCargo);
		}
		data.Generators = new List<PersistenceObjectDataVesselComponent>();
		foreach (Generator gen in DistributionManager.GetGenerators().Cast<Generator>())
		{
			data.Generators.Add(gen.GetPersistenceData() as PersistenceObjectDataVesselComponent);
		}
		data.SubSystems = new List<PersistenceObjectDataVesselComponent>();
		foreach (SubSystem ss in DistributionManager.GetSubSystems().Cast<SubSystem>())
		{
			data.SubSystems.Add(ss.GetPersistenceData() as PersistenceObjectDataVesselComponent);
		}
		data.Rooms = new List<PersistenceObjectDataRoom>();
		foreach (Room room in DistributionManager.GetRooms())
		{
			data.Rooms.Add(room.GetPersistenceData() as PersistenceObjectDataRoom);
		}
		data.Doors = new List<PersistenceObjectDataDoor>();
		foreach (Door door in Doors)
		{
			data.Doors.Add(door.GetPersistenceData() as PersistenceObjectDataDoor);
		}
		if (DockingPorts != null)
		{
			data.DockingPorts = new List<PersistenceObjectDataDockingPort>();
			foreach (VesselDockingPort dp in DockingPorts)
			{
				data.DockingPorts.Add(dp.GetPersistenceData() as PersistenceObjectDataDockingPort);
			}
		}
		data.Executors = new List<PersistenceObjectDataExecutor>();
		foreach (SceneTriggerExecutor exe in SceneTriggerExecutors)
		{
			data.Executors.Add(exe.GetPersistenceData() as PersistenceObjectDataExecutor);
		}
		data.NameTags = new List<PersistenceObjectDataNameTag>();
		foreach (NameTagData nameTag in NameTags)
		{
			data.NameTags.Add(new PersistenceObjectDataNameTag
			{
				InSceneID = nameTag.InSceneID,
				NameTagText = nameTag.NameTagText
			});
		}
		data.RepairPoints = new List<PersistenceObjectDataRepairPoint>();
		foreach (VesselRepairPoint rp in RepairPoints)
		{
			data.RepairPoints.Add(rp.GetPersistenceData() as PersistenceObjectDataRepairPoint);
		}
		return data;
	}

	public async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		PersistenceObjectDataShip data = persistenceData as PersistenceObjectDataShip;
		Guid = data.GUID;
		await CreateShipData(data.Registration, data.Tag, data.SceneID, loadDynamicObjects: false);
		VesselData.VesselName = data.Name;
		EmblemId = data.EmblemId;
		LoadShipRequestPersistance(data.timePassedSinceShipCall);
		DistributionManager = new DistributionManager(this);
		InitializeOrbit(Vector3D.Zero, Vector3D.One, data.Forward.ToVector3D(), data.Up.ToVector3D());
		Server.Instance.PhysicsController.CreateAndAddRigidBody(this);
		Rotation = data.Rotation.ToVector3D();
		await SetHealthAsync(data.Health);
		IsInvulnerable = data.IsInvulnerable;
		DockingControlsDisabled = data.DockingControlsDisabled;
		SecurityPanelsLocked = data.SecurityPanelsLocked;
		StructureSceneData structureSceneData = ObjectCopier.DeepCopy(StaticData.StructuresDataList.Find((StructureSceneData m) => m.ItemID == (short)SceneID));

		foreach (PersistenceObjectDataDynamicObject dobjData in data.DynamicObjects.Cast<PersistenceObjectDataDynamicObject>())
		{
			DynamicObject dobj = await Persistence.CreateDynamicObject(dobjData, this, structureSceneData);
			if (dobj is { Item.AttachPointID: not null })
			{
				VesselComponent comp = MainDistributionManager.GetVesselComponentByPartSlot(dobj.Item.AttachPointID);
				if (comp != null && dobj.Item is MachineryPart part)
				{
					comp.FitPartToSlot(part.AttachPointID, part);
				}
			}
		}
		if (data.CargoBay != null)
		{
			await CargoBay.LoadPersistenceData(data.CargoBay);
		}
		if (data.ResourceTanks != null)
		{
			await Parallel.ForEachAsync(data.ResourceTanks, async (rtd, ct) =>
			{
				await DistributionManager.GetResourceContainer(new VesselObjectID(Guid, rtd.InSceneID))?.LoadPersistenceData(rtd);
			});
		}
		if (data.Generators != null)
		{
			await Parallel.ForEachAsync(data.Generators, async (vc, ct) =>
			{
				await DistributionManager.GetGenerator(new VesselObjectID(Guid, vc.InSceneID))?.LoadPersistenceData(vc);
			});
		}
		if (data.SubSystems != null)
		{
			await Parallel.ForEachAsync(data.SubSystems, async (subSystem, ct) =>
			{
				await DistributionManager.GetSubSystem(new VesselObjectID(Guid, subSystem.InSceneID))?.LoadPersistenceData(subSystem);
			});
		}
		if (data.Rooms != null)
		{
			await Parallel.ForEachAsync(data.Rooms, async (room, ct) =>
			{
				await DistributionManager.GetRoom(new VesselObjectID(Guid, room.InSceneID))?.LoadPersistenceData(room);
			});
		}
		if (data.Doors != null)
		{
			await Parallel.ForEachAsync(data.Doors, async (door, ct) =>
			{
				await Doors.Find((Door x) => x.ID.InSceneID == door.InSceneID)?.LoadPersistenceData(door);
			});
		}
		if (data.DockingPorts != null)
		{
			await Parallel.ForEachAsync(data.DockingPorts, async (dp, ct) =>
			{
				await DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == dp.InSceneID)?.LoadPersistenceData(dp);
			});
		}
		if (data.Executors != null)
		{
			await Parallel.ForEachAsync(data.Executors, async (executor, ct) =>
			{
				await SceneTriggerExecutors.Find(x => x.InSceneID == executor.InSceneID)?.LoadPersistenceData(executor);
			});
		}
		if (data.NameTags != null)
		{
			foreach (PersistenceObjectDataNameTag i in data.NameTags)
			{
				NameTagData nameTag = NameTags.Find((NameTagData x) => x.InSceneID == i.InSceneID);
				if (nameTag != null)
				{
					nameTag.NameTagText = i.NameTagText;
				}
			}
		}
		if (data.RepairPoints is { Count: > 0 })
		{
			await Parallel.ForEachAsync(data.RepairPoints, async (rp, ct) =>
			{
				await RepairPoints.Find((VesselRepairPoint x) => x.ID.InSceneID == rp.InSceneID)?.LoadPersistenceData(rp);
			});
		}
		await MainDistributionManager.UpdateSystems();
		if (data.OrbitData != null)
		{
			Orbit.ParseNetworkData(data.OrbitData, resetOrbit: true);
		}
		Server.Instance.Add(this);
		SetPhysicsParameters();
		if (data.DockedToShipGUID.HasValue)
		{
			Ship dockToShip = Server.Instance.GetVessel(data.DockedToShipGUID.Value) as Ship;

			VesselDockingPort myPort = DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == data.DockedPortID.Value);
			VesselDockingPort dockedToPort = dockToShip.DockingPorts.First((VesselDockingPort m) => m.ID.InSceneID == data.DockedToPortID.Value);

			System.Diagnostics.Debug.Assert(myPort != null);
			System.Diagnostics.Debug.Assert(dockedToPort != null);

			await DockToVessel(myPort, dockedToPort, dockToShip, disableStabilization: false, useCurrentSolarSystemTime: true, buildingStation: true);
		}
		if (data.StabilizeToTargetGUID.HasValue)
		{
			SpaceObjectVessel ab = Server.Instance.GetObject(data.StabilizeToTargetGUID.Value) as SpaceObjectVessel;
			StabilizeToTarget(ab, forceStabilize: true);
			StabilizeToTargetRelPosition = data.StabilizeToTargetPosition.ToVector3D();
			await UpdateStabilization();
		}
		if (data.timePassedSinceShipCall > 0.0)
		{
			LoadShipRequestPersistance(data.timePassedSinceShipCall);
		}
		IsDistressSignalActive = data.IsDistressSignalActive;
		IsAlwaysVisible = data.IsAlwaysVisible;
		IsPrefabStationVessel = data.IsPrefabStationVessel;
		if (data.SelfDestructTimer != null)
		{
			SelfDestructTimer = new SelfDestructTimer(this, data.SelfDestructTimer);
		}
		AuthorizedPersonel = data.AuthorizedPersonel;
		StartingSetId = data.StartingSetId;
		if (data.CourseInProgress != null)
		{
			CurrentCourse = await ManeuverCourse.ParsePersistenceData(data.CourseInProgress, this);
		}
	}

	public async void VesselRequestListener(NetworkData data)
	{
		var request = data as VesselRequest;
		if (request.GUID != Guid)
		{
			return;
		}
		var response = new VesselRequestResponse();
		response.GUID = Guid;
		response.Active = false;
		if (timePassedSinceRequest > 0.0)
		{
			response.Message = RescueShipMessages.ShipEnRoute;
			response.Time = (float)(RespawnTimeForShip - timePassedSinceRequest);
			foreach (Player p3 in MainVessel.VesselCrew)
			{
				await NetworkController.SendAsync(p3.Guid, response);
			}
			{
				foreach (Ship shp3 in MainVessel.AllDockedVessels.Cast<Ship>())
				{
					foreach (Player pl3 in shp3.VesselCrew)
					{
						await NetworkController.SendAsync(pl3.Guid, response);
					}
				}
				return;
			}
		}
		if (Server.Instance.SolarSystem.GetArtificialBodieslsInRange(this, 5000.0).FirstOrDefault((ArtificialBody m) => m is SpaceObjectVessel vessel && GameScenes.Ranges.IsShip(vessel.SceneID)) == null)
		{
			timePassedSinceRequest = 1.0;
			RespawnTimeForShip = request.Time;
			RescueShipSceneID = request.RescueShipSceneID;
			RescueShipTag = request.RescueShipTag;
			response.Message = RescueShipMessages.ShipCalled;
			response.Time = (float)RespawnTimeForShip;
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, SpawnShipCallback);
			foreach (Player p2 in MainVessel.VesselCrew)
			{
				await NetworkController.SendAsync(p2.Guid, response);
			}
			{
				foreach (Ship shp2 in MainVessel.AllDockedVessels.Cast<Ship>())
				{
					foreach (Player pl2 in shp2.VesselCrew)
					{
						await NetworkController.SendAsync(pl2.Guid, response);
					}
				}
				return;
			}
		}
		response.Message = RescueShipMessages.AnotherShipInRange;
		foreach (Player p in MainVessel.VesselCrew)
		{
			await NetworkController.SendAsync(p.Guid, response);
		}
		foreach (Ship shp in MainVessel.AllDockedVessels.Cast<Ship>())
		{
			foreach (Player pl in shp.VesselCrew)
			{
				await NetworkController.SendAsync(pl.Guid, response);
			}
		}
	}

	public void LoadShipRequestPersistance(double timeSince)
	{
		if (timeSince > 0.001)
		{
			timePassedSinceRequest = timeSince;
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, SpawnShipCallback);
		}
	}

	public async void SpawnShipCallback(double dbl)
	{
		timePassedSinceRequest += dbl;
		if (!(timePassedSinceRequest > RespawnTimeForShip))
		{
			return;
		}
		double a = MathHelper.RandomRange(0.0, System.Math.PI * 2.0);
		double b = MathHelper.RandomRange(0.0, System.Math.PI);
		double r = MathHelper.RandomRange(Radius + 200.0, Radius + 350.0);
		Vector3D newPos = r * new Vector3D(System.Math.Cos(a) * System.Math.Sin(b), System.Math.Sin(a) * System.Math.Sin(b), System.Math.Cos(b));
		CurrentSpawnedShip = await SpawnRescueShip(this, newPos, RescueShipSceneID, RescueShipTag);
		Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_1_0_sec, SpawnShipCallback);
		timePassedSinceRequest = 0.0;
		VesselRequestResponse vrr = new VesselRequestResponse();
		vrr.Active = false;
		vrr.GUID = Guid;
		vrr.Message = RescueShipMessages.ShipArrived;
		foreach (Player p in MainVessel.VesselCrew)
		{
			await NetworkController.SendAsync(p.Guid, vrr);
		}
		foreach (Ship shp in MainVessel.AllDockedVessels.Cast<Ship>())
		{
			foreach (Player pl in shp.VesselCrew)
			{
				await NetworkController.SendAsync(pl.Guid, vrr);
			}
		}
	}

	public static async Task<Ship> SpawnRescueShip(SpaceObjectVessel mainShip, Vector3D pos, GameScenes.SceneId sceneID, string tag)
	{
		Ship rescueShip = await CreateNewShip(sceneID, "", -1L, new List<long> { mainShip.Guid }, null, pos, null, MathHelper.RandomRotation(), tag + (tag == "" || tag.EndsWith(";") ? "" : ";") + "_RescueVessel");
		rescueShip.StabilizeToTarget(mainShip, forceStabilize: true);
		return rescueShip;
	}

	public void DistressCallRequestListener(NetworkData data)
	{
		var request = data as DistressCallRequest;
		if (request.GUID == Guid)
		{
			IsDistressSignalActive = request.IsDistressActive;
			MainVessel.UpdateVesselData();
		}
	}

	public async void VesselSecurityRequestListener(NetworkData data)
	{
		var request = data as VesselSecurityRequest;
		if (request.VesselGUID != Guid)
		{
			return;
		}
		Player sender = Server.Instance.GetPlayer(request.Sender);
		if (sender != null)
		{
			// Get player.
			Player pl;
			if (!request.AddPlayerId.IsNullOrEmpty())
			{
				pl = Server.Instance.GetPlayerFromPlayerId(request.AddPlayerId);
			}
			else if (!request.RemovePlayerId.IsNullOrEmpty())
			{
				pl = Server.Instance.GetPlayerFromPlayerId(request.RemovePlayerId);
			}
			else
			{
				return;
			}

			bool sendSecurityResponse = false;

			// Change name.
			if (!request.VesselName.IsNullOrEmpty() && ChangeVesselName(sender, request.VesselName))
			{
				sendSecurityResponse = true;
			}

			// Add player.
			if (!request.AddPlayerId.IsNullOrEmpty() && request.AddPlayerRank.HasValue && AddAuthorizedPerson(sender, pl, request.AddPlayerName, request.AddPlayerRank.Value))
			{
				sendSecurityResponse = true;
			}

			// Removing player.
			if (!request.RemovePlayerId.IsNullOrEmpty() && RemoveAuthorizedPerson(sender, pl))
			{
				sendSecurityResponse = true;
			}

			// Hack.
			if (request.HackPanel.HasValue && request.HackPanel.Value && ClearSecuritySystem(sender))
			{
				sendSecurityResponse = true;
			}

			// Send if it was successful.
			if (sendSecurityResponse)
			{
				await SendSecurityResponse(includeVesselName: true);
			}
		}
	}

	public void RoomPressureMessageListener(NetworkData data)
	{
		var message = data as RoomPressureMessage;
		if (message.ID.VesselGUID != Guid)
		{
			return;
		}
		Room room = Rooms.FirstOrDefault((Room m) => m.ID.Equals(message.ID));
		if (room == null)
		{
			return;
		}
		if (message.TargetPressure.HasValue)
		{
			room.EquilizePressureRoom = null;
			room.TargetPressure = message.TargetPressure.Value;
		}
		else if (message.TargetRoomID != null)
		{
			SpaceObjectVessel vessel = Server.Instance.GetVessel(message.TargetRoomID.VesselGUID);
			if (vessel != null)
			{
				room.TargetPressure = null;
				room.EquilizePressureRoom = vessel.Rooms.FirstOrDefault((Room m) => m.ID.Equals(message.TargetRoomID));
			}
		}
		else
		{
			room.TargetPressure = null;
			room.EquilizePressureRoom = null;
		}
	}

	public async void RecycleItemMessageListener(NetworkData data)
	{
		var message = data as RecycleItemMessage;
		if (message.ID.VesselGUID == Guid && AttachPoints.TryGetValue(message.ID.InSceneID, out var ap))
		{
			Item item = !message.GUID.HasValue ? ap.Item : Server.Instance.GetItem(message.GUID.Value);
			if (item != null && (item.DynamicObj.Parent == this || item.DynamicObj.Parent.Parent == this))
			{
				Player pl = Server.Instance.GetPlayer(message.Sender);
				await RecycleItem(item, message.RecycleMode, pl);
			}
		}
	}

	private async Task RecycleItem(Item item, RecycleMode mode, Player pl)
	{
		Debug.LogFormat("Recycling item {0} for player {1}.", item.TypeName, pl.Name);
		if (item is Outfit outfit)
		{
			foreach (InventorySlot invSlot in outfit.InventorySlots.Values)
			{
				if (invSlot.Item != null)
				{
					await RecycleItem(invSlot.Item, mode, pl);
				}
			}
		}
		else
		{
			foreach (ItemSlot slot in item.Slots.Values)
			{
				if (slot.Item != null)
				{
					await RecycleItem(slot.Item, mode, pl);
				}
			}
		}
		if (item.AttachPointID != null)
		{
			item.SetAttachPoint(null);
		}
		if (mode != 0)
		{
			ItemCompoundType cit = item.CompoundType;
			if (pl != null && pl.Blueprints.Count((ItemCompoundType m) => m.Type == cit.Type && m.SubType == cit.SubType && m.PartType == cit.PartType && m.Tier == cit.Tier) < 1)
			{
				Dictionary<ResourceType, float> craftingResources = Item.GetCraftingResources(item);
				if (craftingResources is { Count: > 0 })
				{
					pl.Blueprints.Add(cit);
					await NetworkController.SendAsync(pl.Guid, new UpdateBlueprintsMessage
					{
						Blueprints = pl.Blueprints
					});
				}
			}
		}
		if (item is Grenade && mode != RecycleMode.ResearchOnly)
		{
			await item.TakeDamage(TypeOfDamage.Impact, MaxHealth, forceTakeDamage: true);
		}
		else
		{
			await item.DestroyItem();
		}
		if (mode == RecycleMode.ResearchOnly)
		{
			return;
		}
		Dictionary<ResourceType, float> recycleResources = Item.GetRecycleResources(item);
		if (recycleResources == null)
		{
			return;
		}
		foreach (KeyValuePair<ResourceType, float> rr in recycleResources)
		{
			float qty = rr.Value;
			foreach (CargoCompartmentData ccd in CargoBay.Compartments)
			{
				qty -= await CargoBay.ChangeQuantityByAsync(ccd.ID, rr.Key, qty);
				if (qty <= float.Epsilon)
				{
					break;
				}
			}
		}
	}

	public async Task ResetSpawnPointsForPlayer(Player pl, bool sendStatsMessage)
	{
		if (SpawnPoints == null || SpawnPoints.Count == 0)
		{
			return;
		}
		ShipStatsMessage retMsg = null;
		foreach (ShipSpawnPoint sp in SpawnPoints)
		{
			if (sp.Player == pl)
			{
				if (retMsg == null && sendStatsMessage)
				{
					retMsg = new ShipStatsMessage();
					retMsg.GUID = Guid;
					retMsg.Temperature = Temperature;
					retMsg.Health = Health;
					retMsg.Armor = Armor;
					retMsg.VesselObjects = new VesselObjects();
					retMsg.VesselObjects.SpawnPoints = new List<SpawnPointStats>();
					retMsg.SelfDestructTime = SelfDestructTimer?.Time;
				}
				sp.Player = null;
				sp.State = SpawnPointState.Unlocked;
				sp.IsPlayerInSpawnPoint = false;
				retMsg.VesselObjects.SpawnPoints.Add(new SpawnPointStats
				{
					InSceneID = sp.SpawnPointID,
					NewState = sp.State,
					PlayerGUID = -1L,
					PlayerName = ""
				});
			}
		}
		if (StartingSetId > 0)
		{
		}
		if (retMsg != null)
		{
			await NetworkController.SendToClientsSubscribedTo(retMsg, -1L, this);
		}
	}

	public override async Task<float> ChangeHealthBy(float value, List<VesselRepairPoint> repairPoints = null, VesselRepairPoint.Priority damagePiority = VesselRepairPoint.Priority.None, bool force = false, VesselDamageType damageType = VesselDamageType.None, double time = 1.0)
	{
		if (value >= 0f)
		{
			LastVesselDamageType = VesselDamageType.None;
		}
		if (!force && value < 0f && (AllVessels.Sum((SpaceObjectVessel n) => n.VesselCrew.Count((Player m) => m.GodMode)) > 0 || IsInvulnerable || (InvulnerableWhenDocked && (DockedToVessel != null || DockedVessels.Count > 0))))
		{
			return 0f;
		}
		if (value < 0f)
		{
			if (!force)
			{
				value += BaseArmor;
				if (value < 0f && VesselBaseSystem != null && AddedArmor > float.Epsilon)
				{
					value += await VesselBaseSystem.ConsumeArmor(0f - value, time);
				}
				if (value >= 0f)
				{
					return 0f;
				}
			}
			LastVesselDamageType = damageType;
		}
		int prevRadarSignatureHealthMultiplierIndex = RadarSignatureHealthMultiplierIndex;
		float prevHealth = _Health;
		_Health = MathHelper.Clamp(Health + value, 0f, MaxHealth);
		if (damageType != 0 && Health > 0f && prevRadarSignatureHealthMultiplierIndex != RadarSignatureHealthMultiplierIndex)
		{
			MainVessel.UpdateVesselData();
		}
		if (value < 0f && RepairPoints.Count > 0)
		{
			float damage = System.Math.Abs(value);
			Func<VesselRepairPoint, double> sortOrder = (VesselRepairPoint m) => (m.Health == m.MaxHealth ? 1 : 0) + MathHelper.RandomNextDouble();
			List<VesselRepairPoint> list = RepairPoints.OrderBy(sortOrder).ToList();
			if (repairPoints != null)
			{
				list.RemoveAll(repairPoints.Contains);
				list.InsertRange(0, repairPoints);
			}
			else
			{
				List<VesselRepairPoint> priority = Enumerable.Where(predicate: damagePiority switch
				{
					VesselRepairPoint.Priority.Internal => (VesselRepairPoint m) => !m.External,
					VesselRepairPoint.Priority.External => (VesselRepairPoint m) => m.External,
					VesselRepairPoint.Priority.Fire => (VesselRepairPoint m) => m.DamageType == RepairPointDamageType.Fire,
					VesselRepairPoint.Priority.Breach => (VesselRepairPoint m) => m.DamageType == RepairPointDamageType.Breach,
					VesselRepairPoint.Priority.System => (VesselRepairPoint m) => m.DamageType == RepairPointDamageType.System,
					_ => (VesselRepairPoint m) => true,
				}, source: RepairPoints).OrderBy(sortOrder).ToList();
				list.RemoveAll(priority.Contains);
				list.InsertRange(0, priority);
				if (MathHelper.RandomNextDouble() < damage * Server.ActivateRepairPointChanceMultiplier / MaxHealth)
				{
					VesselRepairPoint rrp = list.Find((VesselRepairPoint m) => m.Health == m.MaxHealth);
					if (rrp != null)
					{
						list.Remove(rrp);
						list.Insert(0, rrp);
					}
				}
			}
			foreach (VesselRepairPoint rp in list.Where((VesselRepairPoint m) => m.Health > 0f))
			{
				if (rp.AffectedSystem is { Defective: true} && MathHelper.RandomNextDouble() < damage * Server.DamageUpgradePartChanceMultiplier / MaxHealth)
				{
					MachineryPart mp = (from m in rp.AffectedSystem.MachineryParts.Values
						where m != null
						orderby MathHelper.RandomNextDouble()
						select m).FirstOrDefault();

					if (mp != null)
					{
						await mp.TakeDamage(new Dictionary<TypeOfDamage, float> {
						{
							TypeOfDamage.Impact,
							mp.MaxHealth
						} }, forceTakeDamage: true);
					}
				}
				if (rp.Health >= damage)
				{
					float s = (from m in RepairPoints.ToArray()
						where m != rp
						select m).Sum((VesselRepairPoint k) => k.Health);
					await rp.SetHealthAsync(Health - s);
					break;
				}
				damage -= rp.Health;
				await rp.SetHealthAsync(0f);
			}
		}
		return Health - prevHealth;
	}
}
