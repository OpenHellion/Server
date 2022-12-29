using ZeroGravity.Math;

namespace ZeroGravity.Objects;

public abstract class SpaceObjectTransferable : SpaceObject
{
	public Vector3D LocalPosition;

	public QuaternionD LocalRotation;

	public override Vector3D Position
	{
		get
		{
			if (Parent == null)
			{
				Dbg.Error(ObjectType.ToString() + " parent is null!", GUID);
				return LocalPosition;
			}
			if (Parent is SpaceObjectVessel)
			{
				SpaceObjectVessel vessel = Parent as SpaceObjectVessel;
				return vessel.Position + QuaternionD.LookRotation(vessel.Forward, vessel.Up) * LocalPosition;
			}
			return Parent.Position + LocalPosition;
		}
	}

	public QuaternionD Rotation
	{
		get
		{
			if (Parent is SpaceObjectVessel)
			{
				SpaceObjectVessel vessel = Parent as SpaceObjectVessel;
				return QuaternionD.LookRotation(vessel.Forward, vessel.Up) * LocalRotation;
			}
			return LocalRotation;
		}
	}

	public SpaceObjectTransferable(long guid, Vector3D localPosition, QuaternionD localRotation)
		: base(guid)
	{
		LocalPosition = localPosition;
		LocalRotation = localRotation;
	}
}
