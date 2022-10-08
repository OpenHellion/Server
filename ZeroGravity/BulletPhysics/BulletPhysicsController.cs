using System;
using System.Collections.Generic;
using BulletSharp;
using BulletSharp.Math;
using ZeroGravity.Data;
using ZeroGravity.Objects;

namespace ZeroGravity.BulletPhysics;

public class BulletPhysicsController
{
	private BroadphaseInterface broadphase;

	private DefaultCollisionConfiguration collisionConfiguration;

	private CollisionDispatcher dispatcher;

	public DiscreteDynamicsWorld dynamicsWorld;

	public double Restitution = 0.85;

	public double Friction = 0.7;

	private double prevUpdateTime;

	public BulletPhysicsController()
	{
		collisionConfiguration = new DefaultCollisionConfiguration();
		dispatcher = new CollisionDispatcher(collisionConfiguration);
		broadphase = new DbvtBroadphase();
		dynamicsWorld = new DiscreteDynamicsWorld(dispatcher, broadphase, null, collisionConfiguration);
		dynamicsWorld.Gravity = Vector3.Zero;
		dynamicsWorld.SetInternalTickCallback(InternalTickCallback);
		GImpactCollisionAlgorithm.RegisterAlgorithm(dispatcher);
	}

	public void CreateAndAddRigidBody(SpaceObjectVessel vessel)
	{
		CompoundShape compound = new CompoundShape();
		CompoundShape baseCompound = new CompoundShape();
		foreach (VesselPrimitiveColliderData data4 in vessel.PrimitiveCollidersData)
		{
			CollisionShape shape4 = null;
			Matrix l = BulletHelper.AffineTransformation(1f, Quaternion.Identity, data4.CenterPosition.ToVector3());
			if (data4.Type == ColliderDataType.Box)
			{
				shape4 = new BoxShape(data4.Bounds.X / 2.0, data4.Bounds.Y / 2.0, data4.Bounds.Z / 2.0);
			}
			else if (data4.Type == ColliderDataType.Sphere)
			{
				shape4 = new SphereShape(data4.Bounds.X);
			}
			if (shape4 != null)
			{
				shape4.UserObject = data4;
				baseCompound.AddChildShape(l, shape4);
			}
		}
		foreach (VesselMeshColliderData data3 in vessel.MeshCollidersData)
		{
			GImpactMeshShape shape3 = null;
			Matrix k = BulletHelper.AffineTransformation(1f, data3.Rotation.ToQuaternion(), data3.CenterPosition.ToVector3());
			shape3 = new GImpactMeshShape(new TriangleIndexVertexArray(data3.Indices, data3.Vertices));
			shape3.LocalScaling = data3.Scale.ToVector3();
			shape3.UpdateBound();
			if (shape3 != null)
			{
				shape3.UserObject = data3;
				baseCompound.AddChildShape(k, shape3);
			}
		}
		baseCompound.UserObject = vessel;
		Matrix offset = BulletHelper.AffineTransformation(1f, Quaternion.Identity, -vessel.VesselData.CollidersCenterOffset.ToVector3D().ToVector3());
		compound.AddChildShape(offset, baseCompound);
		Quaternion qua = BulletHelper.LookRotation(vessel.Forward.ToVector3(), vessel.Up.ToVector3());
		Matrix position = BulletHelper.AffineTransformation(1f, qua, vessel.Position.ToVector3());
		double AdditionalMass = 0.0;
		if (vessel.AllDockedVessels.Count > 0)
		{
			foreach (Ship p in vessel.AllDockedVessels)
			{
				AdditionalMass += p.Mass;
				CompoundShape newCompound = new CompoundShape();
				Matrix relative = BulletHelper.AffineTransformation(1f, p.RelativeRotationFromMainParent.ToQuaternion(), p.RelativePositionFromMainParent.ToVector3() - vessel.VesselData.CollidersCenterOffset.ToVector3D().ToVector3());
				foreach (VesselPrimitiveColliderData data2 in p.PrimitiveCollidersData)
				{
					CollisionShape shape2 = null;
					Matrix j = BulletHelper.AffineTransformation(1f, Quaternion.Identity, data2.CenterPosition.ToVector3());
					if (data2.Type == ColliderDataType.Box)
					{
						shape2 = new BoxShape(data2.Bounds.X / 2.0, data2.Bounds.Y / 2.0, data2.Bounds.Z / 2.0);
					}
					else if (data2.Type == ColliderDataType.Sphere)
					{
						shape2 = new SphereShape(data2.Bounds.X);
					}
					if (shape2 != null)
					{
						shape2.UserObject = data2;
						newCompound.AddChildShape(j, shape2);
					}
				}
				foreach (VesselMeshColliderData data in p.MeshCollidersData)
				{
					GImpactMeshShape shape = null;
					Matrix i = BulletHelper.AffineTransformation(1f, data.Rotation.ToQuaternion(), data.CenterPosition.ToVector3());
					shape = new GImpactMeshShape(new TriangleIndexVertexArray(data.Indices, data.Vertices));
					shape.LocalScaling = data.Scale.ToVector3();
					shape.UpdateBound();
					if (shape != null)
					{
						shape.UserObject = data;
						newCompound.AddChildShape(i, shape);
					}
				}
				newCompound.UserObject = p;
				compound.AddChildShape(relative, newCompound);
			}
		}
		DefaultMotionState motionState = new DefaultMotionState(position);
		Vector3 inertiaTensor = compound.CalculateLocalInertia(vessel.Mass + AdditionalMass);
		RigidBodyConstructionInfo rigidBodyCI = new RigidBodyConstructionInfo(vessel.Mass + AdditionalMass, motionState, compound);
		rigidBodyCI.LocalInertia = inertiaTensor;
		RigidBody rigidBody = new RigidBody(rigidBodyCI);
		rigidBody.SetDamping(0.0, 0.0);
		rigidBody.SetSleepingThresholds(0.1, 0.1);
		rigidBody.Restitution = Restitution;
		rigidBody.Friction = Friction;
		rigidBody.ForceActivationState(ActivationState.DisableDeactivation);
		rigidBody.UserObject = vessel;
		dynamicsWorld.AddRigidBody(rigidBody);
		compound.GetBoundingSphere(out var _, out var sphereRadius);
		vessel.SetRadius(sphereRadius);
		vessel.RigidBody = rigidBody;
	}

	public static void ComplexBoundCalculation(SpaceObjectVessel vessel, out Vector3 minValue, out Vector3 maxValue)
	{
		CompoundShape compound = new CompoundShape();
		foreach (VesselPrimitiveColliderData data4 in vessel.PrimitiveCollidersData)
		{
			if (data4.AffectingCenterOfMass)
			{
				CollisionShape shape4 = null;
				Matrix l = BulletHelper.AffineTransformation(1f, Quaternion.Identity, data4.CenterPosition.ToVector3());
				if (data4.Type == ColliderDataType.Box)
				{
					shape4 = new BoxShape(data4.Bounds.X / 2.0, data4.Bounds.Y / 2.0, data4.Bounds.Z / 2.0);
				}
				else if (data4.Type == ColliderDataType.Sphere)
				{
					shape4 = new SphereShape(data4.Bounds.X);
				}
				if (shape4 != null)
				{
					compound.AddChildShape(l, shape4);
				}
			}
		}
		foreach (VesselMeshColliderData data3 in vessel.MeshCollidersData)
		{
			if (data3.AffectingCenterOfMass)
			{
				GImpactMeshShape shape3 = null;
				Matrix k = BulletHelper.AffineTransformation(1f, data3.Rotation.ToQuaternion(), data3.CenterPosition.ToVector3());
				shape3 = new GImpactMeshShape(new TriangleIndexVertexArray(data3.Indices, data3.Vertices));
				shape3.LocalScaling = data3.Scale.ToVector3();
				shape3.UpdateBound();
				if (shape3 != null)
				{
					compound.AddChildShape(k, shape3);
				}
			}
		}
		if (vessel.AllDockedVessels.Count > 0)
		{
			foreach (Ship p in vessel.AllDockedVessels)
			{
				CompoundShape newCompound = new CompoundShape();
				Matrix relative = BulletHelper.AffineTransformation(1f, p.RelativeRotationFromMainParent.ToQuaternion(), p.RelativePositionFromMainParent.ToVector3());
				foreach (VesselPrimitiveColliderData data2 in p.PrimitiveCollidersData)
				{
					CollisionShape shape2 = null;
					Matrix j = BulletHelper.AffineTransformation(1f, Quaternion.Identity, data2.CenterPosition.ToVector3());
					if (data2.Type == ColliderDataType.Box)
					{
						shape2 = new BoxShape(data2.Bounds.X / 2.0, data2.Bounds.Y / 2.0, data2.Bounds.Z / 2.0);
					}
					else if (data2.Type == ColliderDataType.Sphere)
					{
						shape2 = new SphereShape(data2.Bounds.X);
					}
					if (shape2 != null)
					{
						newCompound.AddChildShape(j, shape2);
					}
				}
				foreach (VesselMeshColliderData data in p.MeshCollidersData)
				{
					GImpactMeshShape shape = null;
					Matrix i = BulletHelper.AffineTransformation(1f, data.Rotation.ToQuaternion(), data.CenterPosition.ToVector3());
					shape = new GImpactMeshShape(new TriangleIndexVertexArray(data.Indices, data.Vertices));
					shape.LocalScaling = data.Scale.ToVector3();
					shape.UpdateBound();
					if (shape != null)
					{
						newCompound.AddChildShape(i, shape);
					}
				}
				compound.AddChildShape(relative, newCompound);
			}
		}
		Matrix identity = BulletHelper.AffineTransformation(1f, Quaternion.Identity, Vector3.Zero);
		compound.GetAabb(identity, out minValue, out maxValue);
	}

	public bool RayCast(Vector3 from, Vector3 to, out ClosestRayResultCallback result)
	{
		result = new ClosestRayResultCallback(ref from, ref to);
		dynamicsWorld.RayTest(from, to, result);
		return result.HasHit;
	}

	public bool RayCastAll(Vector3 from, Vector3 to, out AllHitsRayResultCallback result)
	{
		result = new AllHitsRayResultCallback(from, to);
		dynamicsWorld.RayTest(from, to, result);
		return result.HasHit;
	}

	private void InternalTickCallback(DynamicsWorld world, double timeStep)
	{
		foreach (SpaceObjectVessel vess in Server.Instance.AllVessels)
		{
			vess.ReadPhysicsParameters();
		}
		try
		{
			int k = world.Dispatcher.NumManifolds;
			for (int i = 0; i < k; i++)
			{
				PersistentManifold contactManifold = world.Dispatcher.GetManifoldByIndexInternal(i);
				RigidBody obA = contactManifold.Body0 as RigidBody;
				RigidBody obB = contactManifold.Body1 as RigidBody;
				Vector3 vel = obA.LinearVelocity - obB.LinearVelocity;
				List<long> shipsGUID = new List<long>();
				if (obA.UserObject is Ship)
				{
					shipsGUID.Add((obA.UserObject as Ship).GUID);
				}
				if (obB.UserObject is Ship)
				{
					shipsGUID.Add((obB.UserObject as Ship).GUID);
				}
				int numContacts = contactManifold.NumContacts;
				for (int j = 0; j < numContacts; j++)
				{
					ManifoldPoint pt = contactManifold.GetContactPoint(j);
					if (pt.AppliedImpulse.IsNotEpsilonZeroD() && shipsGUID.Count > 0)
					{
						(Server.Instance.GetVessel(shipsGUID[0]) as Ship).SendCollision(vel.Length, pt.AppliedImpulse, pt.LifeTime, (shipsGUID.Count > 1) ? shipsGUID[1] : (-1));
					}
				}
				contactManifold.ClearManifold();
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	public void Update()
	{
		double deltaTime = Server.SolarSystemTime - prevUpdateTime;
		prevUpdateTime = Server.SolarSystemTime;
		try
		{
			dynamicsWorld.StepSimulation(deltaTime);
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
	}

	public bool RemoveRigidBody(SpaceObjectVessel ship)
	{
		try
		{
			if (ship.RigidBody != null && dynamicsWorld.CollisionObjectArray.Contains(ship.RigidBody))
			{
				dynamicsWorld.RemoveRigidBody(ship.RigidBody);
				ship.RigidBody = null;
				return true;
			}
			if (ship.IsDocked)
			{
				return RemoveRigidBody(ship.DockedToMainVessel as Ship);
			}
			return true;
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
		return false;
	}
}
