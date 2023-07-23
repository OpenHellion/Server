using System;
using System.Collections.Generic;
using System.Linq;
using BulletSharp;
using OpenHellion.Networking;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class ManeuverCourse
{
	private static readonly double _activationTimeDifference = 5.0;

	private static readonly double _activationDirectionDifference = 5.0;

	public static readonly double StartingSoonTime = 2.0;

	private long GUID;

	private Ship parentShip;

	private bool isValid = false;

	private bool isActivated = false;

	private bool isStartingSoonSent = false;

	private bool isStarted = false;

	private ManeuverType type;

	private double startSolarSystemTime;

	private double endSolarSystemTime;

	private double travelTime;

	private BezierD bezCurve;

	private double bezCurveScale;

	private int currentCourseDataIndex = -1;

	private List<CourseItemData> courseItems = new List<CourseItemData>();

	private Vector3D startPos;

	private Vector3D startVel;

	private Vector3D startDir = Vector3D.Forward;

	private Vector3D targetPos;

	private Vector3D targetVel;

	private double warpAcceleration;

	private bool isSameParentWarp;

	private double warpDistance;

	private double tparam;

	private SpaceObjectVessel targetVessel = null;

	private WarpData warpData;

	private Vector3D endPosDeviation;

	private OrbitParameters startOrbit;

	private OrbitParameters targetOrbit;

	public long CourseGUID => GUID;

	public bool IsValid => isValid;

	public bool IsActivated => isActivated;

	public double StartSolarSystemTime => startSolarSystemTime;

	public double EndSolarSystemTime => endSolarSystemTime;

	public bool IsInProgress => isValid && isStarted;

	public bool IsStartingSoonSent => isStartingSoonSent;

	public ManeuverType Type => type;

	public SpaceObjectVessel TargetVessel => targetVessel;

	public CourseItemData CurrentCourseItem
	{
		get
		{
			if (courseItems.Count == 0 || currentCourseDataIndex < 0 || currentCourseDataIndex > courseItems.Count)
			{
				return null;
			}
			return courseItems[currentCourseDataIndex];
		}
	}

	public bool ReadNextManeuverCourse()
	{
		currentCourseDataIndex++;
		if (courseItems != null && courseItems.Count > currentCourseDataIndex)
		{
			CourseItemData data = courseItems[currentCourseDataIndex];
			type = data.Type;
			isActivated = false;
			isStartingSoonSent = false;
			isStarted = false;
			SetCollisionEnable(value: true);
			if (parentShip.FTL != null && currentCourseDataIndex > 0)
			{
				parentShip.FTL.GoOffLine(autoRestart: false);
			}
			if (data.Type == ManeuverType.Engine)
			{
				isValid = CheckEngineManeuverData(data);
			}
			else if (data.Type == ManeuverType.Transfer)
			{
				isValid = CheckTransferManeuverData(data);
			}
			else if (data.Type == ManeuverType.Warp)
			{
				isValid = CheckWarpManeuverData(data);
			}
			if (!isValid)
			{
				NetworkController.Instance.SendToClientsSubscribedTo(new ManeuverCourseResponse
				{
					IsValid = isValid,
					CourseGUID = GUID,
					VesselGUID = parentShip.GUID
				}, -1L, parentShip);
			}
			else if (data.EndOrbit.GUID.HasValue && (data.EndOrbit.ObjectType.Value == SpaceObjectType.Ship || data.EndOrbit.ObjectType.Value == SpaceObjectType.Asteroid))
			{
				targetVessel = Server.Instance.GetVessel(data.EndOrbit.GUID.Value);
			}
		}
		else
		{
			isValid = false;
			isActivated = false;
			isStartingSoonSent = false;
			isStarted = false;
			SetCollisionEnable(value: true);
			if (parentShip.FTL != null)
			{
				parentShip.FTL.GoOffLine(autoRestart: false);
			}
			NetworkController.Instance.SendToAllClients(new ManeuverCourseResponse
			{
				IsValid = false,
				IsFinished = true,
				CourseGUID = GUID,
				VesselGUID = parentShip.GUID
			}, -1L);
		}
		return isValid;
	}

	public void ToggleActivated(bool activate)
	{
		if (activate == isActivated)
		{
			return;
		}
		if (StartSolarSystemTime < Server.SolarSystemTime)
		{
			Invalidate();
			return;
		}
		double angle = Vector3D.Angle(parentShip.Forward, startDir);
		if (!activate || (!(StartSolarSystemTime - Server.SolarSystemTime > _activationTimeDifference) && !(angle > _activationDirectionDifference)))
		{
			isActivated = activate;
		}
	}

	public bool FillPositionAndVelocityAtCurrentTime(ref Vector3D relativePosition, ref Vector3D relativeVelocity)
	{
		return FillPositionAndVelocityAtTime(Server.SolarSystemTime, ref relativePosition, ref relativeVelocity);
	}

	private bool FillPositionAndVelocityAtTime(double solarSystemTime, ref Vector3D relativePosition, ref Vector3D relativeVelocity)
	{
		if (type == ManeuverType.Engine || type == ManeuverType.Transfer)
		{
			tparam = MathHelper.Clamp((solarSystemTime - startSolarSystemTime) / (endSolarSystemTime - startSolarSystemTime), 0.0, 1.0);
			bezCurve.FillDataAtPart(tparam, ref relativePosition, ref relativeVelocity);
			relativeVelocity /= bezCurveScale;
			return true;
		}
		if (type == ManeuverType.Warp)
		{
			double timePassed = MathHelper.Clamp(solarSystemTime - startSolarSystemTime, 0.0, endSolarSystemTime - startSolarSystemTime);
			if (timePassed < travelTime / 2.0)
			{
				tparam = 0.5 * warpAcceleration * timePassed * timePassed / warpDistance;
			}
			else
			{
				timePassed = travelTime - timePassed;
				tparam = 1.0 - 0.5 * warpAcceleration * timePassed * timePassed / warpDistance;
			}
			if (isSameParentWarp)
			{
				relativePosition = Vector3D.Lerp(startPos, targetPos, tparam);
				relativeVelocity = Vector3D.Lerp(startVel, targetVel, tparam);
			}
			else
			{
				relativePosition = Vector3D.Lerp(startPos, targetPos, tparam) - parentShip.Orbit.Parent.Position;
				relativeVelocity = Vector3D.Lerp(startVel, targetVel, tparam) - parentShip.Orbit.Parent.Velocity;
			}
			return true;
		}
		return false;
	}

	public void SetFinalPosition()
	{
		targetOrbit.UpdateOrbit();
		double stopDist = 0.0;
		ArtificialBody[] artificialBodies = (from m in Server.Instance.SolarSystem.GetArtificialBodieslsInRange(targetOrbit.Parent.CelestialBody, targetOrbit.RelativePosition, 5000.0)
			where !(m is SpaceObjectVessel) || (m as SpaceObjectVessel).MainVessel != parentShip.MainVessel
			select m).ToArray();
		Vector3D endPos;
		while (true)
		{
			endPos = targetOrbit.RelativePosition + endPosDeviation - parentShip.Forward * stopDist;
			bool clear = true;
			ArtificialBody[] array = artificialBodies;
			foreach (ArtificialBody ab in array)
			{
				double sqDist = (ab.Orbit.RelativePosition - endPos).SqrMagnitude;
				if (sqDist < System.Math.Pow(ab.Radius + parentShip.Radius + 100.0, 2.0))
				{
					clear = false;
					break;
				}
			}
			if (clear)
			{
				break;
			}
			stopDist += 100.0;
		}
		parentShip.Orbit.RelativePosition = endPos;
		parentShip.Orbit.RelativeVelocity = targetOrbit.RelativeVelocity;
		parentShip.Orbit.InitFromCurrentStateVectors(Server.SolarSystemTime);
		parentShip.Orbit.UpdateOrbit();
	}

	public void Invalidate()
	{
		if (isValid)
		{
			if (parentShip.IsWarping)
			{
				Vector3D relPos = parentShip.Orbit.Position - parentShip.Orbit.Parent.Position;
				OrbitParameters tempOrbit = new OrbitParameters();
				Vector3D proj = Vector3D.ProjectOnPlane(relPos, Vector3D.Up);
				double inclination = Vector3D.Angle(proj, relPos);
				double argumentOfPeriapsis = Vector3D.Angle(Vector3D.Forward, proj) + 90.0;
				double LongitudeOfAscendingNode = 0.0;
				double TrueAnomalyAngleDeg = 0.0;
				tempOrbit.InitFromPeriapisAndApoapsis(parentShip.Orbit.Parent, relPos.Magnitude, relPos.Magnitude, inclination, argumentOfPeriapsis, LongitudeOfAscendingNode, TrueAnomalyAngleDeg, Server.Instance.SolarSystem.CurrentTime);
				tempOrbit.UpdateOrbit();
				tempOrbit.RelativePosition += parentShip.Orbit.Position - tempOrbit.Position;
				tempOrbit.InitFromCurrentStateVectors(Server.SolarSystemTime);
				tempOrbit.UpdateOrbit();
				parentShip.Orbit.CopyDataFrom(tempOrbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
				parentShip.Orbit.UpdateOrbit();
			}
			isStarted = false;
			isValid = false;
			SetCollisionEnable(value: true);
			if (parentShip?.FTL != null)
			{
				parentShip.FTL.GoOffLine(autoRestart: false);
			}
			NetworkController.Instance.SendToClientsSubscribedTo(new ManeuverCourseResponse
			{
				IsValid = isValid,
				CourseGUID = GUID,
				VesselGUID = parentShip.GUID
			}, -1L, parentShip);
		}
	}

	public void OrbitParentChanged()
	{
		if (type != ManeuverType.Warp)
		{
			Invalidate();
		}
	}

	private bool CheckEngineManeuverData(CourseItemData data)
	{
		return false;
	}

	private bool CheckTransferManeuverData(CourseItemData data)
	{
		return false;
	}

	private bool CheckWarpManeuverData(CourseItemData data)
	{
		startOrbit = parentShip.Orbit;
		targetOrbit = new OrbitParameters();
		targetOrbit.ParseNetworkData(data.EndOrbit, resetOrbit: true);
		startOrbit.FillPositionAndVelocityAfterTime(data.StartSolarSystemTime - Server.SolarSystemTime, fillRelativeData: true, ref startPos, ref startVel);
		targetOrbit.FillPositionAndVelocityAfterTime(data.EndSolarSystemTime - Server.SolarSystemTime, fillRelativeData: true, ref targetPos, ref targetVel);
		travelTime = data.EndSolarSystemTime - data.StartSolarSystemTime;
		if (travelTime <= 0.0)
		{
			return false;
		}
		isSameParentWarp = startOrbit.Parent == targetOrbit.Parent;
		startOrbit.FillPositionAndVelocityAfterTime(data.StartSolarSystemTime - Server.SolarSystemTime, isSameParentWarp, ref startPos, ref startVel);
		targetOrbit.FillPositionAndVelocityAfterTime(data.EndSolarSystemTime - Server.SolarSystemTime, isSameParentWarp, ref targetPos, ref targetVel);
		warpDistance = (startPos - targetPos).Magnitude;
		warpAcceleration = 4.0 * warpDistance / (travelTime * travelTime);
		if (!CheckManeuverStartData(data, checkSystems: false, consumeResources: false))
		{
			return false;
		}
		startSolarSystemTime = data.StartSolarSystemTime;
		endSolarSystemTime = data.EndSolarSystemTime;
		startDir = (targetPos - startPos).Normalized;
		return true;
	}

	public bool StartManeuver()
	{
		if (!isValid)
		{
			return false;
		}
		if (!isActivated || Vector3D.Angle(parentShip.Forward, startDir) > _activationDirectionDifference || !CheckManeuverStartData(courseItems[currentCourseDataIndex], checkSystems: true, consumeResources: true))
		{
			Invalidate();
			return false;
		}
		isStarted = true;
		SetCollisionEnable(value: false);
		parentShip.RemoveFromSpawnSystem();
		parentShip.DisableStabilization(disableForChildren: true, updateBeforeDisable: false);
		return true;
	}

	private void SetCollisionEnable(bool value)
	{
		if (parentShip.MainVessel.RigidBody != null)
		{
			parentShip.MainVessel.RigidBody.CollisionFlags = ((!value) ? CollisionFlags.NoContactResponse : CollisionFlags.None);
		}
	}

	private bool CheckManeuverStartData(CourseItemData data, bool checkSystems, bool consumeResources)
	{
		try
		{
			if (data.Type == ManeuverType.Warp)
			{
				if (parentShip.GetCompoundMass() > parentShip.Mass + (double)parentShip.FTL.TowingCapacity || (data.WarpIndex == 0 && parentShip.MainVessel.AllDockedVessels.Count > 0))
				{
					return false;
				}
				if (parentShip.SceneID == GameScenes.SceneID.AltCorp_Shuttle_SARA && parentShip.DockingPorts.Find((VesselDockingPort m) => m.OrderID == 1)?.DockedVessel != null)
				{
					return false;
				}
				float fuelConsumptionMultiplier = (float)(parentShip.GetCompoundMass() / parentShip.Mass);
				warpData = parentShip.FTL.WarpsData[data.WarpIndex];
				endPosDeviation = new Vector3D(MathHelper.RandomRange(-1.0, 1.0), MathHelper.RandomRange(-1.0, 1.0), MathHelper.RandomRange(-1.0, 1.0)).Normalized * MathHelper.RandomRange(0f, warpData.EndPositionDeviation);
				if (warpAcceleration > (double)warpData.MaxAcceleration || warpAcceleration < (double)warpData.MinAcceleration)
				{
					return false;
				}
				Dictionary<int, float?> warpCellFuel = parentShip.FTL.GetWarpCellsFuel();
				float totalCellsFuel = 0f;
				List<int> warpCells = data.WarpCells;
				if (warpCells != null && warpCells.Count > 0)
				{
					foreach (int index in data.WarpCells)
					{
						totalCellsFuel += (warpCellFuel[index].HasValue ? warpCellFuel[index].Value : 0f);
					}
				}
				if ((double)totalCellsFuel < (double)warpData.ActivationCellConsumption + travelTime * (double)warpData.CellConsumption * (double)fuelConsumptionMultiplier)
				{
					return false;
				}
				if (checkSystems)
				{
					parentShip.FTL.GoOnLine();
					if (parentShip.FTL.Status != SystemStatus.OnLine)
					{
						return false;
					}
					if (warpData.PowerConsumption > parentShip.Capacitor.Capacity)
					{
						return false;
					}
				}
				if (consumeResources)
				{
					parentShip.FTL.ConsumeWarpResources(data.WarpCells, (int)((double)warpData.ActivationCellConsumption + travelTime * (double)warpData.CellConsumption * (double)fuelConsumptionMultiplier), warpData.PowerConsumption);
				}
			}
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static ManeuverCourse ParseNetworkData(ManeuverCourseRequest req, Ship sh)
	{
		ManeuverCourse course = new ManeuverCourse();
		course.parentShip = sh;
		course.GUID = req.CourseGUID;
		if (req.CourseItems == null || req.CourseItems.Count == 0)
		{
			return course;
		}
		course.courseItems = new List<CourseItemData>(req.CourseItems);
		return course;
	}

	public static ManeuverCourse ParsePersistenceData(CourseItemData data, Ship sh)
	{
		ManeuverCourse course = new ManeuverCourse();
		course.parentShip = sh;
		course.GUID = 100L;
		course.courseItems = new List<CourseItemData> { data };
		course.currentCourseDataIndex = 0;
		course.type = ManeuverType.Warp;
		course.isValid = true;
		course.isActivated = true;
		course.isStarted = true;
		course.isStartingSoonSent = true;
		course.CheckWarpManeuverData(data);
		return course;
	}

	public ManeuverData CurrentData()
	{
		if (!IsValid || type != ManeuverType.Warp || Server.SolarSystemTime < startSolarSystemTime || Server.SolarSystemTime > endSolarSystemTime)
		{
			return null;
		}
		return new ManeuverData
		{
			GUID = GUID,
			Type = type,
			ParentGUID = parentShip.Orbit.Parent.CelestialBody.GUID,
			RelPosition = parentShip.Orbit.RelativePosition.ToArray(),
			RelVelocity = parentShip.Orbit.RelativeVelocity.ToArray()
		};
	}

	public void SendCourseStartResponse()
	{
		if (isValid)
		{
			NetworkController.Instance.SendToClientsSubscribedTo(new ManeuverCourseResponse
			{
				IsValid = isValid,
				CourseGUID = GUID,
				VesselGUID = parentShip.GUID,
				IsActivated = isActivated,
				StartTime = startSolarSystemTime,
				EndTime = endSolarSystemTime,
				StartDirection = startDir.ToFloatArray(),
				StaringSoon = (isActivated && startSolarSystemTime > Server.SolarSystemTime && Server.SolarSystemTime >= startSolarSystemTime - StartingSoonTime)
			}, -1L, parentShip);
		}
	}

	public void SendCourseStartingSoonResponse()
	{
		if (isValid && !isStartingSoonSent)
		{
			isStartingSoonSent = true;
			NetworkController.Instance.SendToClientsSubscribedTo(new ManeuverCourseResponse
			{
				IsValid = isValid,
				CourseGUID = GUID,
				VesselGUID = parentShip.GUID,
				IsActivated = isActivated,
				StartTime = startSolarSystemTime,
				EndTime = endSolarSystemTime,
				StartDirection = startDir.ToFloatArray(),
				StaringSoon = true
			}, -1L, parentShip);
		}
	}
}
