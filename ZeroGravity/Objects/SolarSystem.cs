using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenHellion.Networking;
using OpenHellion;
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

	private double currentTime;

	private List<CelestialBody> celesitalBodies = new List<CelestialBody>();

	private ConcurrentDictionary<long, ArtificialBody> artificialBodies = new ConcurrentDictionary<long, ArtificialBody>();

	private List<Station> stations = new List<Station>();

	public bool CheckDestroyMarkedBodies;

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
		timeCorrection = HiResTime.Milliseconds / 1000.0 - time;
		foreach (CelestialBody body in celesitalBodies)
		{
			body.Update();
		}
	}

	public void UpdateTime(double timeDelta)
	{
		currentTime = HiResTime.Milliseconds / 1000.0 - timeCorrection;
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

	/// <summary>
	/// 	Send a message to the player that contains all of the moved objects.
	/// </summary>
	public void SendMovementMessageToPlayer(Player player)
	{
		MovementMessage movementMessage = new MovementMessage
		{
			SolarSystemTime = CurrentTime,
			Timestamp = (float)Server.Instance.RunTime.TotalSeconds,
			Transforms = new List<ObjectTransform>()
		};

		if (player.Parent is SpaceObjectVessel parent && parent.IsDocked)
		{
			parent.Orbit.CopyDataFrom(parent.DockedToMainVessel.Orbit, 0.0, exactCopy: true);
		}

		// Loops through each artificial body in the universe, and adds all contents to the movement message.
		foreach (ArtificialBody artificialBody in artificialBodies.Values)
		{
			ObjectTransform bodyTransform = new ObjectTransform
			{
				GUID = artificialBody.GUID,
				Type = artificialBody.ObjectType
			};

			if (artificialBody.CurrentCourse is not null && artificialBody.CurrentCourse.IsInProgress)
			{
				bodyTransform.Maneuver = artificialBody.CurrentCourse.CurrentData();
				bodyTransform.Forward = artificialBody.Forward.ToFloatArray();
				bodyTransform.Up = artificialBody.Up.ToFloatArray();
			}

			OrbitParameters orbit = artificialBody is not SpaceObjectVessel objectVessel ? artificialBody.Orbit : objectVessel.MainVessel.Orbit;
			if (artificialBody.StabilizeToTargetObj is not null)
			{
				if (artificialBody.StabilizeToTargetTime >= player.LastMovementMessageSolarSystemTime
					|| (player.UpdateArtificialBodyMovement.Count > 0 && player.UpdateArtificialBodyMovement.Contains(artificialBody.GUID)))
				{
					bodyTransform.StabilizeToTargetGUID = artificialBody.StabilizeToTargetObj.GUID;
					bodyTransform.StabilizeToTargetRelPosition = artificialBody.StabilizeToTargetRelPosition.ToArray();
				}
			}
			else if (orbit.IsOrbitValid)
			{
				if (orbit.LastChangeTime >= player.LastMovementMessageSolarSystemTime
					|| (player.UpdateArtificialBodyMovement.Count > 0 && player.UpdateArtificialBodyMovement.Contains(artificialBody.GUID)))
				{
					bodyTransform.Orbit = new OrbitData
					{
						ParentGUID = orbit.Parent.CelestialBody.GUID
					};
					orbit.FillOrbitData(ref bodyTransform.Orbit);
				}
			}
			else
			{
				bodyTransform.Realtime = new RealtimeData
				{
					ParentGUID = orbit.Parent.CelestialBody.GUID,
					Position = orbit.RelativePosition.ToArray(),
					Velocity = orbit.RelativeVelocity.ToArray()
				};
			}

			if (bodyTransform.Orbit is not null || bodyTransform.Realtime is not null || (artificialBody.LastOrientationChangeTime >= player.LastMovementMessageSolarSystemTime && (player.Parent == artificialBody || (player.Parent as ArtificialBody).Position.DistanceSquared(artificialBody.Position) < 225000000.0)))
			{
				bodyTransform.Forward = artificialBody.Forward.ToFloatArray();
				bodyTransform.Up = artificialBody.Up.ToFloatArray();
				bodyTransform.AngularVelocity = (artificialBody.AngularVelocity * (180.0 / System.Math.PI)).ToFloatArray();
				bodyTransform.RotationVec = artificialBody.Rotation.ToFloatArray();
			}

			bodyTransform.CharactersMovement = new List<CharacterMovementMessage>();
			bodyTransform.DynamicObjectsMovement = new List<DynamicObectMovementMessage>();
			bodyTransform.CorpsesMovement = new List<CorpseMovementMessage>();

			if (artificialBody is SpaceObjectVessel vessel)
			{
				if (player.Parent.GUID == vessel.GUID || player.IsSubscribedTo(vessel.GUID))
				{
					foreach (Player crewPlayer in vessel.VesselCrew)
					{
						if (crewPlayer.PlayerReady)
						{
							CharacterMovementMessage characterMovement = crewPlayer.GetCharacterMovementMessage();
							if (characterMovement is not null)
							{
								bodyTransform.CharactersMovement.Add(characterMovement);
							}
						}
					}

					foreach (Corpse playerCorpse in vessel.Corpses.Values)
					{
						if (playerCorpse.PlayerReceivesMovementMessage(player.GUID))
						{
							CorpseMovementMessage corpseMovement = playerCorpse.GetMovementMessage();
							if (corpseMovement is not null)
							{
								bodyTransform.CorpsesMovement.Add(corpseMovement);
							}
						}
					}

					foreach (DynamicObject dynamicObject in vessel.DynamicObjects.Values)
					{
						if (dynamicObject.PlayerReceivesMovementMessage(player.GUID) && dynamicObject.LastChangeTime >= player.LastMovementMessageSolarSystemTime)
						{
							DynamicObectMovementMessage dynamicOjectMovement = dynamicObject.GetDynamicObectMovementMessage();
							if (dynamicOjectMovement is not null)
							{
								bodyTransform.DynamicObjectsMovement.Add(dynamicOjectMovement);
							}
						}
					}
				}
			}
			else if (artificialBody is Pivot pivot)
			{
				switch (pivot.ObjectType)
				{
					case SpaceObjectType.PlayerPivot:
					{
						Player character = pivot.Child as Player;
						CharacterMovementMessage characterMovement = character.GetCharacterMovementMessage();
						if (characterMovement is not null)
						{
							bodyTransform.CharactersMovement.Add(characterMovement);
						}
						break;
					}

					case SpaceObjectType.CorpsePivot:
					{
						Corpse playerCorpse = pivot.Child as Corpse;
						if (playerCorpse.PlayerReceivesMovementMessage(player.GUID))
						{
							CorpseMovementMessage corpseMovement = playerCorpse.GetMovementMessage();
							if (corpseMovement is not null)
							{
								bodyTransform.CorpsesMovement.Add(corpseMovement);
							}
						}
						break;
					}

					case SpaceObjectType.DynamicObjectPivot:
					{
						DynamicObject dynamicObject = pivot.Child as DynamicObject;
						if (dynamicObject.PlayerReceivesMovementMessage(player.GUID))
						{
							DynamicObectMovementMessage dynamicObjectMovement = dynamicObject.GetDynamicObectMovementMessage();
							if (dynamicObjectMovement is not null)
							{
								bodyTransform.DynamicObjectsMovement.Add(dynamicObjectMovement);
							}
						}
						break;
					}
				}
			}

			if (bodyTransform.Orbit == null && bodyTransform.Realtime == null && bodyTransform.Maneuver == null && bodyTransform.Forward == null && bodyTransform.CharactersMovement.Count <= 0)
			{
				List<CorpseMovementMessage> corpsesMovement = bodyTransform.CorpsesMovement;
				if (corpsesMovement == null || corpsesMovement.Count <= 0)
				{
					List<DynamicObectMovementMessage> dynamicObjectsMovement = bodyTransform.DynamicObjectsMovement;
					if ((dynamicObjectsMovement == null || dynamicObjectsMovement.Count <= 0) && !bodyTransform.StabilizeToTargetGUID.HasValue)
					{
						continue;
					}
				}
			}

			if (artificialBody.StabilizeToTargetObj is not null)
			{
				movementMessage.Transforms.Add(bodyTransform);
			}
			else
			{
				movementMessage.Transforms.Insert(0, bodyTransform);
			}
		}

		player.LastMovementMessageSolarSystemTime = CurrentTime;
		player.UpdateArtificialBodyMovement.Clear();
		NetworkController.Instance.SendToGameClient(player.GUID, movementMessage);
	}

	public void InitializeData()
	{
		foreach (CelestialBodyData cbd in StaticData.SolarSystem.CelestialBodies)
		{
			CelestialBody newBody = new CelestialBody(cbd.GUID);
			newBody.Set(cbd.ParentGUID == -1 ? null : GetCelestialBody(cbd.ParentGUID), cbd.Mass, cbd.Radius * Server.CelestialBodyRadiusMultiplier, cbd.RotationPeriod, cbd.Eccentricity, cbd.SemiMajorAxis, cbd.Inclination, cbd.ArgumentOfPeriapsis, cbd.LongitudeOfAscendingNode, CurrentTime);
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
			SpaceObject so = nearArtificialBodyGUIDs.Count != 1 ? Server.Instance.GetObject(nearArtificialBodyGUIDs[MathHelper.RandomRange(0, nearArtificialBodyGUIDs.Count)]) : Server.Instance.GetObject(nearArtificialBodyGUIDs[0]);
			if (so is ArtificialBody body)
			{
				ab = body;
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
				parentBody = celestialBodyGUIDs.Count != 1 ? Server.Instance.SolarSystem.GetCelestialBody(celestialBodyGUIDs[MathHelper.RandomRange(0, celestialBodyGUIDs.Count)]) : Server.Instance.SolarSystem.GetCelestialBody(celestialBodyGUIDs[0]);
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
					velocityAtPosition = !(tangent1.SqrMagnitude > tangent2.SqrMagnitude) ? new Vector3D?(tangent2.Normalized * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(position.Magnitude)) : new Vector3D?(tangent1.Normalized * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(position.Magnitude));
				}
				velocity = velocityAtPosition.Value;
			}
			else
			{
				double distance = 0.0;
				distance = parentBody.GUID != 1 ? parentBody.Orbit.Radius + (parentBody.Orbit.GravityInfluenceRadius - parentBody.Orbit.Radius) * MathHelper.RandomRange(distanceFromSurfacePercMin, distanceFromSurfacePercMax) : parentBody.Orbit.Radius + (483940704314.0 - parentBody.Orbit.Radius) * MathHelper.RandomRange(0.1, 1.0);
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
