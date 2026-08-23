using ZeroGravity.Math;

namespace OpenHellion;

/// <summary>
/// 	Re-measures a position, rotation or velocity against a different object.
/// </summary>
public static class SpatialMath
{
	public static Vector3D ToLocalPosition(Vector3D anchorRelativePosition, Vector3D anchorPosition,
		Vector3D parentPosition, QuaternionD parentRotation)
	{
		return QuaternionD.Inverse(parentRotation) * (anchorPosition + anchorRelativePosition - parentPosition);
	}

	public static QuaternionD ToLocalRotation(QuaternionD anchorRelativeRotation, QuaternionD parentRotation)
	{
		return QuaternionD.Inverse(parentRotation) * anchorRelativeRotation;
	}

	public static Vector3D ToAnchorRelativePosition(Vector3D localPosition, Vector3D anchorPosition,
		Vector3D parentPosition, QuaternionD parentRotation)
	{
		return parentPosition + parentRotation * localPosition - anchorPosition;
	}

	public static Vector3D ToLocalVelocity(Vector3D anchorRelativeVelocity, Vector3D anchorVelocity,
		Vector3D parentVelocity, QuaternionD parentRotation)
	{
		return QuaternionD.Inverse(parentRotation) * (anchorRelativeVelocity + anchorVelocity - parentVelocity);
	}

	public static Vector3D ToAnchorRelativeVelocity(Vector3D localVelocity, Vector3D anchorVelocity,
		Vector3D parentVelocity, QuaternionD parentRotation)
	{
		return parentVelocity + parentRotation * localVelocity - anchorVelocity;
	}

	public static QuaternionD ToAnchorRelativeRotation(QuaternionD localRotation, QuaternionD parentRotation)
	{
		return parentRotation * localRotation;
	}
}
