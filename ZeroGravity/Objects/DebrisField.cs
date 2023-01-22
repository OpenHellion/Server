using System;
using System.Collections.Generic;
using System.Linq;
using OpenHellion.Networking;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public class DebrisField
{
	[Serializable]
	public class FloatRange
	{
		public float Min;

		public float Max;
	}

	[Serializable]
	public class DebrisFieldData
	{
		public string CelestialBody;

		public string Name;

		public DebrisFieldType Type;

		public double Radius_Km;

		public double OrbitRadius_Km;

		public float Inclination_Deg;

		public float ArgumentOfPeriapsis_Deg;

		public float LongitudeOfAscendingNode_Deg;

		public float TrueAnomaly_Deg;

		public float SmallFragmentsDensity;

		public float SmallFragmentsVelocity;

		public float LargeFragmentsSpawnChance;

		public string[] LargeFragmentsScenes;

		public string LargeFragmentsTag;

		public FloatRange LargeFragmentsHealth;

		public FloatRange LargeFragmentsTimeToLive;

		public float LargeFragmentsSpawnDistance;

		public float LargeFragmentsVelocity;

		public float LargeFragmentsZoneRadius;

		public float DamageVesselChance;

		public FloatRange Damage;

		public float ScanningSensitivityMultiplier;

		public float RadarSignatureMultiplier;
	}

	public DebrisFieldData Data;

	public OrbitParameters Orbit;

	public List<GameScenes.SceneID> LargeFragmentsScenes = new List<GameScenes.SceneID>();

	private double radiusSq;

	private int steps;

	private static int maxSteps = 100;

	private static int[] indexes = Enumerable.Range(0, maxSteps).ToArray();

	private Queue<Delegate> SpawnQueue = new Queue<Delegate>();

	public DebrisField(DebrisFieldData data)
	{
		Data = data;
		Data.LargeFragmentsSpawnChance = MathHelper.Clamp(Data.LargeFragmentsSpawnChance, 0f, Data.DamageVesselChance);
		CelestialBodyGUID tmpCelBody = CelestialBodyGUID.Bethyr;
		Enum.TryParse<CelestialBodyGUID>(data.CelestialBody, out tmpCelBody);
		CelestialBody parent = Server.Instance.SolarSystem.GetCelestialBody((long)tmpCelBody);
		Orbit = new OrbitParameters();
		Orbit.InitFromPeriapisAndApoapsis(parent.Orbit, data.OrbitRadius_Km * 1000.0, data.OrbitRadius_Km * 1000.0, data.Inclination_Deg, data.ArgumentOfPeriapsis_Deg, data.LongitudeOfAscendingNode_Deg, data.TrueAnomaly_Deg, 0.0);
		if (data.LargeFragmentsScenes != null && data.LargeFragmentsScenes.Length != 0)
		{
			string[] largeFragmentsScenes = data.LargeFragmentsScenes;
			foreach (string s in largeFragmentsScenes)
			{
				if (Enum.TryParse<GameScenes.SceneID>(s, out var SceneId))
				{
					LargeFragmentsScenes.Add(SceneId);
				}
			}
		}
		radiusSq = data.Radius_Km * data.Radius_Km * 1000000.0;
		double vs = System.Math.PI * System.Math.Pow(data.Radius_Km * 1000.0, 3.0);
		double vo = Orbit.Circumference * System.Math.PI * System.Math.Pow(data.Radius_Km * 1000.0, 2.0);
		double i = vo / vs * (double)data.DamageVesselChance;
		if (i > (double)maxSteps)
		{
			Dbg.Warning("Debris field DamageVesselChance can't be achieved", data.Name, "Possible solutions: increase radius and/or scale down orbit.");
		}
		steps = (int)MathHelper.Clamp(System.Math.Ceiling(i), 1.0, maxSteps);
	}

	public DebrisFieldDetails GetDetails()
	{
		DebrisFieldDetails details = new DebrisFieldDetails
		{
			Name = Data.Name,
			Type = Data.Type,
			Radius = Data.Radius_Km * 1000.0,
			FragmentsDensity = Data.SmallFragmentsDensity,
			FragmentsVelocity = Data.SmallFragmentsVelocity,
			Orbit = new OrbitData(),
			ScanningSensitivityMultiplier = Data.ScanningSensitivityMultiplier,
			RadarSignatureMultiplier = Data.RadarSignatureMultiplier
		};
		Orbit.FillOrbitData(ref details.Orbit);
		return details;
	}

	public void CheckVessels(double time)
	{
		HashSet<SpaceObjectVessel> damageVessel = new HashSet<SpaceObjectVessel>();
		ArtificialBody[] artificialBodies = Server.Instance.SolarSystem.GetArtificialBodies();
		foreach (ArtificialBody ab in artificialBodies)
		{
			if (ab.Orbit.Parent != Orbit.Parent)
			{
				continue;
			}
			Orbit.GetOrbitPlaneData(out var planeRot, out var planeCenter);
			Vector3D relPos = ab.Position - (Orbit.Parent.Position + planeCenter);
			Vector3D proj = Vector3D.ProjectOnPlane(relPos, planeRot * Vector3D.Up);
			Vector3D posOnOrbit = proj.Normalized * Orbit.SemiMajorAxis;
			if ((posOnOrbit - relPos).SqrMagnitude <= radiusSq)
			{
				ab.IsInDebrisField = true;
				if ((double)Data.DamageVesselChance >= MathHelper.RandomNextDouble() && ab is SpaceObjectVessel)
				{
					damageVessel.Add(ab as SpaceObjectVessel);
				}
				if (!((double)Data.LargeFragmentsSpawnChance >= MathHelper.RandomNextDouble()))
				{
					continue;
				}
				Vector3D velocityDirection = Orbit.VelocityAtEccentricAnomaly(Vector3D.Angle(planeRot * Vector3D.Forward, proj.Normalized) * (System.Math.PI / 180.0), getRelativePosition: false).Normalized;
				Vector3D offset = -velocityDirection * Data.LargeFragmentsSpawnDistance;
				if (ab is Pivot)
				{
					Pivot pivot = ab as Pivot;
					if (pivot.Child is Player)
					{
						offset += (pivot.Child as Player).LocalPosition;
						SpawnQueue.Enqueue((Action)delegate
						{
							SpawnFragment(ab, velocityDirection, offset);
						});
					}
				}
				else
				{
					if (!(ab is SpaceObjectVessel) || Server.Instance.AllPlayers.Count((Player m) => m.Parent is SpaceObjectVessel && (m.Parent as SpaceObjectVessel).MainVessel == (ab as SpaceObjectVessel).MainVessel && m.IsAlive && Server.Instance.SolarSystem.CurrentTime - m.LastMovementMessageSolarSystemTime < 10.0) <= 0)
					{
						continue;
					}
					SpaceObjectVessel ves = (ab as SpaceObjectVessel).MainVessel;
					if (ves.AllDockedVessels.Count > 0 && MathHelper.RandomRange(0, ves.AllDockedVessels.Count + 1) > 0)
					{
						ves = ves.AllDockedVessels.OrderBy((SpaceObjectVessel m) => MathHelper.RandomNextDouble()).First();
					}
					SpawnQueue.Enqueue((Action)delegate
					{
						SpawnFragment(ves, velocityDirection, offset);
					});
				}
			}
			else
			{
				ab.IsInDebrisField = false;
			}
		}
		foreach (SpaceObjectVessel v in damageVessel)
		{
			ShipCollisionMessage scm = new ShipCollisionMessage
			{
				CollisionVelocity = 0f,
				ShipOne = v.GUID,
				ShipTwo = -1L
			};
			NetworkController.Instance.SendToClientsSubscribedTo(scm, -1L, v);
			v.ChangeHealthBy(0f - MathHelper.RandomRange(Data.Damage.Min, Data.Damage.Max), null, VesselRepairPoint.Priority.External, force: false, VesselDamageType.SmallDebrisHit, time);
		}
	}

	private void SpawnFragment(ArtificialBody ab, Vector3D velocityDirection, Vector3D offset)
	{
		Ship ship = Ship.CreateNewShip(LargeFragmentsScenes.OrderBy((GameScenes.SceneID m) => MathHelper.RandomNextDouble()).FirstOrDefault(), "", -1L, new List<long> { ab.GUID }, null, offset, null, null, Data.LargeFragmentsTag, checkPosition: false, 0.03, 0.3, null, 1.5, 100.0, MathHelper.RandomRange(Data.LargeFragmentsHealth.Min, Data.LargeFragmentsHealth.Max), isDebrisFragment: true);
		float ttl = MathHelper.RandomRange(Data.LargeFragmentsTimeToLive.Min, Data.LargeFragmentsTimeToLive.Max);
		ship.Health = ship.MaxHealth;
		foreach (VesselRepairPoint vrp in ship.RepairPoints)
		{
			vrp.Health = vrp.MaxHealth;
		}
		ship.Health = (float)((double)(ttl * ship.ExposureDamage) * Server.VesselDecayRateMultiplier);
		Vector3D thrust = velocityDirection * Data.LargeFragmentsVelocity;
		Vector3D offset2 = new Vector3D(MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5, MathHelper.RandomNextDouble() - 0.5) * Data.LargeFragmentsZoneRadius * 2.0;
		ship.Rotation = new Vector3D(MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble(), MathHelper.RandomNextDouble()) * 50.0;
		ship.Orbit.InitFromStateVectors(ship.Orbit.Parent, ship.Orbit.Position + offset2, ship.Orbit.Velocity + thrust, Server.Instance.SolarSystem.CurrentTime, areValuesRelative: false);
	}

	public void SpawnFragments()
	{
		while (SpawnQueue.Count > 0)
		{
			SpawnQueue.Dequeue().DynamicInvoke(null);
		}
	}
}
