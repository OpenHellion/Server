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

public class Ship : SpaceObjectVessel, IPersistantObject
{
	private bool isRcsOnline = false;

	public Vector3D RcsThrustVelocityDifference = Vector3D.Zero;

	public Vector3D RcsThrustDirection = Vector3D.Zero;

	public Vector3D ExtraRcsThrustVelocityDifference = Vector3D.Zero;

	private double rcsThrustResetTimer = 0.0;

	private double rcsThrustResetTreshold = 0.2;

	public Vector3D EngineThrustVelocityDifference = Vector3D.Zero;

	public Vector3D ExtraEngineThrust = Vector3D.Zero;

	private double engineThrustPercentage = 0.0;

	private double currentEngineThrustPerc = 0.0;

	private bool isRotationOnline = false;

	public Vector3D RotationThrustVelocityDifference = Vector3D.Zero;

	public Vector3D RotationThrustDirection = Vector3D.Zero;

	public Vector3D ExtraRotationThrustVelocityDifference = Vector3D.Zero;

	private double rotationThrustResetTimer = 0.0;

	private double rotationThrustResetTreshold = 0.5;

	private double autoStabilizeTimer = 0.0;

	private double autoStabilizeTreshold = 60.0;

	private Vector3D stabilize = Vector3D.Zero;

	private double stabilizeResetTimer = 0.0;

	private double stabilizeResetTreshold = 1.0;

	private double systemsUpdateTimer = 0.0;

	public int ColliderIndex = 1;

	public bool CollectAtmosphere;

	public bool sendResourceUpdate;

	private bool rcsThrustChanged = false;

	private Vector3D? _currRcsMoveThrust = null;

	private Vector3D? _currRcsRotationThrust = null;

	private ManeuverCourse AutoActivateCourse;

	private float[] RadarSignatureHealthMultipliers;

	public static bool DoSpecialPrint = false;

	private string RescueShipTag = "";

	private GameScenes.SceneID RescueShipSceneID = GameScenes.SceneID.AltCorp_Shuttle_CECA;

	private double RespawnTimeForShip = 60.0;

	public double timePassedSinceRequest = 0.0;

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

	public int RadarSignatureHealthMultiplierIndex => (int)System.Math.Floor(base.Health / MaxHealth * 9f);

	public float TimeToLive => (float)((double)base.Health / ((double)base.ExposureDamage * Server.VesselDecayRateMultiplier));

	public Ship(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid, initializeOrbit, position, velocity, forward, up)
	{
		Radius = 100.0;
		EventSystem.AddListener(typeof(ShipStatsMessage), ShipStatsMessageListener);
		EventSystem.AddListener(typeof(ManeuverCourseRequest), ManeuverCourseRequestListener);
		EventSystem.AddListener(typeof(DistressCallRequest), DistressCallRequestListener);
		EventSystem.AddListener(typeof(VesselRequest), VesselRequestListener);
		EventSystem.AddListener(typeof(VesselSecurityRequest), VesselSecurityRequestListener);
		EventSystem.AddListener(typeof(RoomPressureMessage), RoomPressureMessageListener);
		EventSystem.AddListener(typeof(RecycleItemMessage), RecycleItemMessageListener);
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

	private void ManeuverCourseRequestListener(NetworkData data)
	{
		ManeuverCourseRequest req = data as ManeuverCourseRequest;
		if (req.ShipGUID != GUID)
		{
			return;
		}
		if (req.CourseItems != null && req.CourseItems.Count > 0)
		{
			DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
			SetMainVessel();
			base.CurrentCourse = ManeuverCourse.ParseNetworkData(req, this);
			base.CurrentCourse.ReadNextManeuverCourse();
			if (!req.Activate.HasValue)
			{
				AutoActivateCourse = base.CurrentCourse;
			}
		}
		if (req.Activate.HasValue && base.CurrentCourse != null && base.CurrentCourse.CourseGUID == req.CourseGUID)
		{
			base.CurrentCourse.ToggleActivated(req.Activate.Value);
		}
		base.CurrentCourse.SendCourseStartResponse();
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
		if (!base.EngineOnLine)
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
			currentEngineThrustPerc += timeDelta * (double)Engine.AccelerationBuildup * (double)thrustPercSign;
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
		return Forward * currentEngineThrustPerc * ((currentEngineThrustPerc > 0.0) ? base.EngineAcceleration : base.EngineReverseAcceleration);
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
		RcsThrustVelocityDifference = ExtraRcsThrustVelocityDifference + RcsThrustDirection * base.RCSAcceleration * timeDelta * Server.RCS_THRUST_MULTIPLIER;
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
		RotationThrustVelocityDifference = ExtraRotationThrustVelocityDifference + RotationThrustDirection * base.RCSRotationAcceleration * timeDelta * Server.RCS_ROTATION_MULTIPLIER;
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
			if (base.IsMainVessel)
			{
				DampenRotation(stabilize, timeDelta, RCS.MaxOperationRate);
			}
			else
			{
				(base.MainVessel as Ship).DampenRotation(stabilize, timeDelta, RCS.MaxOperationRate, base.RCSRotationStabilization);
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

	public bool CalculateAutoStabilizeRotation(double timeDelta)
	{
		if (Rotation.IsNotEpsilonZero() && !AutoStabilizationDisabled && !base.AllVessels.Any((SpaceObjectVessel m) => m.VesselCrew.FirstOrDefault((Player n) => n.IsPilotingVessel) != null))
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
					rcsVessel.RCS.GoOnLine();
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
		if (base.MainVessel is Ship)
		{
			(base.MainVessel as Ship).autoStabilizeTimer = 0.0;
		}
	}

	public void CheckThrustStatsMessage()
	{
		if (rcsThrustChanged)
		{
			ShipStatsMessage ssm = new ShipStatsMessage();
			ssm.GUID = GUID;
			ssm.ThrustStats = new RcsThrustStats
			{
				MoveTrust = (CurrRcsMoveThrust.HasValue ? CurrRcsMoveThrust.Value.ToFloatArray() : null),
				RotationTrust = (CurrRcsRotationThrust.HasValue ? CurrRcsRotationThrust.Value.ToFloatArray() : null)
			};
			rcsThrustChanged = false;
			NetworkController.Instance.SendToClientsSubscribedTo(ssm, -1L, this);
		}
	}

	public void ShipStatsMessageListener(NetworkData data)
	{
		ShipStatsMessage sm = data as ShipStatsMessage;
		if (sm.GUID != GUID)
		{
			return;
		}
		bool sendShipStatsMsg = false;
		ShipStatsMessage retMsg = new ShipStatsMessage();
		retMsg.GUID = GUID;
		retMsg.Temperature = Temperature;
		retMsg.Health = base.Health;
		retMsg.Armor = base.Armor;
		retMsg.VesselObjects = new VesselObjects();
		Player pl = Server.Instance.GetPlayer(data.Sender);
		bool requestEngine = base.EngineOnLine;
		bool requestRCS = sm.Thrust != null || sm.Rotation != null || sm.AutoStabilize != null || (sm.TargetStabilizationGUID.HasValue && sm.Thrust == null);
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
				RCS.GoOnLine();
				updateDM = true;
			}
			if (updateDM)
			{
				base.MainDistributionManager.UpdateSystems(connectionsChanged: false, compoundRoomsChanged: false);
			}
			canUseEngine = Engine != null && Engine.Status == SystemStatus.OnLine;
			canUseRCS = RCS != null && RCS.Status == SystemStatus.OnLine;
		}
		if (Engine != null && sm.EngineThrustPercentage.HasValue)
		{
			engineThrustPercentage = sm.EngineThrustPercentage.Value;
			retMsg.EngineThrustPercentage = (float)engineThrustPercentage;
			Engine.RequiredThrust = (float)System.Math.Abs(engineThrustPercentage);
			Engine.ReverseThrust = engineThrustPercentage < 0.0;
			sendShipStatsMsg = true;
		}
		if (RCS != null && canUseRCS)
		{
			float opRateThr = 0f;
			float opRateRot = 0f;
			if (sm.Thrust != null)
			{
				Vector3D thr = sm.Thrust.ToVector3D();
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
			if (sm.Rotation != null && (base.CurrentCourse == null || !base.CurrentCourse.IsInProgress))
			{
				Vector3D rot = sm.Rotation.ToVector3D();
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
			if (sm.AutoStabilize != null)
			{
				stabilize = sm.AutoStabilize.ToVector3D();
				stabilizeResetTimer = 0.0;
				RCS.OperationRate = RCS.MaxOperationRate;
			}
			if (RCS.OperationRate == 0f)
			{
				RCS.OperationRate = System.Math.Max(opRateThr, opRateRot);
			}
			if (sm.TargetStabilizationGUID.HasValue && sm.Thrust == null)
			{
				if (Server.Instance.GetObject(sm.TargetStabilizationGUID.Value) is SpaceObjectVessel target && StabilizeToTarget(target))
				{
					retMsg.TargetStabilizationGUID = target.GUID;
				}
				else
				{
					retMsg.TargetStabilizationGUID = -1L;
				}
				sendShipStatsMsg = true;
			}
		}
		if (sm.VesselObjects == null)
		{
			return;
		}
		bool dmUpdate = false;
		bool dmUpdateConn = false;
		bool dmUpdateCav = false;
		if (sm.VesselObjects.RoomTriggers != null && sm.VesselObjects.RoomTriggers.Count > 0)
		{
			dmUpdate = true;
			foreach (RoomDetails trigData in sm.VesselObjects.RoomTriggers)
			{
				Room room = base.MainDistributionManager.GetRoom(new VesselObjectID(GUID, trigData.InSceneID));
				if (room != null)
				{
					room.UseGravity = trigData.UseGravity;
					room.AirFiltering = trigData.AirFiltering;
				}
			}
		}
		if (sm.VesselObjects.Generators != null && sm.VesselObjects.Generators.Count > 0)
		{
			dmUpdate = true;
			foreach (GeneratorDetails gd in sm.VesselObjects.Generators)
			{
				Generator gen = base.MainDistributionManager.GetGenerator(new VesselObjectID(GUID, gd.InSceneID));
				gen.SetDetails(gd);
			}
		}
		if (sm.VesselObjects.SubSystems != null && sm.VesselObjects.SubSystems.Count > 0)
		{
			dmUpdate = true;
			foreach (SubSystemDetails ssd in sm.VesselObjects.SubSystems)
			{
				SubSystem ss = base.MainDistributionManager.GetSubSystem(new VesselObjectID(GUID, ssd.InSceneID));
				ss.SetDetails(ssd);
			}
		}
		if (sm.VesselObjects.Doors != null)
		{
			List<DoorDetails> doorsChanged = new List<DoorDetails>();
			foreach (DoorDetails dd2 in sm.VesselObjects.Doors)
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
						VesselObjectID id = new VesselObjectID(GUID, dd2.InSceneID);
						doorDetails2.PressureEquilizationTime = base.MainDistributionManager.PressureEquilizationTime(id, out doorDetails2.AirFlowDirection, out doorDetails2.AirSpeed);
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
		if (sm.VesselObjects.SceneTriggerExecuters != null)
		{
			List<SceneTriggerExecuterDetails> sceneTriggerExecutersChanged = new List<SceneTriggerExecuterDetails>();
			foreach (SceneTriggerExecuterDetails stDetails2 in sm.VesselObjects.SceneTriggerExecuters)
			{
				SceneTriggerExecuter ste = SceneTriggerExecuters.Find((SceneTriggerExecuter m) => m.InSceneID == stDetails2.InSceneID);
				if (ste != null && stDetails2 != null)
				{
					sceneTriggerExecutersChanged.Add(ste.ChangeState(sm.Sender, stDetails2));
				}
			}
			if (sceneTriggerExecutersChanged.Count > 0)
			{
				retMsg.VesselObjects.SceneTriggerExecuters = sceneTriggerExecutersChanged;
				sendShipStatsMsg = true;
			}
		}
		if (sm.VesselObjects.AttachPoints != null)
		{
			foreach (AttachPointDetails apd in sm.VesselObjects.AttachPoints)
			{
				if (apd.AuxDetails != null && apd.AuxDetails is MachineryPartSlotAuxDetails)
				{
					VesselObjectID slotKey = new VesselObjectID(GUID, apd.InSceneID);
					base.MainDistributionManager.GetVesselComponentByPartSlot(slotKey)?.SetMachineryPartSlotActive(slotKey, (apd.AuxDetails as MachineryPartSlotAuxDetails).IsActive);
				}
			}
		}
		if (sm.VesselObjects.DockingPorts != null)
		{
			List<SceneDockingPortDetails> dockingPortsChanged = new List<SceneDockingPortDetails>();
			foreach (SceneDockingPortDetails stDetails in sm.VesselObjects.DockingPorts)
			{
				VesselDockingPort port = DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == stDetails.ID.InSceneID);
				if (port == null || stDetails.DockedToID == null || !Server.Instance.DoesObjectExist(stDetails.DockedToID.VesselGUID))
				{
					continue;
				}
				if (stDetails.DockingStatus)
				{
					Ship dockToShip = Server.Instance.GetVessel(stDetails.DockedToID.VesselGUID) as Ship;
					VesselDockingPort parentPort = dockToShip.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == stDetails.DockedToID.InSceneID);
					if (!DockToVessel(port, parentPort, dockToShip))
					{
						continue;
					}
					stDetails.CollidersCenterOffset = DockedToMainVessel.VesselData.CollidersCenterOffset;
					stDetails.RelativePositionUpdate = new Dictionary<long, float[]>();
					stDetails.RelativeRotationUpdate = new Dictionary<long, float[]>();
					foreach (Ship s in DockedToMainVessel.AllDockedVessels)
					{
						stDetails.RelativePositionUpdate.Add(s.GUID, s.RelativePositionFromParent.ToFloatArray());
						stDetails.RelativeRotationUpdate.Add(s.GUID, s.RelativeRotationFromParent.ToFloatArray());
					}
					stDetails.ExecutersMerge = port.GetMergedExecuters(parentPort);
					stDetails.PairedDoors = GetPairedDoors(port);
					stDetails.RelativePosition = RelativePositionFromParent.ToFloatArray();
					stDetails.RelativeRotation = RelativeRotationFromParent.ToFloatArray();
					dockingPortsChanged.Add(stDetails);
				}
				else if (port.DockedToID != null)
				{
					Ship dockedToShip = port.DockedVessel as Ship;
					VesselDockingPort dockedToPort = dockedToShip.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == port.DockedToID.InSceneID);
					SceneDockingPortDetails det = stDetails;
					if (!UndockFromVessel(port, dockedToShip, dockedToPort, ref det))
					{
						continue;
					}
					stDetails.ExecutersMerge = new List<ExecuterMergeDetails>();
					foreach (SceneTriggerExecuter exec in port.MergeExecuters.Keys)
					{
						stDetails.ExecutersMerge.Add(new ExecuterMergeDetails
						{
							ParentTriggerID = new VesselObjectID(GUID, exec.InSceneID)
						});
					}
					stDetails.RelativePosition = RelativeRotationFromParent.ToFloatArray();
					stDetails.RelativeRotation = RelativeRotationFromParent.ToFloatArray();
					dockingPortsChanged.Add(stDetails);
				}
				else
				{
					Dbg.Warning("Tried to undock non-docked docking port", port.ID);
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
		if (sm.VesselObjects.SpawnPoints != null)
		{
			List<SpawnPointStats> spawnPointsChanged = new List<SpawnPointStats>();
			foreach (SpawnPointStats stats in sm.VesselObjects.SpawnPoints)
			{
				ShipSpawnPoint sp = SpawnPoints.Find((ShipSpawnPoint m) => m.SpawnPointID == stats.InSceneID);
				if (sp != null)
				{
					SpawnPointStats changed = sp.SetStats(stats, pl);
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
		if (sm.VesselObjects.EmblemId != null)
		{
			EmblemId = sm.VesselObjects.EmblemId;
			retMsg.VesselObjects.EmblemId = EmblemId;
			sendShipStatsMsg = true;
		}
		if (dmUpdate)
		{
			base.MainDistributionManager.UpdateSystems(dmUpdateConn, dmUpdateCav);
			retMsg.VesselObjects.SubSystems = base.MainDistributionManager.GetSubSystemsDetails(changedOnly: true, GUID);
			sendShipStatsMsg |= retMsg.VesselObjects.SubSystems.Count > 0;
			retMsg.VesselObjects.Generators = base.MainDistributionManager.GetGeneratorsDetails(changedOnly: true, GUID);
			sendShipStatsMsg |= retMsg.VesselObjects.Generators.Count > 0;
			retMsg.VesselObjects.RoomTriggers = base.MainDistributionManager.GetRoomsDetails(changedOnly: true, GUID);
			sendShipStatsMsg |= retMsg.VesselObjects.RoomTriggers.Count > 0;
			retMsg.VesselObjects.ResourceContainers = base.MainDistributionManager.GetResourceContainersDetails(changedOnly: true, GUID);
			sendShipStatsMsg |= retMsg.VesselObjects.ResourceContainers.Count > 0;
		}
		if (sm.GatherAtmosphere.HasValue)
		{
			CollectAtmosphere = sm.GatherAtmosphere.Value;
		}
		if (sm.SelfDestructTime.HasValue)
		{
			if (sm.SelfDestructTime.Value >= 0f)
			{
				SelfDestructTimer = new SelfDestructTimer(this, sm.SelfDestructTime.Value);
			}
			else
			{
				SelfDestructTimer = null;
			}
			sendShipStatsMsg = true;
		}
		if (sendShipStatsMsg)
		{
			NetworkController.Instance.SendToClientsSubscribedTo(retMsg, -1L, this);
		}
	}

	public void SendCollision(double vel, double impulse, double time, long otherShipGUID)
	{
		if (time <= double.Epsilon)
		{
			time = 1.0;
		}
		float dmg = (float)(impulse / time / 10000.0 * (IsDebrisFragment ? Server.DebrisVesselCollisionDamageMultiplier : Server.VesselCollisionDamageMultiplier));
		if ((double)dmg < 0.1)
		{
			return;
		}
		SpaceObjectVessel otherVessel = Server.Instance.GetVessel(otherShipGUID);
		List<SpaceObjectVessel> list = new List<SpaceObjectVessel>();
		list.Add(this);
		list.AddRange(AllDockedVessels);
		SpaceObjectVessel vessel = list.OrderBy((SpaceObjectVessel x) => MathHelper.RandomNextDouble()).First();
		vessel.ChangeHealthBy(0f - dmg, null, VesselRepairPoint.Priority.External, force: false, (otherVessel != null && otherVessel.IsDebrisFragment) ? VesselDamageType.LargeDebrisHit : VesselDamageType.Collision);
		if (otherVessel != null)
		{
			list = new List<SpaceObjectVessel>();
			list.Add(otherVessel);
			list.AddRange(otherVessel.AllDockedVessels);
			vessel = list.OrderBy((SpaceObjectVessel x) => MathHelper.RandomNextDouble()).First();
			vessel.ChangeHealthBy(0f - dmg, null, VesselRepairPoint.Priority.External, force: false, IsDebrisFragment ? VesselDamageType.LargeDebrisHit : VesselDamageType.Collision);
		}
		if ((Server.Instance.RunTime - lastShipCollisionMessageTime).TotalSeconds > 0.1)
		{
			lastShipCollisionMessageTime = Server.Instance.RunTime;
			ShipCollisionMessage scm = new ShipCollisionMessage
			{
				CollisionVelocity = (float)vel,
				ShipOne = GUID,
				ShipTwo = otherShipGUID
			};
			NetworkController.Instance.SendToClientsSubscribedTo(scm, -1L, this, (otherShipGUID != -1) ? Server.Instance.GetVessel(otherShipGUID) : null);
		}
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		if (base.IsDocked && DockedToMainVessel is Ship)
		{
			return (DockedToMainVessel as Ship).GetSpawnResponseData(pl);
		}
		bool isDummy = (pl.Position - Position).SqrMagnitude > 100000000.0;
		return new SpawnShipResponseData
		{
			GUID = GUID,
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
					GUID = sh.GUID,
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
		InitializeShipAuxData auxData = new InitializeShipAuxData();
		auxData.VesselObjects = GetVesselObjects();
		msg.AuxData = auxData;
		return msg;
	}

	private void FillShipData(GameScenes.SceneID sceneID, List<StructureSceneData> structureSceneDataList, bool loadDynamicObjects = true, float? health = null)
	{
		StructureSceneData data = ObjectCopier.DeepCopy(structureSceneDataList.Find((StructureSceneData m) => m.ItemID == (short)sceneID));
		if (data == null)
		{
			return;
		}
		RadarSignatureHealthMultipliers = ObjectCopier.Copy(data.RadarSignatureHealthMultipliers);
		RadarSignature = data.RadarSignature;
		base.HasSecuritySystem = data.HasSecuritySystem;
		MaxHealth = data.MaxHealth;
		BaseArmor = data.BaseArmor;
		InvulnerableWhenDocked = data.InvulnerableWhenDocked;
		if (health.HasValue)
		{
			base.Health = health.Value;
		}
		else
		{
			base.Health = data.Health;
			if (data.SpawnSettings != null)
			{
				SceneSpawnSettings[] spawnSettings = data.SpawnSettings;
				foreach (SceneSpawnSettings sss in spawnSettings)
				{
					if (CheckTag(sss.Tag, sss.Case))
					{
						base.Health = MaxHealth * MathHelper.RandomRange(sss.MinHealth, sss.MaxHealth);
						break;
					}
				}
			}
		}
		if (data.AdditionalTags != null && data.AdditionalTags.Length != 0)
		{
			TagChance tc = data.AdditionalTags[MathHelper.RandomRange(0, data.AdditionalTags.Length)];
			if (MathHelper.RandomNextDouble() < (double)tc.Chance)
			{
				if (VesselData.Tag == null)
				{
					VesselData.Tag = "";
				}
				VesselData vesselData = VesselData;
				vesselData.Tag = vesselData.Tag + ((VesselData.Tag == "") ? "" : ";") + tc.Tag;
			}
		}
		if (data.Doors != null && data.Doors.Count > 0)
		{
			foreach (DoorData door in data.Doors)
			{
				Doors.Add(new Door
				{
					ID = new VesselObjectID(GUID, door.InSceneID),
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
		if (data.NameTags != null && data.NameTags.Count > 0)
		{
			foreach (NameTagData ntd in data.NameTags)
			{
				NameTags.Add(ntd);
			}
		}
		if (data.Rooms != null && data.Rooms.Count > 0)
		{
			foreach (RoomData roomData in data.Rooms)
			{
				Room room = new Room
				{
					ID = new VesselObjectID(GUID, roomData.InSceneID),
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
		if (data.SceneTriggerExecuters != null && data.SceneTriggerExecuters.Count > 0)
		{
			foreach (SceneTriggerExecuterData std2 in data.SceneTriggerExecuters)
			{
				if (std2.TagAction != 0)
				{
					bool hasTag2 = CheckTag(std2.Tags);
					if ((std2.TagAction == TagAction.EnableIfTagIs && !hasTag2) || (std2.TagAction == TagAction.DisableIfTagIs && hasTag2))
					{
						continue;
					}
				}
				SceneTriggerExecuter ex2 = new SceneTriggerExecuter
				{
					InSceneID = std2.InSceneID,
					ParentShip = this
				};
				ex2.SetStates(std2.States, std2.DefaultStateID);
				if (std2.ProximityTriggers != null && std2.ProximityTriggers.Count > 0)
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
				SceneTriggerExecuters.Add(ex2);
			}
		}
		if (data.DockingPorts != null && data.DockingPorts.Count > 0)
		{
			foreach (SceneDockingPortData std in data.DockingPorts)
			{
				VesselObjectID id = new VesselObjectID(GUID, std.InSceneID);
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
					MergeExecuters = new Dictionary<SceneTriggerExecuter, Vector3D>(),
					MergeExecutersDistance = std.MergeExecuterDistance
				};
				foreach (SceneDockingPortExecuterMerge mer in std.MergeExecuters)
				{
					SceneTriggerExecuter ex = SceneTriggerExecuters.Find((SceneTriggerExecuter m) => m.InSceneID == mer.InSceneID);
					newPort.MergeExecuters.Add(ex, mer.Position.ToVector3D());
				}
				DockingPorts.Add(newPort);
			}
		}
		if (data.SpawnPoints != null && data.SpawnPoints.Count > 0)
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
				SceneTriggerExecuter executer = ((spd.ExecuterID > 0) ? SceneTriggerExecuters.Find((SceneTriggerExecuter m) => m.InSceneID == spd.ExecuterID) : null);
				ShipSpawnPoint newSpawnPoint = new ShipSpawnPoint
				{
					SpawnPointID = spd.InSceneID,
					Type = spd.Type,
					Executer = executer,
					ExecuterStateID = spd.ExecuterStateID,
					Ship = this,
					State = SpawnPointState.Unlocked,
					Player = null
				};
				if (newSpawnPoint.Executer != null)
				{
					executer.SpawnPoint = newSpawnPoint;
					newSpawnPoint.ExecuterOccupiedStateIDs = new List<int>(spd.ExecuterOccupiedStateIDs);
				}
				SpawnPoints.Add(newSpawnPoint);
			}
		}
		if (data.AttachPoints != null && data.AttachPoints.Count > 0)
		{
			foreach (BaseAttachPointData apd2 in data.AttachPoints)
			{
				AttachPointsTypes[new VesselObjectID(GUID, apd2.InSceneID)] = apd2.AttachPointType;
				AttachPoints.Add(apd2.InSceneID, new VesselAttachPoint
				{
					Vessel = this,
					InSceneID = apd2.InSceneID,
					Type = apd2.AttachPointType,
					ItemTypes = ((apd2.ItemTypes != null) ? new List<ItemType>(apd2.ItemTypes) : new List<ItemType>()),
					GenericSubTypes = ((apd2.GenericSubTypes != null) ? new List<GenericItemSubType>(apd2.GenericSubTypes) : new List<GenericItemSubType>()),
					MachineryPartTypes = ((apd2.MachineryPartTypes != null) ? new List<MachineryPartType>(apd2.MachineryPartTypes) : new List<MachineryPartType>()),
					Item = null
				});
			}
		}
		if (data.SubSystems != null && data.SubSystems.Count > 0)
		{
			foreach (SubSystemData ssd in data.SubSystems)
			{
				Systems.Add(createSubSystem(ssd));
			}
		}
		if (data.Generators != null && data.Generators.Count > 0)
		{
			foreach (GeneratorData gd in data.Generators)
			{
				Systems.Add(createGenerator(gd));
			}
		}
		if (data.RepairPoints != null && data.RepairPoints.Count > 0)
		{
			float maxHealth2 = (float)System.Math.Ceiling(MaxHealth / (float)data.RepairPoints.Count);
			foreach (VesselRepairPointData rpd in data.RepairPoints)
			{
				RepairPoints.Add(new VesselRepairPoint(this, rpd, maxHealth2));
			}
			float h = base.Health;
			foreach (VesselRepairPoint rp in from m in RepairPoints.ToArray()
				orderby MathHelper.RandomNextInt()
				select m)
			{
				if (h > rp.MaxHealth)
				{
					rp.Health = rp.MaxHealth;
					h -= rp.MaxHealth;
					continue;
				}
				rp.Health = h;
				break;
			}
		}
		if (data.SpawnObjectChanceData != null && data.SpawnObjectChanceData.Count > 0)
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
		if (!loadDynamicObjects || data.DynamicObjects == null || data.DynamicObjects.Count <= 0)
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
					if (!doss.Tag.IsNullOrEmpty() && CheckTag(doss.Tag, doss.Case) && (doss.SpawnChance < 0f || (double)doss.SpawnChance > rand))
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
				DynamicObject dobj = new DynamicObject(dosd, this, -1L);
				dobj.RespawnTime = respawnTime;
				dobj.SpawnMaxHealth = maxHealth;
				dobj.SpawnMinHealth = minHealth;
				dobj.SpawnWearMultiplier = wearMultiplier;
				if (dobj.Item != null && dobj.Item != null && maxHealth >= 0f && minHealth >= 0f)
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
				if (dobj.Item is MachineryPart)
				{
					(dobj.Item as MachineryPart).WearMultiplier = wearMultiplier;
				}
				dobj.APDetails = apd;
			}
		}
	}

	private void CreateShipData(string shipRegistration, string shipTag, GameScenes.SceneID shipItemID, bool loadDynamicObjects, float? health = null)
	{
		VesselData = new VesselData
		{
			Id = GUID,
			VesselRegistration = shipRegistration,
			VesselName = "",
			Tag = shipTag,
			SceneID = shipItemID,
			IsDebrisFragment = IsDebrisFragment,
			CreationSolarSystemTime = Server.SolarSystemTime
		};
		FillShipData(VesselData.SceneID, StaticData.StructuresDataList, loadDynamicObjects, health);
		VesselData.RadarSignature = RadarSignature;
		ReadBoundsAndMass(VesselData.SceneID, Vector3D.Zero);
		RecalculateCenter();
	}

	private void ReadBoundsAndMass(GameScenes.SceneID sceneID, Vector3D connectionOffset)
	{
		StructureSceneData sceneData = StaticData.StructuresDataList.Find((StructureSceneData m) => m.ItemID == (short)sceneID);
		if (sceneData == null)
		{
			return;
		}
		Mass += ((sceneData.Mass > 0f) ? sceneData.Mass : 1f);
		HeatCollectionFactor = sceneData.HeatCollectionFactor;
		HeatDissipationFactor = sceneData.HeatDissipationFactor;
		if (sceneData.Colliders == null)
		{
			return;
		}
		if (sceneData.Colliders.PrimitiveCollidersData != null && sceneData.Colliders.PrimitiveCollidersData.Count > 0)
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

	public static Ship CreateNewShip(GameScenes.SceneID sceneID, string registration = "", long shipID = -1L, List<long> nearArtificialBodyGUIDs = null, List<long> celestialBodyGUIDs = null, Vector3D? positionOffset = null, Vector3D? velocityAtPosition = null, QuaternionD? localRotation = null, string vesselTag = "", bool checkPosition = true, double distanceFromSurfacePercMin = 0.03, double distanceFromSurfacePercMax = 0.3, SpawnRuleOrbit spawnRuleOrbit = null, double celestialBodyDeathDistanceMultiplier = 1.5, double artificialBodyDistanceCheck = 100.0, float? health = null, bool isDebrisFragment = false)
	{
		Vector3D shipPos = Vector3D.Zero;
		Vector3D shipVel = Vector3D.Zero;
		Vector3D shipForward = Vector3D.Forward;
		Vector3D shipUp = Vector3D.Up;
		OrbitParameters orbit = null;
		Ship newShip = new Ship((shipID < 0) ? GUIDFactory.NextVesselGUID() : shipID, initializeOrbit: false, shipPos, shipVel, shipForward, shipUp);
		newShip.IsDebrisFragment = isDebrisFragment;
		newShip.CreateShipData(registration, vesselTag, sceneID, loadDynamicObjects: true, health);
		newShip.DistributionManager = new DistributionManager(newShip);
		Server.Instance.PhysicsController.CreateAndAddRigidBody(newShip);
		Server.Instance.SolarSystem.GetSpawnPosition(SpaceObjectType.Ship, newShip.Radius, checkPosition, out shipPos, out shipVel, out shipForward, out shipUp, nearArtificialBodyGUIDs, celestialBodyGUIDs, positionOffset, velocityAtPosition, localRotation, distanceFromSurfacePercMin, distanceFromSurfacePercMax, spawnRuleOrbit, celestialBodyDeathDistanceMultiplier, artificialBodyDistanceCheck, out orbit);
		newShip.InitializeOrbit(shipPos, shipVel, shipForward, shipUp, orbit);
		if (registration.IsNullOrEmpty())
		{
			newShip.VesselData.VesselRegistration = Server.NameGenerator.GenerateObjectRegistration(SpaceObjectType.Ship, newShip.Orbit.Parent.CelestialBody, sceneID);
		}
		foreach (DynamicObject dobj in newShip.DynamicObjects.Values)
		{
			if (dobj.Item != null && dobj.Item.AttachPointID != null)
			{
				VesselComponent comp = newShip.MainDistributionManager.GetVesselComponentByPartSlot(dobj.Item.AttachPointID);
				if (comp != null && dobj.Item is MachineryPart)
				{
					comp.FitPartToSlot(dobj.Item.AttachPointID, (MachineryPart)dobj.Item);
				}
			}
		}
		newShip.MainDistributionManager.UpdateSystems();
		Server.Instance.Add(newShip);
		newShip.SetPhysicsParameters();
		return newShip;
	}

	public override void AddPlayerToCrew(Player pl)
	{
		if (!VesselCrew.Contains(pl))
		{
			VesselCrew.Add(pl);
			RemovePlayerFromExecuters(pl);
		}
	}

	public override void RemovePlayerFromCrew(Player pl, bool checkDetails = false)
	{
		VesselCrew.Remove(pl);
		if (checkDetails)
		{
			RemovePlayerFromExecuters(pl);
		}
	}

	public void RemovePlayerFromExecuters(Player pl)
	{
		List<SceneTriggerExecuterDetails> executers = new List<SceneTriggerExecuterDetails>();
		foreach (SceneTriggerExecuter ex in SceneTriggerExecuters)
		{
			SceneTriggerExecuterDetails det = ex.RemovePlayerFromExecuter(pl);
			if (det == null)
			{
				det = ex.RemovePlayerFromProximity(pl);
			}
			if (det != null)
			{
				executers.Add(det);
			}
		}
		if (executers.Count > 0)
		{
			ShipStatsMessage retStatsMsg = new ShipStatsMessage();
			retStatsMsg.VesselObjects = new VesselObjects();
			retStatsMsg.GUID = GUID;
			retStatsMsg.VesselObjects.SceneTriggerExecuters = executers;
			ShipStatsMessageListener(retStatsMsg);
		}
	}

	public override bool HasPlayerInCrew(Player pl)
	{
		return VesselCrew.Contains(pl);
	}

	public void RemovePlayerFromRoom(Player pl)
	{
		base.MainDistributionManager.RemovePlayerFromRoom(pl);
	}

	public override void UpdateTimers(double deltaTime)
	{
		base.UpdateTimers(deltaTime);
		if (SelfDestructTimer != null)
		{
			SelfDestructTimer.Update();
			if (SelfDestructTimer.Time == 0f)
			{
				SelfDestructTimer = null;
				if (DockedToVessel != null && DockedToVessel is Ship)
				{
					DockedToVessel.SelfDestructTimer = new SelfDestructTimer(this, MathHelper.RandomRange(1f, 3f));
				}
				foreach (SpaceObjectVessel ves in DockedVessels.Where((SpaceObjectVessel m) => m is Ship))
				{
					ves.SelfDestructTimer = new SelfDestructTimer(ves, MathHelper.RandomRange(1f, 3f));
				}
				ChangeHealthBy(0f - MaxHealth, null, VesselRepairPoint.Priority.None, force: true, VesselDamageType.SelfDestruct);
			}
		}
		if (VesselCrew.Count > 0)
		{
			Temperature = SpaceExposureTemperature(Temperature, HeatCollectionFactor, HeatDissipationFactor, (float)Mass, deltaTime);
		}
		systemsUpdateTimer += deltaTime;
		if (base.CurrentCourse != null && AutoActivateCourse == base.CurrentCourse && base.CurrentCourse.StartSolarSystemTime > Server.SolarSystemTime && base.CurrentCourse.StartSolarSystemTime <= Server.SolarSystemTime + 1.0)
		{
			AutoActivateCourse.ToggleActivated(activate: true);
			AutoActivateCourse = null;
		}
	}

	public override void UpdateVesselSystems()
	{
		if (base.IsMainVessel)
		{
			base.MainDistributionManager.UpdateSystems(ConnectionsChanged, ConnectionsChanged);
			ConnectionsChanged = false;
		}
		ShipStatsMessage ssm = new ShipStatsMessage();
		ssm.GUID = GUID;
		ssm.Temperature = Temperature;
		ssm.Health = base.Health;
		ssm.Armor = base.Armor;
		ssm.VesselObjects = new VesselObjects();
		ssm.VesselObjects.SubSystems = DistributionManager.GetSubSystemsDetails(changedOnly: true, GUID);
		ssm.VesselObjects.Generators = DistributionManager.GetGeneratorsDetails(changedOnly: true, GUID);
		ssm.VesselObjects.RoomTriggers = DistributionManager.GetRoomsDetails(changedOnly: true, GUID);
		ssm.VesselObjects.ResourceContainers = DistributionManager.GetResourceContainersDetails(changedOnly: true, GUID);
		ssm.VesselObjects.RepairPoints = GetVesselRepairPointsDetails(changedOnly: true);
		ssm.VesselObjects.Doors = DistributionManager.GetDoorsDetails(changedOnly: true, GUID);
		if (SelfDestructTimer != null && prevDestructionSolarSystemTime != SelfDestructTimer.DestructionSolarSystemTime)
		{
			prevDestructionSolarSystemTime = SelfDestructTimer.DestructionSolarSystemTime;
			ssm.SelfDestructTime = SelfDestructTimer?.Time;
		}
		NetworkController.Instance.SendToClientsSubscribedTo(ssm, -1L, this);
		foreach (DynamicObject dobj in DynamicObjects.Values.Where((DynamicObject x) => x.Item != null && x.Item.AttachPointType != AttachPointType.None))
		{
			if (dobj.Item.AttachPointType == AttachPointType.BatteryRechargePoint && dobj.Item is Battery)
			{
				Battery bat = dobj.Item as Battery;
				bat.ChangeQuantity(bat.ChargeAmount);
			}
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
		EventSystem.RemoveListener(typeof(ShipStatsMessage), ShipStatsMessageListener);
		EventSystem.RemoveListener(typeof(ManeuverCourseRequest), ManeuverCourseRequestListener);
		EventSystem.RemoveListener(typeof(DistressCallRequest), DistressCallRequestListener);
		EventSystem.RemoveListener(typeof(VesselRequest), VesselRequestListener);
		EventSystem.RemoveListener(typeof(VesselSecurityRequest), VesselSecurityRequestListener);
		EventSystem.RemoveListener(typeof(RoomPressureMessage), RoomPressureMessageListener);
		EventSystem.RemoveListener(typeof(RecycleItemMessage), RecycleItemMessageListener);
	}

	~Ship()
	{
		DisconectListener();
	}

	public void DampenRotation(Vector3D stabilizeAxes, double timeDelta, double stabilizationMultiplier = 1.0, double? rotationStabilization = null)
	{
		if (!rotationStabilization.HasValue)
		{
			rotationStabilization = ((RCS == null && IsPrefabStationVessel) ? 1f : base.RCSRotationStabilization);
		}
		double stabilizationValue = rotationStabilization.Value * stabilizationMultiplier * timeDelta * Server.RCS_ROTATION_MULTIPLIER;
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
		data.GUID = GUID;
		data.Health = base.Health;
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
		data.SceneID = base.SceneID;
		data.timePassedSinceShipCall = timePassedSinceRequest;
		data.IsDistressSignalActive = base.IsDistressSignalActive;
		data.IsAlwaysVisible = base.IsAlwaysVisible;
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
					data.DockedToShipGUID = DockedToVessel.GUID;
					data.DockedPortID = port.ID.InSceneID;
					data.DockedToPortID = port.DockedToID.InSceneID;
					break;
				}
			}
		}
		if (base.StabilizeToTargetObj != null)
		{
			data.StabilizeToTargetGUID = base.StabilizeToTargetObj.GUID;
			data.StabilizeToTargetPosition = base.StabilizeToTargetRelPosition.ToArray();
		}
		if (base.IsMainVessel && base.CurrentCourse != null && base.CurrentCourse.IsInProgress)
		{
			data.CourseInProgress = base.CurrentCourse.CurrentCourseItem;
		}
		data.DynamicObjects = new List<PersistenceObjectData>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			data.DynamicObjects.Add((dobj.Item != null) ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
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
		foreach (Generator gen in DistributionManager.GetGenerators())
		{
			data.Generators.Add(gen.GetPersistenceData() as PersistenceObjectDataVesselComponent);
		}
		data.SubSystems = new List<PersistenceObjectDataVesselComponent>();
		foreach (SubSystem ss in DistributionManager.GetSubSystems())
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
		data.DockingPorts = new List<PersistenceObjectDataDockingPort>();
		foreach (VesselDockingPort dp in DockingPorts)
		{
			data.DockingPorts.Add(dp.GetPersistenceData() as PersistenceObjectDataDockingPort);
		}
		data.Executers = new List<PersistenceObjectDataExecuter>();
		foreach (SceneTriggerExecuter exe in SceneTriggerExecuters)
		{
			data.Executers.Add(exe.GetPersistenceData() as PersistenceObjectDataExecuter);
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

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			PersistenceObjectDataShip data = persistenceData as PersistenceObjectDataShip;
			GUID = data.GUID;
			CreateShipData(data.Registration, data.Tag, data.SceneID, loadDynamicObjects: false);
			VesselData.VesselName = data.Name;
			EmblemId = data.EmblemId;
			LoadShipRequestPersistance(data.timePassedSinceShipCall);
			DistributionManager = new DistributionManager(this);
			InitializeOrbit(Vector3D.Zero, Vector3D.One, data.Forward.ToVector3D(), data.Up.ToVector3D());
			Server.Instance.PhysicsController.CreateAndAddRigidBody(this);
			Rotation = data.Rotation.ToVector3D();
			base.Health = data.Health;
			IsInvulnerable = data.IsInvulnerable;
			DockingControlsDisabled = data.DockingControlsDisabled;
			SecurityPanelsLocked = data.SecurityPanelsLocked;
			StructureSceneData structureSceneData = ObjectCopier.DeepCopy(StaticData.StructuresDataList.Find((StructureSceneData m) => m.ItemID == (short)base.SceneID));
			foreach (PersistenceObjectDataDynamicObject dobjData in data.DynamicObjects)
			{
				DynamicObject dobj = Persistence.CreateDynamicObject(dobjData, this, structureSceneData);
				if (dobj != null && dobj.Item != null && dobj.Item.AttachPointID != null)
				{
					VesselComponent comp = base.MainDistributionManager.GetVesselComponentByPartSlot(dobj.Item.AttachPointID);
					if (comp != null && dobj.Item is MachineryPart)
					{
						comp.FitPartToSlot(dobj.Item.AttachPointID, (MachineryPart)dobj.Item);
					}
				}
			}
			if (data.CargoBay != null)
			{
				CargoBay.LoadPersistenceData(data.CargoBay);
			}
			if (data.ResourceTanks != null)
			{
				foreach (PersistenceObjectDataCargo rtd in data.ResourceTanks)
				{
					DistributionManager.GetResourceContainer(new VesselObjectID
					{
						VesselGUID = GUID,
						InSceneID = rtd.InSceneID
					})?.LoadPersistenceData(rtd);
				}
			}
			if (data.Generators != null)
			{
				foreach (PersistenceObjectDataVesselComponent vc in data.Generators)
				{
					DistributionManager.GetGenerator(new VesselObjectID
					{
						VesselGUID = GUID,
						InSceneID = vc.InSceneID
					})?.LoadPersistenceData(vc);
				}
			}
			if (data.SubSystems != null)
			{
				foreach (PersistenceObjectDataVesselComponent vc2 in data.SubSystems)
				{
					DistributionManager.GetSubSystem(new VesselObjectID
					{
						VesselGUID = GUID,
						InSceneID = vc2.InSceneID
					})?.LoadPersistenceData(vc2);
				}
			}
			if (data.Rooms != null)
			{
				foreach (PersistenceObjectDataRoom r in data.Rooms)
				{
					DistributionManager.GetRoom(new VesselObjectID
					{
						VesselGUID = GUID,
						InSceneID = r.InSceneID
					})?.LoadPersistenceData(r);
				}
			}
			if (data.Doors != null)
			{
				foreach (PersistenceObjectDataDoor d in data.Doors)
				{
					Doors.Find((Door x) => x.ID.InSceneID == d.InSceneID)?.LoadPersistenceData(d);
				}
			}
			if (data.DockingPorts != null)
			{
				foreach (PersistenceObjectDataDockingPort dp in data.DockingPorts)
				{
					DockingPorts.Find((VesselDockingPort x) => x.ID.InSceneID == dp.InSceneID)?.LoadPersistenceData(dp);
				}
			}
			if (data.Executers != null)
			{
				foreach (PersistenceObjectDataExecuter e2 in data.Executers)
				{
					SceneTriggerExecuters.Find((SceneTriggerExecuter x) => x.InSceneID == e2.InSceneID)?.LoadPersistenceData(e2);
				}
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
			if (data.RepairPoints != null && data.RepairPoints.Count > 0)
			{
				foreach (PersistenceObjectDataRepairPoint rp in data.RepairPoints)
				{
					RepairPoints.Find((VesselRepairPoint x) => x.ID.InSceneID == rp.InSceneID)?.LoadPersistenceData(rp);
				}
			}
			base.MainDistributionManager.UpdateSystems();
			if (data.OrbitData != null)
			{
				Orbit.ParseNetworkData(data.OrbitData, resetOrbit: true);
			}
			Server.Instance.Add(this);
			SetPhysicsParameters();
			if (data.DockedToShipGUID.HasValue)
			{
				Ship dockToShip = Server.Instance.GetVessel(data.DockedToShipGUID.Value) as Ship;
				VesselDockingPort myPort = DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == data.DockedPortID.Value);
				VesselDockingPort dockedToPort = dockToShip.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == data.DockedToPortID.Value);
				DockToVessel(myPort, dockedToPort, dockToShip, disableStabilization: false, useCurrentSolarSystemTime: true, buildingStation: true);
			}
			if (data.StabilizeToTargetGUID.HasValue)
			{
				SpaceObjectVessel ab = Server.Instance.GetObject(data.StabilizeToTargetGUID.Value) as SpaceObjectVessel;
				StabilizeToTarget(ab, forceStabilize: true);
				base.StabilizeToTargetRelPosition = data.StabilizeToTargetPosition.ToVector3D();
				UpdateStabilization();
			}
			if (data.timePassedSinceShipCall > 0.0)
			{
				LoadShipRequestPersistance(data.timePassedSinceShipCall);
			}
			base.IsDistressSignalActive = data.IsDistressSignalActive;
			base.IsAlwaysVisible = data.IsAlwaysVisible;
			IsPrefabStationVessel = data.IsPrefabStationVessel;
			if (data.SelfDestructTimer != null)
			{
				SelfDestructTimer = new SelfDestructTimer(this, data.SelfDestructTimer);
			}
			AuthorizedPersonel = data.AuthorizedPersonel;
			StartingSetId = data.StartingSetId;
			if (data.CourseInProgress != null)
			{
				base.CurrentCourse = ManeuverCourse.ParsePersistenceData(data.CourseInProgress, this);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public void VesselRequestListener(NetworkData data)
	{
		VesselRequest vr = data as VesselRequest;
		if (vr.GUID != GUID)
		{
			return;
		}
		VesselRequestResponse vrr = new VesselRequestResponse();
		vrr.GUID = GUID;
		vrr.Active = false;
		if (timePassedSinceRequest > 0.0)
		{
			vrr.Message = RescueShipMessages.ShipEnRoute;
			vrr.Time = (float)(RespawnTimeForShip - timePassedSinceRequest);
			foreach (Player p3 in base.MainVessel.VesselCrew)
			{
				NetworkController.Instance.SendToGameClient(p3.GUID, vrr);
			}
			{
				foreach (Ship shp3 in base.MainVessel.AllDockedVessels)
				{
					foreach (Player pl3 in shp3.VesselCrew)
					{
						NetworkController.Instance.SendToGameClient(pl3.GUID, vrr);
					}
				}
				return;
			}
		}
		if (Server.Instance.SolarSystem.GetArtificialBodieslsInRange(this, 5000.0).FirstOrDefault((ArtificialBody m) => m is SpaceObjectVessel && GameScenes.Ranges.IsShip((m as SpaceObjectVessel).SceneID)) == null)
		{
			timePassedSinceRequest = 1.0;
			RespawnTimeForShip = vr.Time;
			RescueShipSceneID = vr.RescueShipSceneID;
			RescueShipTag = vr.RescueShipTag;
			vrr.Message = RescueShipMessages.ShipCalled;
			vrr.Time = (float)RespawnTimeForShip;
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_sec, SpawnShipCallback);
			foreach (Player p2 in base.MainVessel.VesselCrew)
			{
				NetworkController.Instance.SendToGameClient(p2.GUID, vrr);
			}
			{
				foreach (Ship shp2 in base.MainVessel.AllDockedVessels)
				{
					foreach (Player pl2 in shp2.VesselCrew)
					{
						NetworkController.Instance.SendToGameClient(pl2.GUID, vrr);
					}
				}
				return;
			}
		}
		vrr.Message = RescueShipMessages.AnotherShipInRange;
		foreach (Player p in base.MainVessel.VesselCrew)
		{
			NetworkController.Instance.SendToGameClient(p.GUID, vrr);
		}
		foreach (Ship shp in base.MainVessel.AllDockedVessels)
		{
			foreach (Player pl in shp.VesselCrew)
			{
				NetworkController.Instance.SendToGameClient(pl.GUID, vrr);
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

	public void SpawnShipCallback(double dbl)
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
		CurrentSpawnedShip = SpawnRescueShip(this, newPos, RescueShipSceneID, RescueShipTag);
		Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_1_0_sec, SpawnShipCallback);
		timePassedSinceRequest = 0.0;
		VesselRequestResponse vrr = new VesselRequestResponse();
		vrr.Active = false;
		vrr.GUID = GUID;
		vrr.Message = RescueShipMessages.ShipArrived;
		foreach (Player p in base.MainVessel.VesselCrew)
		{
			NetworkController.Instance.SendToGameClient(p.GUID, vrr);
		}
		foreach (Ship shp in base.MainVessel.AllDockedVessels)
		{
			foreach (Player pl in shp.VesselCrew)
			{
				NetworkController.Instance.SendToGameClient(pl.GUID, vrr);
			}
		}
	}

	public static Ship SpawnRescueShip(SpaceObjectVessel mainShip, Vector3D pos, GameScenes.SceneID sceneID, string tag)
	{
		Ship rescueShip = CreateNewShip(sceneID, "", -1L, new List<long> { mainShip.GUID }, null, pos, null, MathHelper.RandomRotation(), tag + ((tag == "" || tag.EndsWith(";")) ? "" : ";") + "_RescueVessel");
		rescueShip.StabilizeToTarget(mainShip, forceStabilize: true);
		return rescueShip;
	}

	public void DistressCallRequestListener(NetworkData data)
	{
		DistressCallRequest dcr = data as DistressCallRequest;
		if (dcr.GUID == GUID)
		{
			base.IsDistressSignalActive = dcr.IsDistressActive;
			base.MainVessel.UpdateVesselData();
		}
	}

	public void VesselSecurityRequestListener(NetworkData data)
	{
		VesselSecurityRequest req = data as VesselSecurityRequest;
		if (req.VesselGUID != GUID)
		{
			return;
		}
		Player sender = Server.Instance.GetPlayer(data.Sender);
		if (sender != null)
		{
			// Get player.
			Player pl;
			if (!req.AddPlayerId.IsNullOrEmpty())
			{
				pl = Server.Instance.GetPlayerFromPlayerId(req.AddPlayerId);
			}
			else if (!req.RemovePlayerId.IsNullOrEmpty())
			{
				pl = Server.Instance.GetPlayerFromPlayerId(req.RemovePlayerId);
			}
			else
			{
				return;
			}

			bool sendSecurityResponse = false;

			// Change name.
			if (!req.VesselName.IsNullOrEmpty() && ChangeVesselName(sender, req.VesselName))
			{
				sendSecurityResponse = true;
			}

			// Add player.
			if (!req.AddPlayerId.IsNullOrEmpty() && req.AddPlayerRank.HasValue && AddAuthorizedPerson(sender, pl, req.AddPlayerName, req.AddPlayerRank.Value))
			{
				sendSecurityResponse = true;
			}

			// Removing player.
			if (!req.RemovePlayerId.IsNullOrEmpty() && RemoveAuthorizedPerson(sender, pl))
			{
				sendSecurityResponse = true;
			}

			// Hack.
			if (req.HackPanel.HasValue && req.HackPanel.Value && ClearSecuritySystem(sender))
			{
				sendSecurityResponse = true;
			}

			// Send if it was successful.
			if (sendSecurityResponse)
			{
				SendSecurityResponse(includeVesselName: true);
			}
		}
	}

	public void RoomPressureMessageListener(NetworkData data)
	{
		RoomPressureMessage rpm = data as RoomPressureMessage;
		if (rpm.ID.VesselGUID != GUID)
		{
			return;
		}
		Room room = Rooms.FirstOrDefault((Room m) => m.ID.Equals(rpm.ID));
		if (room == null)
		{
			return;
		}
		if (rpm.TargetPressure.HasValue)
		{
			room.EquilizePressureRoom = null;
			room.TargetPressure = rpm.TargetPressure.Value;
		}
		else if (rpm.TargetRoomID != null)
		{
			SpaceObjectVessel vessel = Server.Instance.GetVessel(rpm.TargetRoomID.VesselGUID);
			if (vessel != null)
			{
				room.TargetPressure = null;
				room.EquilizePressureRoom = vessel.Rooms.FirstOrDefault((Room m) => m.ID.Equals(rpm.TargetRoomID));
			}
		}
		else
		{
			room.TargetPressure = null;
			room.EquilizePressureRoom = null;
		}
	}

	public void RecycleItemMessageListener(NetworkData data)
	{
		RecycleItemMessage rim = data as RecycleItemMessage;
		if (rim.ID.VesselGUID == GUID && AttachPoints.TryGetValue(rim.ID.InSceneID, out var ap))
		{
			Item item = ((!rim.GUID.HasValue) ? ap.Item : Server.Instance.GetItem(rim.GUID.Value));
			if (item != null && (item.DynamicObj.Parent == this || item.DynamicObj.Parent.Parent == this))
			{
				Player pl = Server.Instance.GetPlayer(rim.Sender);
				RecycleItem(item, rim.RecycleMode, pl);
			}
		}
	}

	private void RecycleItem(Item item, RecycleMode mode, Player pl)
	{
		if (item is Outfit)
		{
			foreach (InventorySlot invSlot in (item as Outfit).InventorySlots.Values)
			{
				if (invSlot.Item != null)
				{
					RecycleItem(invSlot.Item, mode, pl);
				}
			}
		}
		else
		{
			foreach (ItemSlot slot in item.Slots.Values)
			{
				if (slot.Item != null)
				{
					RecycleItem(slot.Item, mode, pl);
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
				if (craftingResources != null && craftingResources.Count > 0)
				{
					pl.Blueprints.Add(cit);
					NetworkController.Instance.SendToGameClient(pl.GUID, new UpdateBlueprintsMessage
					{
						Blueprints = pl.Blueprints
					});
				}
			}
		}
		if (item is Grenade && mode != RecycleMode.ResearchOnly)
		{
			item.TakeDamage(TypeOfDamage.Impact, MaxHealth, forceTakeDamage: true);
		}
		else
		{
			item.DestroyItem();
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
				qty -= CargoBay.ChangeQuantityBy(ccd.ID, rr.Key, qty);
				if (qty <= float.Epsilon)
				{
					break;
				}
			}
		}
	}

	public void ResetSpawnPointsForPlayer(Player pl, bool sendStatsMessage)
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
					retMsg.GUID = GUID;
					retMsg.Temperature = Temperature;
					retMsg.Health = base.Health;
					retMsg.Armor = base.Armor;
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
			NetworkController.Instance.SendToClientsSubscribedTo(retMsg, -1L, this);
		}
	}

	public override float ChangeHealthBy(float value, List<VesselRepairPoint> repairPoints = null, VesselRepairPoint.Priority damagePiority = VesselRepairPoint.Priority.None, bool force = false, VesselDamageType damageType = VesselDamageType.None, double time = 1.0)
	{
		if (value >= 0f)
		{
			LastVesselDamageType = VesselDamageType.None;
		}
		if (!force && value < 0f && (base.AllVessels.Sum((SpaceObjectVessel n) => n.VesselCrew.Count((Player m) => m.GodMode)) > 0 || IsInvulnerable || (InvulnerableWhenDocked && (DockedToVessel != null || DockedVessels.Count > 0))))
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
					value += VesselBaseSystem.ConsumeArmor(0f - value, time);
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
		_Health = MathHelper.Clamp(base.Health + value, 0f, MaxHealth);
		if (damageType != 0 && base.Health > 0f && prevRadarSignatureHealthMultiplierIndex != RadarSignatureHealthMultiplierIndex)
		{
			base.MainVessel.UpdateVesselData();
		}
		if (value < 0f && RepairPoints.Count > 0)
		{
			float damage = System.Math.Abs(value);
			Func<VesselRepairPoint, double> sortOrder = (VesselRepairPoint m) => (double)((m.Health == m.MaxHealth) ? 1 : 0) + MathHelper.RandomNextDouble();
			List<VesselRepairPoint> list = RepairPoints.OrderBy(sortOrder).ToList();
			if (repairPoints != null)
			{
				list.RemoveAll((VesselRepairPoint m) => repairPoints.Contains(m));
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
				list.RemoveAll((VesselRepairPoint m) => priority.Contains(m));
				list.InsertRange(0, priority);
				if (MathHelper.RandomNextDouble() < (double)damage * Server.ActivateRepairPointChanceMultiplier / (double)MaxHealth)
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
				if (rp.AffectedSystem != null && rp.AffectedSystem.Defective && MathHelper.RandomNextDouble() < (double)damage * Server.DamageUpgradePartChanceMultiplier / (double)MaxHealth)
				{
					MachineryPart mp = (from m in rp.AffectedSystem.MachineryParts.Values
						where m != null
						orderby MathHelper.RandomNextDouble()
						select m).FirstOrDefault();
					mp?.TakeDamage(new Dictionary<TypeOfDamage, float> {
					{
						TypeOfDamage.Impact,
						mp.MaxHealth
					} }, forceTakeDamage: true);
				}
				if (rp.Health >= damage)
				{
					float s = (from m in RepairPoints.ToArray()
						where m != rp
						select m).Sum((VesselRepairPoint k) => k.Health);
					rp.Health = base.Health - s;
					break;
				}
				damage -= rp.Health;
				rp.Health = 0f;
			}
		}
		return base.Health - prevHealth;
	}
}
