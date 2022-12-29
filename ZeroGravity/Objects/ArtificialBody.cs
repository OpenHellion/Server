using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public class ArtificialBody : SpaceObject
{
	private double prevUpdatePositionTime = Server.SolarSystemTime;

	public OrbitParameters Orbit = new OrbitParameters();

	private ManeuverCourse _CurrentCourse = null;

	public double LastOrientationChangeTime = 0.0;

	public bool IsInDebrisField;

	public Vector3D _Forward = Vector3D.Forward;

	public Vector3D _Up = Vector3D.Up;

	public double Radius;

	public Vector3D AngularVelocity;

	public Vector3D Rotation;

	public Vector3D PhysicsVelocityDifference;

	public Vector3D PhysicsRotationDifference;

	private bool _markForDestruction;

	private float _RadarSignature;

	private bool updateAngularVelocity = true;

	private static double stabilizeToTargetMaxVelocityDiff = 2.0;

	private static double stabilizeToTargetMaxPositionDiff = 100.0;

	public List<SpaceObjectVessel> StabilizedToTargetChildren = new List<SpaceObjectVessel>();

	private double? stabilizationDisabledTime = null;

	private bool? stabilizationDisableAfterUpdate = null;

	private Vector3D? stabilizationDisableRelativePositionExtra = null;

	private Vector3D? stabilizationDisableRelativeVelocityExtra = null;

	public ManeuverCourse CurrentCourse
	{
		get
		{
			if (_CurrentCourse == null && this is Ship)
			{
				SpaceObjectVessel mainVessel = (this as SpaceObjectVessel).MainVessel;
				if (mainVessel._CurrentCourse != null && mainVessel.MainVessel.FTL != null && mainVessel.FTL.Status == SystemStatus.OnLine)
				{
					return (this as SpaceObjectVessel).MainVessel._CurrentCourse;
				}
				SpaceObjectVessel courseVessel = (this as SpaceObjectVessel).MainVessel.AllDockedVessels.FirstOrDefault((SpaceObjectVessel m) => m._CurrentCourse != null && m.FTL != null && m.FTL.Status == SystemStatus.OnLine);
				if (courseVessel != null)
				{
					return courseVessel._CurrentCourse;
				}
			}
			return _CurrentCourse;
		}
		set
		{
			_CurrentCourse = value;
		}
	}

	public bool IsDistressSignalActive { get; set; }

	public bool IsAlwaysVisible { get; set; }

	public override Vector3D Position => Orbit.Position;

	public override Vector3D Velocity => Orbit.Velocity;

	public virtual Vector3D Forward
	{
		get
		{
			if (this is SpaceObjectVessel && !(this as SpaceObjectVessel).IsMainVessel)
			{
				return QuaternionD.LookRotation((this as SpaceObjectVessel).MainVessel.Forward, (this as SpaceObjectVessel).MainVessel.Up) * (this as SpaceObjectVessel).RelativeRotationFromMainParent * Vector3D.Forward;
			}
			return _Forward;
		}
		set
		{
			if (value.IsEpsilonEqual(Vector3D.Zero, 1.000000013351432E-10))
			{
			}
			if (!_Forward.IsEpsilonEqual(value, 9.9999997473787516E-06))
			{
				LastOrientationChangeTime = Server.Instance.SolarSystem.CurrentTime;
			}
			_Forward = (value.IsEpsilonEqual(Vector3D.Zero, 1.000000013351432E-10) ? _Forward : value);
		}
	}

	public virtual Vector3D Up
	{
		get
		{
			if (this is SpaceObjectVessel && !(this as SpaceObjectVessel).IsMainVessel)
			{
				return QuaternionD.LookRotation((this as SpaceObjectVessel).MainVessel.Forward, (this as SpaceObjectVessel).MainVessel.Up) * (this as SpaceObjectVessel).RelativeRotationFromMainParent * Vector3D.Up;
			}
			return _Up;
		}
		set
		{
			if (value.IsEpsilonEqual(Vector3D.Zero, 1.000000013351432E-10))
			{
			}
			if (!_Up.IsEpsilonEqual(value, 9.9999997473787516E-06))
			{
				LastOrientationChangeTime = Server.Instance.SolarSystem.CurrentTime;
			}
			_Up = (value.IsEpsilonEqual(Vector3D.Zero, 1.000000013351432E-10) ? _Up : value);
		}
	}

	public bool MarkForDestruction
	{
		get
		{
			return _markForDestruction;
		}
		set
		{
			_markForDestruction = value;
			Server.Instance.SolarSystem.CheckDestroyMarkedBodies = true;
		}
	}

	public virtual float RadarSignature
	{
		get
		{
			return _RadarSignature;
		}
		protected set
		{
			_RadarSignature = value;
		}
	}

	public SpaceObjectVessel StabilizeToTargetObj { get; private set; }

	public Vector3D StabilizeToTargetRelPosition { get; protected set; }

	public double StabilizeToTargetTime { get; private set; }

	public ArtificialBody(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid)
	{
		if (initializeOrbit)
		{
			Orbit.SetArtificialBody(this);
			InitializeFromStateVectors(position, velocity);
			Forward = forward;
			Up = up;
			Server.Instance.SolarSystem.AddArtificialBody(this);
		}
	}

	public void InitializeOrbit(Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up, OrbitParameters orbit = null)
	{
		Orbit.SetArtificialBody(this);
		InitializeFromStateVectors(position, velocity, orbit);
		Forward = forward;
		Up = up;
		Server.Instance.SolarSystem.AddArtificialBody(this);
	}

	public override void Destroy()
	{
		base.Destroy();
		DisableStabilization(disableForChildren: true, updateBeforeDisable: false);
		foreach (Player pl in Server.Instance.AllPlayers)
		{
			if (pl.IsSubscribedTo(GUID))
			{
				pl.UnsubscribeFrom(this);
			}
			if (pl.CurrentSpawnPoint != null && pl.CurrentSpawnPoint.Ship == this)
			{
				pl.SetSpawnPoint(null);
			}
			if (pl.AuthorizedSpawnPoint != null && pl.AuthorizedSpawnPoint.Ship == this)
			{
				pl.ClearAuthorizedSpawnPoint();
			}
		}
		Server.Instance.SolarSystem.RemoveArtificialBody(this);
	}

	public void InitializeFromStateVectors(Vector3D position, Vector3D velocity, OrbitParameters orbit = null)
	{
		CelestialBody cbParent = Server.Instance.SolarSystem.FindCelestialBodyParent(position);
		if (velocity.SqrMagnitude < double.Epsilon)
		{
			velocity = Forward * cbParent.Orbit.CircularOrbitVelocityMagnitudeAtDistance(Vector3D.Distance(cbParent.Position, position));
		}
		if (orbit != null)
		{
			Orbit.InitFromPeriapisAndApoapsis(cbParent.Orbit, orbit.PeriapsisDistance, orbit.ApoapsisDistance, orbit.Inclination, orbit.ArgumentOfPeriapsis, orbit.LongitudeOfAscendingNode, orbit.TrueAnomalyAtZeroTime() * (180.0 / System.Math.PI), 0.0);
		}
		else
		{
			Orbit.InitFromStateVectors(cbParent.Orbit, position, velocity, Server.Instance.SolarSystem.CurrentTime, areValuesRelative: false);
		}
		Orbit.SetArtificialBody(this);
	}

	private void ApplyRotation(double deltaTime)
	{
		if (ObjectType == SpaceObjectType.Player || ObjectType == SpaceObjectType.Ship || ObjectType == SpaceObjectType.Asteroid)
		{
			Rotation.X = MathHelper.Clamp(Rotation.X, 0.0 - Server.MaxAngularVelocityPerAxis, Server.MaxAngularVelocityPerAxis);
			Rotation.Y = MathHelper.Clamp(Rotation.Y, 0.0 - Server.MaxAngularVelocityPerAxis, Server.MaxAngularVelocityPerAxis);
			Rotation.Z = MathHelper.Clamp(Rotation.Z, 0.0 - Server.MaxAngularVelocityPerAxis, Server.MaxAngularVelocityPerAxis);
			QuaternionD rot = QuaternionD.LookRotation(Forward, Up) * QuaternionD.Euler(Rotation * deltaTime);
			Forward = rot * Vector3D.Forward;
			Up = rot * Vector3D.Up;
		}
	}

	private void UpdatePosition()
	{
		double deltaTime = Server.SolarSystemTime - prevUpdatePositionTime;
		prevUpdatePositionTime = Server.SolarSystemTime;
		if (deltaTime <= double.Epsilon)
		{
			return;
		}
		if (this is Ship && !(this as Ship).IsMainVessel)
		{
			((this as Ship).MainVessel as Ship).AddDockedVesselsThrust(this as Ship, deltaTime);
			(this as Ship).CheckThrustStatsMessage();
			return;
		}
		if (stabilizationDisabledTime.HasValue)
		{
			if (stabilizationDisabledTime.Value.IsEpsilonEqualD(Server.Instance.SolarSystem.CurrentTime))
			{
				stabilizationDisabledTime = null;
				return;
			}
			stabilizationDisabledTime = null;
		}
		if (CheckCurrentCourse())
		{
			Orbit.InitFromCurrentStateVectors(Server.Instance.SolarSystem.CurrentTime);
		}
		else
		{
			Orbit.UpdateOrbit();
		}
		if (CheckThrustAndRotation(deltaTime))
		{
			if (CurrentCourse != null)
			{
				CurrentCourse.Invalidate();
			}
			Orbit.InitFromCurrentStateVectors(Server.Instance.SolarSystem.CurrentTime);
		}
		if (this is SpaceObjectVessel)
		{
			(this as SpaceObjectVessel).SetPhysicsParameters();
		}
		if (CheckGravityInfluenceRadius())
		{
			if (CurrentCourse != null)
			{
				CurrentCourse.OrbitParentChanged();
			}
			Orbit.InitFromCurrentStateVectors(Server.Instance.SolarSystem.CurrentTime);
		}
		if (CheckPlanetDeath())
		{
			MarkForDestruction = true;
		}
	}

	public void UpdateStabilization()
	{
		if (StabilizeToTargetObj == null || !(this is Ship) || (this as Ship).IsDocked)
		{
			return;
		}
		if (CurrentCourse != null)
		{
			CurrentCourse.Invalidate();
		}
		Orbit.CopyDataFrom(StabilizeToTargetObj.Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
		Orbit.RelativePosition += StabilizeToTargetRelPosition;
		if (stabilizationDisableRelativePositionExtra.HasValue)
		{
			Orbit.RelativePosition += stabilizationDisableRelativePositionExtra.Value;
			stabilizationDisableRelativePositionExtra = null;
		}
		if (stabilizationDisableRelativeVelocityExtra.HasValue)
		{
			Orbit.RelativeVelocity += stabilizationDisableRelativeVelocityExtra.Value;
			stabilizationDisableRelativeVelocityExtra = null;
		}
		Orbit.InitFromCurrentStateVectors(Server.Instance.SolarSystem.CurrentTime);
		if (this is SpaceObjectVessel)
		{
			(this as SpaceObjectVessel).SetPhysicsParameters();
		}
		if (StabilizedToTargetChildren.Count <= 0)
		{
			return;
		}
		foreach (SpaceObjectVessel ab in StabilizedToTargetChildren)
		{
			ab.UpdateStabilization();
		}
	}

	private bool CheckCurrentCourse()
	{
		if (CurrentCourse == null || !CurrentCourse.IsValid)
		{
			return false;
		}
		if (CurrentCourse.IsActivated && !CurrentCourse.IsStartingSoonSent && CurrentCourse.StartSolarSystemTime > Server.Instance.SolarSystem.CurrentTime && Server.Instance.SolarSystem.CurrentTime >= CurrentCourse.StartSolarSystemTime - ManeuverCourse.StartingSoonTime)
		{
			CurrentCourse.SendCourseStartingSoonResponse();
		}
		else if (CurrentCourse.StartSolarSystemTime <= Server.Instance.SolarSystem.CurrentTime)
		{
			if (!CurrentCourse.IsActivated)
			{
				CurrentCourse.Invalidate();
				return false;
			}
			if (!CurrentCourse.IsInProgress)
			{
				if (!CurrentCourse.StartManeuver())
				{
					return false;
				}
				if (!CurrentCourse.IsStartingSoonSent)
				{
					CurrentCourse.SendCourseStartingSoonResponse();
				}
			}
			Vector3D relativePosition = Vector3D.Zero;
			Vector3D relativeVelocity = Vector3D.Zero;
			CurrentCourse.FillPositionAndVelocityAtCurrentTime(ref relativePosition, ref relativeVelocity);
			Orbit.RelativePosition = relativePosition;
			Orbit.RelativeVelocity = relativeVelocity;
			if (CurrentCourse.Type == ManeuverType.Engine || CurrentCourse.Type == ManeuverType.Transfer)
			{
				Vector3D right = Vector3D.Cross(Forward, Up);
				Forward = Vector3D.Lerp(Forward, Orbit.RelativeVelocity.Normalized, Server.Instance.DeltaTime).Normalized;
				Up = Vector3D.Cross(right, Forward);
			}
			if (CurrentCourse.EndSolarSystemTime <= Server.Instance.SolarSystem.CurrentTime)
			{
				CurrentCourse.SetFinalPosition();
				CurrentCourse.ReadNextManeuverCourse();
			}
			return true;
		}
		return false;
	}

	private void ApplyThrust(double timeDelta, ref Vector3D thrust)
	{
		if ((StabilizeToTargetObj != null || StabilizedToTargetChildren.Count > 0) && thrust.IsNotEpsilonZero(0.001))
		{
			if (StabilizeToTargetObj != null)
			{
				DisableStabilizationAfterUpdate(thrust * timeDelta, thrust);
			}
			else
			{
				foreach (SpaceObjectVessel ab in StabilizedToTargetChildren)
				{
					ab.DisableStabilizationAfterUpdate(-thrust * timeDelta, -thrust);
				}
			}
		}
		Orbit.RelativePosition += thrust * timeDelta;
		Orbit.RelativeVelocity += thrust;
		thrust = Vector3D.Zero;
	}

	private bool CheckThrustAndRotation(double timeDelta)
	{
		Vector3D prevAngularVelocity = Rotation;
		bool recalculateOrbit = false;
		if (this is Ship)
		{
			Ship sh = this as Ship;
			if (sh.CalculateEngineThrust(timeDelta))
			{
				recalculateOrbit = true;
				ApplyThrust(timeDelta, ref sh.EngineThrustVelocityDifference);
			}
			if (sh.CalculateRcsThrust(timeDelta))
			{
				recalculateOrbit = true;
				ApplyThrust(timeDelta, ref sh.RcsThrustVelocityDifference);
			}
			if (PhysicsVelocityDifference.IsNotEpsilonZero(0.001))
			{
				recalculateOrbit = true;
				ApplyThrust(timeDelta, ref PhysicsVelocityDifference);
			}
			if (sh.CalculateRotationThrust(timeDelta))
			{
				updateAngularVelocity = true;
				Rotation += sh.RotationThrustVelocityDifference;
				sh.RotationThrustVelocityDifference = Vector3D.Zero;
			}
			if (sh.CalculateRotationDampen(timeDelta))
			{
				updateAngularVelocity = true;
			}
			else if (sh.CalculateAutoStabilizeRotation(timeDelta))
			{
				updateAngularVelocity = true;
			}
			if (PhysicsRotationDifference.IsNotEpsilonZero(0.001))
			{
				updateAngularVelocity = true;
				Rotation += PhysicsRotationDifference;
				PhysicsRotationDifference = Vector3D.Zero;
			}
			if (updateAngularVelocity)
			{
				QuaternionD oldRotation2 = QuaternionD.LookRotation(Forward, Up);
				if (Rotation.IsNotEpsilonZero())
				{
					ApplyRotation(timeDelta);
				}
				AngularVelocity = (QuaternionD.LookRotation(Forward, Up) * oldRotation2.Inverse()).EulerAngles / timeDelta * (System.Math.PI / 180.0);
				updateAngularVelocity = false;
			}
			else if (Rotation.IsNotEpsilonZero())
			{
				ApplyRotation(timeDelta);
			}
			sh.CheckThrustStatsMessage();
		}
		else if (this is Asteroid)
		{
			if (PhysicsVelocityDifference.IsNotEpsilonZero(0.001))
			{
				recalculateOrbit = true;
				ApplyThrust(timeDelta, ref PhysicsVelocityDifference);
			}
			if (PhysicsRotationDifference.IsNotEpsilonZero(0.001))
			{
				updateAngularVelocity = true;
				Rotation += PhysicsRotationDifference;
				PhysicsRotationDifference = Vector3D.Zero;
			}
			if (updateAngularVelocity)
			{
				QuaternionD oldRotation = QuaternionD.LookRotation(Forward, Up);
				if (Rotation.IsNotEpsilonZero())
				{
					ApplyRotation(timeDelta);
				}
				AngularVelocity = (QuaternionD.LookRotation(Forward, Up) * oldRotation.Inverse()).EulerAngles / timeDelta * (System.Math.PI / 180.0);
				updateAngularVelocity = false;
			}
			else if (Rotation.IsNotEpsilonZero())
			{
				ApplyRotation(timeDelta);
			}
		}
		return recalculateOrbit;
	}

	private bool CheckGravityInfluenceRadius()
	{
		if (!double.IsInfinity(Orbit.Parent.GravityInfluenceRadiusSquared) && Orbit.RelativePosition.SqrMagnitude > Orbit.Parent.GravityInfluenceRadiusSquared)
		{
			Orbit.ChangeOrbitParent(Orbit.Parent.Parent);
			return true;
		}
		if (Orbit.Parent.CelestialBody.ChildBodies.Count > 0)
		{
			foreach (CelestialBody cb in Orbit.Parent.CelestialBody.ChildBodies)
			{
				if (Orbit.Position.DistanceSquared(cb.Position) < cb.Orbit.GravityInfluenceRadiusSquared)
				{
					Orbit.ChangeOrbitParent(cb.Orbit);
					return true;
				}
			}
		}
		return false;
	}

	protected virtual bool CheckPlanetDeath()
	{
		if (Orbit.RelativePosition.SqrMagnitude < System.Math.Pow(Orbit.Parent.Radius + Server.CelestialBodyDeathDistance, 2.0))
		{
			return true;
		}
		return false;
	}

	public void Update()
	{
		if (this is Ship)
		{
			if (!(this as Ship).IsMainVessel)
			{
				return;
			}
			(this as Ship).ExtraRcsThrustVelocityDifference = Vector3D.Zero;
			(this as Ship).ExtraRotationThrustVelocityDifference = Vector3D.Zero;
			foreach (Ship ship in (this as Ship).AllDockedVessels)
			{
				ship.UpdatePosition();
			}
			UpdatePosition();
		}
		else
		{
			UpdatePosition();
		}
	}

	public void AfterUpdate()
	{
		if (stabilizationDisableAfterUpdate.HasValue && stabilizationDisableAfterUpdate.Value)
		{
			DisableStabilization(disableForChildren: true, updateBeforeDisable: true);
		}
		else if (StabilizeToTargetObj != null)
		{
			UpdateStabilization();
		}
	}

	public bool StabilizeToTarget(SpaceObjectVessel vessel, bool forceStabilize = false)
	{
		if (!(this is SpaceObjectVessel) || vessel == null)
		{
			return false;
		}
		SpaceObjectVessel mainVessel = (this as SpaceObjectVessel).MainVessel;
		SpaceObjectVessel targetMainVessel = GetStabilizationTarget(vessel);
		if (targetMainVessel == mainVessel)
		{
			return false;
		}
		if (targetMainVessel.StabilizedToTargetChildren.Contains(this as SpaceObjectVessel))
		{
			return false;
		}
		if (!forceStabilize)
		{
			if (targetMainVessel.GUID != vessel.MainVessel.GUID)
			{
				if ((targetMainVessel.Velocity - mainVessel.Velocity).Magnitude > stabilizeToTargetMaxVelocityDiff || (vessel.MainVessel.Position - mainVessel.Position).Magnitude - vessel.MainVessel.Radius > stabilizeToTargetMaxPositionDiff)
				{
					return false;
				}
			}
			else if ((targetMainVessel.Velocity - mainVessel.Velocity).Magnitude > stabilizeToTargetMaxVelocityDiff || (targetMainVessel.Position - mainVessel.Position).Magnitude - targetMainVessel.Radius > stabilizeToTargetMaxPositionDiff)
			{
				return false;
			}
		}
		StabilizeToTargetObj = targetMainVessel;
		StabilizeToTargetRelPosition = Position - targetMainVessel.Position;
		targetMainVessel.StabilizedToTargetChildren.Add(this as SpaceObjectVessel);
		StabilizeToTargetTime = Server.Instance.SolarSystem.CurrentTime;
		return true;
	}

	private SpaceObjectVessel GetStabilizationTarget(SpaceObjectVessel vessel)
	{
		SpaceObjectVessel target = vessel.MainVessel;
		if (target.StabilizeToTargetObj != null)
		{
			target = GetStabilizationTarget(target.StabilizeToTargetObj);
		}
		return target;
	}

	public void DisableStabilization(bool disableForChildren, bool updateBeforeDisable)
	{
		if (!(this is SpaceObjectVessel))
		{
			return;
		}
		stabilizationDisableAfterUpdate = null;
		if (StabilizeToTargetObj != null)
		{
			if (updateBeforeDisable)
			{
				UpdateStabilization();
			}
			StabilizeToTargetObj.StabilizedToTargetChildren.Remove(this as SpaceObjectVessel);
			StabilizeToTargetObj = null;
			stabilizationDisabledTime = Server.Instance.SolarSystem.CurrentTime;
		}
		if (!disableForChildren || StabilizedToTargetChildren.Count <= 0)
		{
			return;
		}
		List<SpaceObjectVessel> reStabilizeChildren = new List<SpaceObjectVessel>(StabilizedToTargetChildren);
		int sanityCheck = 0;
		while (StabilizedToTargetChildren.Count > 0 && sanityCheck < 1000)
		{
			StabilizedToTargetChildren[0].DisableStabilization(disableForChildren: false, updateBeforeDisable);
			sanityCheck++;
		}
		if (sanityCheck >= 1000)
		{
			Dbg.Error("When disabling stabilization for", GUID, "children, sanity check reached", sanityCheck);
		}
		if (reStabilizeChildren.Count <= 1)
		{
			return;
		}
		SpaceObjectVessel newMain = null;
		foreach (SpaceObjectVessel ves in reStabilizeChildren)
		{
			if (newMain == null)
			{
				newMain = ves;
			}
			else
			{
				ves.StabilizeToTarget(newMain, forceStabilize: true);
			}
		}
	}

	public void DisableStabilizationAfterUpdate(Vector3D? relativePositionExtra, Vector3D? relativeVelocityExtra)
	{
		if (StabilizeToTargetObj != null)
		{
			stabilizationDisableAfterUpdate = true;
			stabilizationDisableRelativePositionExtra = relativePositionExtra;
			stabilizationDisableRelativeVelocityExtra = relativeVelocityExtra;
		}
	}

	public float GetCompoundRadarSignature()
	{
		if (this is SpaceObjectVessel)
		{
			return (this as SpaceObjectVessel).AllVessels.Sum((SpaceObjectVessel m) => m.RadarSignature);
		}
		return RadarSignature;
	}
}
