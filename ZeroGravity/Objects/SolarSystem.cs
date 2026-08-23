using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenHellion.Net;
using OpenHellion;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Spawn;
using OpenHellion.Net.Message;

namespace ZeroGravity.Objects;

public class SolarSystem
{
	public const double ViewRadius = 10000.0; // in meters

	private double _currentTime;

	private List<CelestialBody> _celestialBodies = new List<CelestialBody>();

	public bool CheckDestroyMarkedBodies;

	private double _timeCorrection;

	public double CurrentTime => _currentTime;

	public int ArtificialBodiesCount => Server.Instance.SpaceObjects.Values.OfType<ArtificialBody>().Count();

	public CelestialBody GetCelestialBody(long guid)
	{
		return _celestialBodies.Find((CelestialBody m) => m.GUID == guid);
	}

	public CelestialBody FindCelestialBodyParent(Vector3D position)
	{
		CelestialBody foundBody = _celestialBodies[0];
		double currMinDistance = (_celestialBodies[0].Position - position).SqrMagnitude;
		for (int i = 1; i < _celestialBodies.Count; i++)
		{
			double tmpDistance = (_celestialBodies[i].Position - position).SqrMagnitude;
			if (tmpDistance < _celestialBodies[i].Orbit.GravityInfluenceRadiusSquared && tmpDistance < currMinDistance)
			{
				foundBody = _celestialBodies[i];
				currMinDistance = tmpDistance;
			}
		}
		return foundBody;
	}

	public void AddArtificialBody(ArtificialBody body)
	{
		Server.Instance.SpaceObjects.TryRemove(body.Guid, out _);
		Server.Instance.SpaceObjects.TryAdd(body.Guid, body, body.Position);
	}

	public void RemoveArtificialBody(ArtificialBody body)
	{
		Server.Instance.SpaceObjects.TryRemove(body.Guid, out _);
		if (body is Pivot { Child: not null } pivot)
		{
			Server.Instance.SpaceObjects.TryAdd(pivot.Guid, pivot.Child);
		}
	}

	public void CalculatePositionsAfterTime(double time)
	{
		_currentTime = time;
		_timeCorrection = HiResTime.Milliseconds / 1000.0 - time;
		foreach (CelestialBody body in _celestialBodies)
		{
			body.Update();
		}
	}

	public void UpdateTime(double timeDelta)
	{
		_currentTime = HiResTime.Milliseconds / 1000.0 - _timeCorrection;
	}

	public async Task UpdatePositions()
	{
		foreach (CelestialBody body in _celestialBodies)
		{
			body.Update();
		}
		List<ArtificialBody> artificialBodies = [.. Server.Instance.SpaceObjects.Values.OfType<ArtificialBody>()];
		await Parallel.ForEachAsync(artificialBodies, async (ab, ct) =>
		{
			await ab.Update();
		});
		await Parallel.ForEachAsync(artificialBodies, async (ArtificialBody ab, CancellationToken _) =>
		{
			await ab.AfterUpdate();
			if (ab is SpaceObjectVessel { IsDocked: true })
			{
				Server.Instance.SpaceObjects.ClearPosition(ab.Guid);
			}
			else
			{
				Server.Instance.SpaceObjects.SetPosition(ab.Guid, ab.Position);
			}
		});
		if (CheckDestroyMarkedBodies)
		{
			foreach (ArtificialBody ab2 in artificialBodies.Where((ArtificialBody m) => m.MarkForDestruction))
			{
				await Server.Instance.DestroyArtificialBody(ab2);
			}
			CheckDestroyMarkedBodies = false;
		}
	}

	/// <summary>
	/// 	Get the guids of a list of bodies and their child objects (commonly docked vessels).
	/// </summary>
	/// <remarks>
	/// 	Dynamic objects owned by a player, a corpse or another dyamic object is sent elsewhere.
	/// </remarks>
	public HashSet<long> GetChildrenOfBodies(List<ArtificialBody> bodies)
	{
		HashSet<long> children = [];
		foreach (ArtificialBody body in bodies)
		{
			children.Add(body.Guid);
			if (body is SpaceObjectVessel vessel)
			{
				foreach (SpaceObjectVessel memberVessel in vessel.AllVessels)
				{
					children.Add(memberVessel.Guid);
					children.UnionWith(memberVessel.DynamicObjects);
					children.UnionWith(memberVessel.Corpses);
					foreach (Player crewPlayer in memberVessel.VesselCrew)
					{
						children.Add(crewPlayer.FakeGuid);
					}
				}
			}
			else if (body is Pivot { Child: Player pivotPlayer })
			{
				children.Add(pivotPlayer.FakeGuid);
			}
		}

		return children;
	}

	public HashSet<long> BuildPlayerView(Player player, ArtificialBody mustInclude,
		List<ArtificialBody> bodiesInRange)
	{
		if (mustInclude != null && !bodiesInRange.Contains(mustInclude))
		{
			bodiesInRange.Add(mustInclude);
		}

		HashSet<long> view = GetChildrenOfBodies(bodiesInRange);
		if (player.Parent is not Pivot)
		{
			view.Remove(player.FakeGuid);
		}

		return view;
	}

	public async Task SendMovementMessageToPlayer(Player player)
	{
		ArtificialBody anchor = Server.Instance.TryGetSpaceObject(player.AnchorGuid, out SpaceObject anchorObject)
			? anchorObject as ArtificialBody
			: null;
		if (anchor == null)
		{
			return;
		}

		// Pivots will always have the same guid as its child, so don't include it if
		// it is the player.
		List<ArtificialBody> bodiesInRange = GetArtificialBodiesInRange(player.Position, ViewRadius, player.FakeGuid);

		MovementMessage movementMessage = new MovementMessage
		{
			AnchorGuid = anchor.Guid,
			ParentGuid = player.Parent.Guid,
			PlayerAnimationData = player.AnimationData,
			OriginWorldPosition = anchor.Position.ToArray(),
			VisibleObjects = [.. BuildPlayerView(player, anchor, bodiesInRange)],
			ArtificialBodiesMovement = [],
			OtherPlayersMovement = [],
			CorpsesMovement = [],
			DynamicObjectsMovement = [],
		};

		Vector3D playerPosition = SpatialMath.ToAnchorRelativePosition(player.LocalPosition, anchor.Position,
			player.Parent.Position, player.Parent.Rotation);
		Vector3D playerVelocity = SpatialMath.ToAnchorRelativeVelocity(player.LocalVelocity, anchor.Velocity,
			player.Parent.Velocity, player.Parent.Rotation);

		if (player.NeedsTransformCorrection(anchor, playerPosition, playerVelocity))
		{
			movementMessage.PlayerPosition = playerPosition.ToFloatArray();
			movementMessage.PlayerRotation = player.Rotation.ToFloatArray();
			movementMessage.PlayerVelocity = playerVelocity.ToFloatArray();
		}

		foreach (ArtificialBody artificialBody in bodiesInRange)
		{
			MovementMessage.TransformInfo bodyTransform = new()
			{
				Guid = artificialBody.Guid,
				Position = (artificialBody.Position - anchor.Position).ToFloatArray(),
				Rotation = artificialBody.Rotation.ToFloatArray(),
				Velocity = (artificialBody.Velocity - anchor.Velocity).ToFloatArray(),
				AngularVelocity = artificialBody.AngularVelocity.ToFloatArray(),
			};

			if (artificialBody.StabilizeToTargetObj is not null)
			{
				bodyTransform.StabiliseToTargetGuid = artificialBody.StabilizeToTargetObj.Guid;
				bodyTransform.StabilisationOffset = artificialBody.StabilizeToTargetRelPosition.ToFloatArray();
			}

			movementMessage.ArtificialBodiesMovement.Add(bodyTransform);

			if (artificialBody is SpaceObjectVessel vessel)
			{
				foreach (SpaceObjectVessel memberVessel in vessel.AllVessels)
				{
					if (player.Parent.Guid != memberVessel.Guid && !player.IsSubscribedTo(memberVessel.Guid))
					{
						continue;
					}

					foreach (Player crewPlayer in memberVessel.VesselCrew)
					{
						if (!crewPlayer.PlayerReady || crewPlayer.Guid == player.Guid)
						{
							continue;
						}

						MovementMessage.OtherPlayerInfo playerInfo = new()
						{
							Guid = crewPlayer.FakeGuid,
							Position = (crewPlayer.Position - anchor.Position).ToFloatArray(),
							Rotation = crewPlayer.Rotation.ToFloatArray(),
							FreeLookX = crewPlayer.FreeLookX,
							FreeLookY = crewPlayer.FreeLookY,
							MouseLook = crewPlayer.MouseLook,
							AnimationData = crewPlayer.AnimationData,
							RagdollData = crewPlayer.RagdollData,
							JetpackDirection = crewPlayer.JetpackDirection
						};

						movementMessage.OtherPlayersMovement.Add(playerInfo);
					}

					foreach (long corpseGuid in memberVessel.Corpses)
					{
						if (Server.Instance.SpaceObjects.TryGet(corpseGuid, out SpaceObject obj) && obj is Corpse corpse
							&& corpse.LastChangeTime > player.LastMovementMessageSolarSystemTime)
						{
							MovementMessage.TransformInfo corpseInfo = new()
							{
								Guid = corpseGuid,
								Position = (corpse.Position - anchor.Position).ToFloatArray(),
								Rotation = corpse.Rotation.ToFloatArray(),
								Velocity = (corpse.Velocity - anchor.Velocity).ToFloatArray(),
								AngularVelocity = corpse.AngularVelocity.ToFloatArray(),
							};

							movementMessage.CorpsesMovement.Add(corpseInfo);
						}
					}

					foreach (long dynamicObjectGuid in memberVessel.DynamicObjects)
					{
						if (Server.Instance.SpaceObjects.TryGet(dynamicObjectGuid, out SpaceObject obj) && obj is DynamicObject dynamicObject
							&& dynamicObject.LastChangeTime > player.LastMovementMessageSolarSystemTime)
						{
							MovementMessage.TransformInfo dynamicObjectInfo = new()
							{
								Guid = dynamicObjectGuid,
								Position = (dynamicObject.Position - anchor.Position).ToFloatArray(),
								Rotation = dynamicObject.Rotation.ToFloatArray(),
								Velocity = (dynamicObject.Velocity - anchor.Velocity).ToFloatArray(),
								AngularVelocity = dynamicObject.AngularVelocity.ToFloatArray(),
							};

							movementMessage.DynamicObjectsMovement.Add(dynamicObjectInfo);
						}
					}
				}
			}
			else if (artificialBody is Pivot { ObjectType: SpaceObjectType.PlayerPivot } pivot)
			{
				Player otherPlayer = pivot.Child as Player;
				if (otherPlayer.PlayerReady && otherPlayer.Guid != player.Guid)
				{
					MovementMessage.OtherPlayerInfo playerInfo = new()
					{
						Guid = otherPlayer.FakeGuid,
						Position = (otherPlayer.Position - anchor.Position).ToFloatArray(),
						Rotation = otherPlayer.Rotation.ToFloatArray(),
						FreeLookX = otherPlayer.FreeLookX,
						FreeLookY = otherPlayer.FreeLookY,
						MouseLook = otherPlayer.MouseLook,
						AnimationData = otherPlayer.AnimationData,
						RagdollData = otherPlayer.RagdollData,
						JetpackDirection = otherPlayer.JetpackDirection?.Select(d => d).ToArray()
					};

					movementMessage.OtherPlayersMovement.Add(playerInfo);
				}
			}
		}

		player.LastMovementMessageSolarSystemTime = CurrentTime;
		await NetworkController.SendAsync(player.Guid, movementMessage);
	}

	public void InitializeData()
	{
		Debug.Log("Initialising celestial boldies data...");
		foreach (CelestialBodyData cbd in StaticData.SolarSystem.CelestialBodies)
		{
			CelestialBody newBody = new CelestialBody(cbd.GUID);
			newBody.Set(cbd.ParentGUID == -1 ? null : GetCelestialBody(cbd.ParentGUID), cbd.Mass, cbd.Radius, cbd.RotationPeriod, cbd.Eccentricity, cbd.SemiMajorAxis, cbd.Inclination, cbd.ArgumentOfPeriapsis, cbd.LongitudeOfAscendingNode, CurrentTime);
			newBody.AsteroidGasBurstTimeMin = cbd.AsteroidGasBurstTimeMin;
			newBody.AsteroidGasBurstTimeMax = cbd.AsteroidGasBurstTimeMax;
			newBody.AsteroidResources = cbd.AsteroidResources.ToList();
			_celestialBodies.Add(newBody);
		}
	}

	public ArtificialBody[] GetArtificialBodies()
	{
		return [.. Server.Instance.SpaceObjects.Values.OfType<ArtificialBody>()];
	}

	public List<SpaceObjectVessel> GetVesselsInRange(Vector3D position, double radius, long selfGuid)
	{
		return Server.Instance.SpaceObjects.QueryRadius<SpaceObjectVessel>(position, radius, selfGuid);
	}

	public List<ArtificialBody> GetArtificialBodiesInRange(Vector3D position, double radius, long selfGuid)
	{
		return Server.Instance.SpaceObjects.QueryRadius<ArtificialBody>(position, radius, selfGuid);
	}

	public List<SpaceObject> GetNearbySpaceObjects(Vector3D position, double radius)
	{
		return Server.Instance.SpaceObjects.QueryRadius(position, radius);
	}

	public SpaceObjectVessel NearestSpaceObjectVessel(Vector3D position, double radius)
	{
		return Server.Instance.SpaceObjects.FindNearestNeighbour<SpaceObjectVessel>(position, radius);
	}

	public List<CelestialBody> GetCelestialBodies()
	{
		return _celestialBodies;
	}

	public void GetSpawnPosition(double objectRadius, bool checkPosition, out Vector3D position, out Vector3D velocity, out QuaternionD rotation, List<long> nearArtificialBodyGUIDs, List<long> celestialBodyGUIDs, Vector3D? positionOffset, Vector3D? velocityAtPosition, QuaternionD? localRotation, double distanceFromSurfacePercMin, double distanceFromSurfacePercMax, SpawnRuleOrbit spawnRuleOrbit, double celestialBodyDeathDistanceMultiplier, double artificialBodyDistanceCheck, out OrbitParameters orbit)
	{
		position = Vector3D.Zero;
		velocity = Vector3D.Zero;
		rotation = QuaternionD.Identity;
		orbit = null;
		CelestialBody parentBody = null;
		ArtificialBody ab = null;
		if (nearArtificialBodyGUIDs is { Count: > 0 })
		{
			SpaceObject so = nearArtificialBodyGUIDs.Count != 1 ? Server.Instance.GetSpaceObject(nearArtificialBodyGUIDs[MathHelper.RandomRange(0, nearArtificialBodyGUIDs.Count)]) : Server.Instance.GetSpaceObject(nearArtificialBodyGUIDs[0]);
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
					rotation = localRotation.Value;
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
				rotation = localRotation.Value;
			}
		}
		if (parentBody == null)
		{
			if (celestialBodyGUIDs is { Count: > 0 })
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
				double distance = parentBody.GUID != 1 ? parentBody.Orbit.Radius + (parentBody.Orbit.GravityInfluenceRadius - parentBody.Orbit.Radius) * MathHelper.RandomRange(distanceFromSurfacePercMin, distanceFromSurfacePercMax) : parentBody.Orbit.Radius + (483940704314.0 - parentBody.Orbit.Radius) * MathHelper.RandomRange(0.1, 1.0);
				position = new Vector3D(0.0 - distance, 0.0, 0.0);
				velocity = Vector3D.Back * parentBody.Orbit.RandomOrbitVelocityMagnitudeAtDistance(distance);
				QuaternionD randomRot2 = MathHelper.RandomRotation();
				position = randomRot2 * position;
				velocity = randomRot2 * velocity;
			}
			if (localRotation.HasValue)
			{
				rotation = localRotation.Value;
			}
			else
			{
				rotation = MathHelper.RandomRotation();
			}
		}
		double rotatePivotAngle = -100.0 / position.Magnitude * (180.0 / System.Math.PI);
		position += parentBody.Position;
		int positionIteration = 0;
		if (checkPosition)
		{
			int spawnPointClear;
			do
			{
				spawnPointClear = 0;
				foreach (CelestialBody cb in _celestialBodies)
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
