using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public class SolarSystem
{
	public const double VisibilityLimitDestroySqr = 225000000.0;

	public const double VisibilityLimitLoadSqr = 100000000.0;

	public const double DetailsLimitUnsubscribe = 6250000.0;

	public const double DetailsLimitSubscribe = 2250000.0;

	private double currentTime = 0.0;

	private List<CelestialBody> celesitalBodies = new List<CelestialBody>();

	private ConcurrentDictionary<long, ArtificialBody> artificialBodies = new ConcurrentDictionary<long, ArtificialBody>();

	private List<Station> stations = new List<Station>();

	public bool CheckDestroyMarkedBodies = false;

	private double timeCorrection;

	public double CurrentTime => currentTime;

	public int ArtificialBodiesCount => artificialBodies.Count;

	public void AddCelestialBody(CelestialBody body)
	{
		celesitalBodies.Add(body);
	}

	public CelestialBody GetCelestialBody(long guid)
	{
		return celesitalBodies.Find((CelestialBody m) => m.GUID == guid);
	}

	public CelestialBody FindCelestialBodyParent(Vector3D position)
	{
		CelestialBody foundBody = celesitalBodies[0];
		double currMinDistance = (celesitalBodies[0].Position - position).SqrMagnitude;
		for (int i = 1; i < celesitalBodies.Count; i++)
		{
			double tmpDistance = (celesitalBodies[i].Position - position).SqrMagnitude;
			if (tmpDistance < celesitalBodies[i].Orbit.GravityInfluenceRadiusSquared && tmpDistance < currMinDistance)
			{
				foundBody = celesitalBodies[i];
				currMinDistance = tmpDistance;
			}
		}
		return foundBody;
	}

	public void AddArtificialBody(ArtificialBody body)
	{
		artificialBodies[body.GUID] = body;
	}

	public void RemoveArtificialBody(ArtificialBody body)
	{
		artificialBodies.TryRemove(body.GUID, out body);
	}

	public void CalculatePositionsAfterTime(double time)
	{
		currentTime = time;
		timeCorrection = (double)HiResTime.Milliseconds / 1000.0 - time;
		foreach (CelestialBody body in celesitalBodies)
		{
			body.Update();
		}
	}

	public void UpdateTime(double timeDelta)
	{
		currentTime = (double)HiResTime.Milliseconds / 1000.0 - timeCorrection;
	}

	public void UpdatePositions()
	{
		try
		{
			foreach (CelestialBody body in celesitalBodies)
			{
				body.Update();
			}
			List<ArtificialBody> abs = new List<ArtificialBody>(artificialBodies.Values);
			Parallel.ForEach(abs, delegate(ArtificialBody ab)
			{
				try
				{
					ab.Update();
				}
				catch (Exception ex3)
				{
					Dbg.Exception(ex3);
				}
			});
			Parallel.ForEach(abs, delegate(ArtificialBody ab)
			{
				try
				{
					ab.AfterUpdate();
				}
				catch (Exception ex2)
				{
					Dbg.Exception(ex2);
				}
			});
			if (!CheckDestroyMarkedBodies)
			{
				return;
			}
			foreach (ArtificialBody ab2 in abs.Where((ArtificialBody m) => m.MarkForDestruction))
			{
				Server.Instance.DestroyArtificialBody(ab2);
			}
			CheckDestroyMarkedBodies = false;
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	public void SendMovementMessage()
	{
		try
		{
			Player[] players = Server.Instance.AllPlayers.Where((Player m) => m.EnvironmentReady && m.IsAlive).ToArray();
			if (players.Length < 1)
			{
				return;
			}
			EventWaitHandle wh = new AutoResetEvent(initialState: false);

			// Create cancellation token.
			CancellationTokenSource cts = new();

			Task t = Task.Factory.StartNew(() =>
			{
				ParallelLoopResult parallelLoopResult = Parallel.ForEach(players, delegate(Player pl)
				{
					SendMovementMessageToPlayer(pl);
				});
				wh.Set();
			}, cts.Token);


			if (!wh.WaitOne(10000))
			{
				Dbg.Warning("SendMovementMessage thread timeout. Aborting...");

				// Request cancellation.
				cts.Cancel();

				// Clean up.
				cts.Dispose();
			}
			foreach (Player kv in Server.Instance.AllPlayers)
			{
				kv.TransformData = null;
			}
		}
		finally
		{
		}
	}

	public void SendMovementMessageToPlayer(Player pl)
	{
		MovementMessage mm = new MovementMessage();
		mm.SolarSystemTime = CurrentTime;
		mm.Timestamp = (float)Server.Instance.RunTime.TotalSeconds;
		mm.Transforms = new List<ObjectTransform>();
		if (pl.Parent is SpaceObjectVessel && (pl.Parent as SpaceObjectVessel).IsDocked)
		{
			SpaceObjectVessel vessel = pl.Parent as SpaceObjectVessel;
			vessel.Orbit.CopyDataFrom(vessel.DockedToMainVessel.Orbit, 0.0, exactCopy: true);
		}
		foreach (ArtificialBody ab in artificialBodies.Values)
		{
			ObjectTransform trans = new ObjectTransform
			{
				GUID = ab.GUID,
				Type = ab.ObjectType
			};
			if (ab.CurrentCourse != null && ab.CurrentCourse.IsInProgress)
			{
				trans.Maneuver = ab.CurrentCourse.CurrentData();
				trans.Forward = ab.Forward.ToFloatArray();
				trans.Up = ab.Up.ToFloatArray();
			}
			OrbitParameters orbit = ((!(ab is SpaceObjectVessel)) ? ab.Orbit : (ab as SpaceObjectVessel).MainVessel.Orbit);
			if (ab.StabilizeToTargetObj != null)
			{
				if (ab.StabilizeToTargetTime >= pl.LastMovementMessageSolarSystemTime || (pl.UpdateArtificialBodyMovement.Count > 0 && pl.UpdateArtificialBodyMovement.Contains(ab.GUID)))
				{
					trans.StabilizeToTargetGUID = ab.StabilizeToTargetObj.GUID;
					trans.StabilizeToTargetRelPosition = ab.StabilizeToTargetRelPosition.ToArray();
				}
			}
			else if (orbit.IsOrbitValid)
			{
				if (orbit.LastChangeTime >= pl.LastMovementMessageSolarSystemTime || (pl.UpdateArtificialBodyMovement.Count > 0 && pl.UpdateArtificialBodyMovement.Contains(ab.GUID)))
				{
					trans.Orbit = new OrbitData
					{
						ParentGUID = orbit.Parent.CelestialBody.GUID
					};
					orbit.FillOrbitData(ref trans.Orbit);
				}
			}
			else
			{
				trans.Realtime = new RealtimeData
				{
					ParentGUID = orbit.Parent.CelestialBody.GUID,
					Position = orbit.RelativePosition.ToArray(),
					Velocity = orbit.RelativeVelocity.ToArray()
				};
			}
			if (trans.Orbit != null || trans.Realtime != null || (ab.LastOrientationChangeTime >= pl.LastMovementMessageSolarSystemTime && (pl.Parent == ab || (pl.Parent as ArtificialBody).Position.DistanceSquared(ab.Position) < 225000000.0)))
			{
				trans.Forward = ab.Forward.ToFloatArray();
				trans.Up = ab.Up.ToFloatArray();
				trans.AngularVelocity = (ab.AngularVelocity * (180.0 / System.Math.PI)).ToFloatArray();
				trans.RotationVec = ab.Rotation.ToFloatArray();
			}
			trans.CharactersMovement = new List<CharacterMovementMessage>();
			trans.DynamicObjectsMovement = new List<DynamicObectMovementMessage>();
			trans.CorpsesMovement = new List<CorpseMovementMessage>();
			if (ab is SpaceObjectVessel)
			{
				SpaceObjectVessel ves = ab as SpaceObjectVessel;
				if (pl.Parent.GUID == ab.GUID || pl.IsSubscribedTo(ab.GUID))
				{
					foreach (Player crewPl in ves.VesselCrew)
					{
						if (crewPl.PlayerReady)
						{
							CharacterMovementMessage cmm2 = crewPl.GetCharacterMovementMessage();
							if (cmm2 != null)
							{
								trans.CharactersMovement.Add(cmm2);
							}
						}
					}
					foreach (DynamicObject dobj2 in ves.DynamicObjects.Values)
					{
						if (dobj2.PlayerReceivesMovementMessage(pl.GUID) && dobj2.LastChangeTime >= pl.LastMovementMessageSolarSystemTime)
						{
							DynamicObectMovementMessage mdom2 = dobj2.GetDynamicObectMovementMessage();
							if (mdom2 != null)
							{
								trans.DynamicObjectsMovement.Add(mdom2);
							}
						}
					}
					foreach (Corpse cor2 in ves.Corpses.Values)
					{
						if (cor2.PlayerReceivesMovementMessage(pl.GUID))
						{
							CorpseMovementMessage mcom2 = cor2.GetMovementMessage();
							if (mcom2 != null)
							{
								trans.CorpsesMovement.Add(mcom2);
							}
						}
					}
				}
			}
			else if (ab is Pivot)
			{
				Pivot pivot = ab as Pivot;
				if (pivot.ObjectType == SpaceObjectType.PlayerPivot)
				{
					CharacterMovementMessage cmm = ((Player)pivot.Child).GetCharacterMovementMessage();
					if (cmm != null)
					{
						trans.CharactersMovement.Add(cmm);
					}
				}
				else if (pivot.ObjectType == SpaceObjectType.CorpsePivot)
				{
					Corpse cor = pivot.Child as Corpse;
					if (cor.PlayerReceivesMovementMessage(pl.GUID))
					{
						CorpseMovementMessage mcom = cor.GetMovementMessage();
						if (mcom != null)
						{
							trans.CorpsesMovement.Add(mcom);
						}
					}
				}
				else if (pivot.ObjectType == SpaceObjectType.DynamicObjectPivot)
				{
					DynamicObject dobj = pivot.Child as DynamicObject;
					if (dobj.PlayerReceivesMovementMessage(pl.GUID))
					{
						DynamicObectMovementMessage mdom = dobj.GetDynamicObectMovementMessage();
						if (mdom != null)
						{
							trans.DynamicObjectsMovement.Add(mdom);
						}
					}
				}
			}
			if (trans.Orbit == null && trans.Realtime == null && trans.Maneuver == null && trans.Forward == null && trans.CharactersMovement.Count <= 0)
			{
				List<CorpseMovementMessage> corpsesMovement = trans.CorpsesMovement;
				if (corpsesMovement == null || corpsesMovement.Count <= 0)
				{
					List<DynamicObectMovementMessage> dynamicObjectsMovement = trans.DynamicObjectsMovement;
					if ((dynamicObjectsMovement == null || dynamicObjectsMovement.Count <= 0) && !trans.StabilizeToTargetGUID.HasValue)
					{
						continue;
					}
				}
			}
			if (ab.StabilizeToTargetObj != null)
			{
				mm.Transforms.Add(trans);
			}
			else
			{
				mm.Transforms.Insert(0, trans);
			}
		}
		pl.LastMovementMessageSolarSystemTime = CurrentTime;
		pl.UpdateArtificialBodyMovement.Clear();
		Server.Instance.NetworkController.SendToGameClient(pl.GUID, mm);
	}

	public void InitializeData()
	{
		foreach (CelestialBodyData cbd in StaticData.SolarSystem.CelestialBodies)
		{
			CelestialBody newBody = new CelestialBody(cbd.GUID);
			newBody.Set((cbd.ParentGUID == -1) ? null : GetCelestialBody(cbd.ParentGUID), cbd.Mass, cbd.Radius * Server.CELESTIAL_BODY_RADIUS_MULTIPLIER, cbd.RotationPeriod, cbd.Eccentricity, cbd.SemiMajorAxis, cbd.Inclination, cbd.ArgumentOfPeriapsis, cbd.LongitudeOfAscendingNode, CurrentTime);
			newBody.AsteroidGasBurstTimeMin = cbd.AsteroidGasBurstTimeMin;
			newBody.AsteroidGasBurstTimeMax = cbd.AsteroidGasBurstTimeMax;
			newBody.AsteroidResources = cbd.AsteroidResources.ToList();
			celesitalBodies.Add(newBody);
		}
	}

	public ArtificialBody[] GetArtificialBodies()
	{
		return artificialBodies.Values.ToArray();
	}

	public List<CelestialBody> GetCelestialBodies()
	{
		return celesitalBodies;
	}

	public ArtificialBody[] GetArtificialBodieslsInRange(ArtificialBody ab, double radius)
	{
		double sqRadius = radius * radius;
		return artificialBodies.Values.Where((ArtificialBody m) => m != ab && m.Parent == ab.Parent && (m.Position - ab.Position).SqrMagnitude <= sqRadius).ToArray();
	}

	public ArtificialBody[] GetArtificialBodieslsInRange(Vector3D position, double radius)
	{
		double sqRadius = radius * radius;
		return artificialBodies.Values.Where((ArtificialBody m) => (m.Position - position).SqrMagnitude <= sqRadius).ToArray();
	}

	public ArtificialBody[] GetArtificialBodieslsInRange(CelestialBody celestial, Vector3D position, double radius)
	{
		double sqRadius = radius * radius;
		return artificialBodies.Values.Where((ArtificialBody m) => m.Orbit.Parent.CelestialBody == celestial && (m.Orbit.RelativePosition - position).SqrMagnitude <= sqRadius).ToArray();
	}

	public void GetSpawnPosition(SpaceObjectType type, double objectRadius, bool checkPosition, out Vector3D position, out Vector3D velocity, out Vector3D forward, out Vector3D up, List<long> nearArtificialBodyGUIDs, List<long> celestialBodyGUIDs, Vector3D? positionOffset, Vector3D? velocityAtPosition, QuaternionD? localRotation, double distanceFromSurfacePercMin, double distanceFromSurfacePercMax, SpawnRuleOrbit spawnRuleOrbit, double celestialBodyDeathDistanceMultiplier, double artificialBodyDistanceCheck, out OrbitParameters orbit)
	{
		position = Vector3D.Zero;
		velocity = Vector3D.Zero;
		forward = Vector3D.Forward;
		up = Vector3D.Up;
		orbit = null;
		CelestialBody parentBody = null;
		ArtificialBody ab = null;
		if (nearArtificialBodyGUIDs != null && nearArtificialBodyGUIDs.Count > 0)
		{
			SpaceObject so = ((nearArtificialBodyGUIDs.Count != 1) ? Server.Instance.GetObject(nearArtificialBodyGUIDs[MathHelper.RandomRange(0, nearArtificialBodyGUIDs.Count)]) : Server.Instance.GetObject(nearArtificialBodyGUIDs[0]));
			if (so is ArtificialBody)
			{
				ab = so as ArtificialBody;
			}
			else if (so is Player)
			{
				ab = so.Parent as ArtificialBody;
			}
			if (ab != null)
			{
				parentBody = ab.Orbit.Parent.CelestialBody;
				position = ab.Orbit.RelativePosition + (positionOffset.HasValue ? positionOffset.Value : Vector3D.Zero);
				velocity = ab.Orbit.RelativeVelocity;
				if (position.SqrMagnitude > parentBody.Orbit.GravityInfluenceRadiusSquared * 0.9)
				{
					Vector3D.ClampMagnitude(position, parentBody.Orbit.GravityInfluenceRadiusSquared * 0.9);
				}
				if (localRotation.HasValue)
				{
					forward = localRotation.Value * Vector3D.Forward;
					up = localRotation.Value * Vector3D.Up;
				}
			}
		}
		if (parentBody == null && spawnRuleOrbit != null)
		{
			parentBody = GetCelestialBody((long)spawnRuleOrbit.CelestialBody);
			orbit = spawnRuleOrbit.GenerateRandomOrbit(parentBody);
			position = orbit.RelativePosition;
			velocity = orbit.RelativeVelocity;
			if (localRotation.HasValue)
			{
				forward = localRotation.Value * Vector3D.Forward;
				up = localRotation.Value * Vector3D.Up;
			}
		}
		if (parentBody == null)
		{
			if (celestialBodyGUIDs != null && celestialBodyGUIDs.Count > 0)
			{
				parentBody = ((celestialBodyGUIDs.Count != 1) ? Server.Instance.SolarSystem.GetCelestialBody(celestialBodyGUIDs[MathHelper.RandomRange(0, celestialBodyGUIDs.Count)]) : Server.Instance.SolarSystem.GetCelestialBody(celestialBodyGUIDs[0]));
			}
			if (parentBody == null)
			{
				parentBody = Server.Instance.SolarSystem.GetCelestialBody(MathHelper.RandomRange(1, 20));
			}
			if (positionOffset.HasValue)
			{
				position = positionOffset.Value + positionOffset.Value.Normalized * parentBody.Orbit.Radius;
				if (parentBody.GUID == 1 && position.SqrMagnitude > 897587224200.0)
				{
					Vector3D.ClampMagnitude(position, parentBody.Orbit.GravityInfluenceRadiusSquared * 0.9);
				}
				else if (parentBody.GUID != 1 && position.SqrMagnitude > parentBody.Orbit.GravityInfluenceRadiusSquared * 0.9)
				{
					Vector3D.ClampMagnitude(position, parentBody.Orbit.GravityInfluenceRadiusSquared * 0.9);
				}
				if (!velocityAtPosition.HasValue)
				{
					Vector3D tangent1 = Vector3D.Cross(position.Normalized, Vector3D.Forward);
					Vector3D tangent2 = Vector3D.Cross(position.Normalized, Vector3D.Up);
					velocityAtPosition = ((!(tangent1.SqrMagnitude > tangent2.SqrMagnitude)) ? new Vector3D?(tangent2.Normalized * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(position.Magnitude)) : new Vector3D?(tangent1.Normalized * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(position.Magnitude)));
				}
				velocity = velocityAtPosition.Value;
			}
			else
			{
				double distance = 0.0;
				distance = ((parentBody.GUID != 1) ? (parentBody.Orbit.Radius + (parentBody.Orbit.GravityInfluenceRadius - parentBody.Orbit.Radius) * MathHelper.RandomRange(distanceFromSurfacePercMin, distanceFromSurfacePercMax)) : (parentBody.Orbit.Radius + (483940704314.0 - parentBody.Orbit.Radius) * MathHelper.RandomRange(0.1, 1.0)));
				position = new Vector3D(0.0 - distance, 0.0, 0.0);
				velocity = Vector3D.Back * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(distance);
				QuaternionD randomRot2 = MathHelper.RandomRotation();
				position = randomRot2 * position;
				velocity = randomRot2 * velocity;
			}
			if (localRotation.HasValue)
			{
				forward = localRotation.Value * Vector3D.Forward;
				up = localRotation.Value * Vector3D.Up;
			}
			else
			{
				QuaternionD randomRot = MathHelper.RandomRotation();
				forward = randomRot * Vector3D.Forward;
				up = randomRot * Vector3D.Up;
			}
		}
		double rotatePivotAngle = -100.0 / position.Magnitude * (180.0 / System.Math.PI);
		position += parentBody.Position;
		int positionIteration = 0;
		if (checkPosition)
		{
			int spawnPointClear = 0;
			do
			{
				spawnPointClear = 0;
				foreach (CelestialBody cb in celesitalBodies)
				{
					if (cb.Orbit.IsOrbitValid && cb.GUID != 1 && cb.Position.DistanceSquared(position) < System.Math.Pow(cb.Orbit.Radius + Server.CelestialBodyDeathDistance * celestialBodyDeathDistanceMultiplier + objectRadius, 2.0))
					{
						spawnPointClear = 2;
						break;
					}
				}
				if (spawnPointClear == 0)
				{
					foreach (SpaceObjectVessel tmp in Server.Instance.AllVessels)
					{
						if (!tmp.IsDocked && tmp.Position.DistanceSquared(position) < System.Math.Pow(tmp.Radius + objectRadius + artificialBodyDistanceCheck, 2.0))
						{
							spawnPointClear = 1;
							break;
						}
					}
				}
				if (spawnPointClear != 0)
				{
					if (spawnRuleOrbit != null && positionIteration < 20)
					{
						OrbitParameters orb = spawnRuleOrbit.GenerateRandomOrbit(parentBody);
						position = orb.Position;
						velocity = orb.RelativeVelocity;
					}
					if (ab != null && spawnPointClear == 1 && positionIteration < 80)
					{
						position = MathHelper.RotateAroundPivot(position, parentBody.Position, new Vector3D(0.0, rotatePivotAngle, 0.0));
						velocity = MathHelper.RotateAroundPivot(velocity, Vector3D.Zero, new Vector3D(0.0, rotatePivotAngle, 0.0));
					}
					else
					{
						Vector3D randExtraRot = new Vector3D(MathHelper.RandomRange(0.0, 359.99), MathHelper.RandomRange(0.0, 359.99), MathHelper.RandomRange(0.0, 359.99));
						position = MathHelper.RotateAroundPivot(position, parentBody.Position, randExtraRot);
						velocity = MathHelper.RotateAroundPivot(velocity, Vector3D.Zero, randExtraRot);
					}
				}
				positionIteration++;
			}
			while (spawnPointClear != 0 && positionIteration < 100);
		}
		velocity += parentBody.Velocity;
	}
}
