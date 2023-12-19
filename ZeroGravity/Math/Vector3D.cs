using System;

namespace ZeroGravity.Math;

public struct Vector3D
{
	private const double epsilon = 1E-06;

	public double X;

	public double Y;

	public double Z;

	public static Vector3D Back => new Vector3D(0.0, 0.0, -1.0);

	public static Vector3D Down => new Vector3D(0.0, -1.0, 0.0);

	public static Vector3D Forward => new Vector3D(0.0, 0.0, 1.0);

	public static Vector3D Left => new Vector3D(-1.0, 0.0, 0.0);

	public static Vector3D One => new Vector3D(1.0, 1.0, 1.0);

	public static Vector3D Right => new Vector3D(1.0, 0.0, 0.0);

	public static Vector3D Up => new Vector3D(0.0, 1.0, 0.0);

	public static Vector3D Zero => new Vector3D(0.0, 0.0, 0.0);

	public double Magnitude => System.Math.Sqrt(X * X + Y * Y + Z * Z);

	public double SqrMagnitude => X * X + Y * Y + Z * Z;

	public Vector3D Normalized => Normalize(this);

	public double this[int index]
	{
		get
		{
			return index switch
			{
				0 => X, 
				1 => Y, 
				2 => Z, 
				_ => throw new IndexOutOfRangeException("Invalid Vector3 index!"), 
			};
		}
		set
		{
			switch (index)
			{
			case 0:
				X = value;
				break;
			case 1:
				Y = value;
				break;
			case 2:
				Z = value;
				break;
			default:
				throw new IndexOutOfRangeException("Invalid Vector3 index!");
			}
		}
	}

	public Vector3D(double x, double y, double z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public Vector3D(double x, double y)
	{
		X = x;
		Y = y;
		Z = 0.0;
	}

	public static double Angle(Vector3D from, Vector3D to)
	{
		return System.Math.Acos(MathHelper.Clamp(Dot(from.Normalized, to.Normalized), -1.0, 1.0)) * (180.0 / System.Math.PI);
	}

	public static Vector3D ClampMagnitude(Vector3D vector, double maxLength)
	{
		if (vector.SqrMagnitude > maxLength * maxLength)
		{
			return vector.Normalized * maxLength;
		}
		return vector;
	}

	public static Vector3D Cross(Vector3D lhs, Vector3D rhs)
	{
		return new Vector3D(lhs.Y * rhs.Z - lhs.Z * rhs.Y, lhs.Z * rhs.X - lhs.X * rhs.Z, lhs.X * rhs.Y - lhs.Y * rhs.X);
	}

	public static double Distance(Vector3D a, Vector3D b)
	{
		Vector3D vector = new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
		return System.Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
	}

	public static double Dot(Vector3D lhs, Vector3D rhs)
	{
		return lhs.X * rhs.X + lhs.Y * rhs.Y + lhs.Z * rhs.Z;
	}

	private static void Internal_OrthoNormalize2(ref Vector3D a, ref Vector3D b)
	{
		INTERNAL_CALL_Internal_OrthoNormalize2(ref a, ref b);
	}

	private static void Internal_OrthoNormalize3(ref Vector3D a, ref Vector3D b, ref Vector3D c)
	{
		INTERNAL_CALL_Internal_OrthoNormalize3(ref a, ref b, ref c);
	}

	public static Vector3D Lerp(Vector3D a, Vector3D b, double t)
	{
		t = MathHelper.Clamp(t, 0.0, 1.0);
		return new Vector3D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
	}

	public static Vector3D LerpUnclamped(Vector3D a, Vector3D b, double t)
	{
		return new Vector3D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
	}

	public static Vector3D Max(Vector3D lhs, Vector3D rhs)
	{
		return new Vector3D(System.Math.Max(lhs.X, rhs.X), System.Math.Max(lhs.Y, rhs.Y), System.Math.Max(lhs.Z, rhs.Z));
	}

	public static Vector3D Min(Vector3D lhs, Vector3D rhs)
	{
		return new Vector3D(System.Math.Min(lhs.X, rhs.X), System.Math.Min(lhs.Y, rhs.Y), System.Math.Min(lhs.Z, rhs.Z));
	}

	public static Vector3D MoveTowards(Vector3D current, Vector3D target, double maxDistanceDelta)
	{
		Vector3D a = target - current;
		double magnitude = a.Magnitude;
		if (magnitude <= maxDistanceDelta || magnitude == 0.0)
		{
			return target;
		}
		return current + a / magnitude * maxDistanceDelta;
	}

	public static Vector3D Normalize(Vector3D value)
	{
		double num = value.Magnitude;
		if (num > 1E-06)
		{
			return value / num;
		}
		return Zero;
	}

	public static void OrthoNormalize(ref Vector3D normal, ref Vector3D tangent)
	{
		Internal_OrthoNormalize2(ref normal, ref tangent);
	}

	public static void OrthoNormalize(ref Vector3D normal, ref Vector3D tangent, ref Vector3D binormal)
	{
		Internal_OrthoNormalize3(ref normal, ref tangent, ref binormal);
	}

	public static Vector3D Project(Vector3D vector, Vector3D onNormal)
	{
		double num = Dot(onNormal, onNormal);
		if (num < double.Epsilon)
		{
			return Zero;
		}
		return onNormal * Dot(vector, onNormal) / num;
	}

	public static Vector3D ProjectOnPlane(Vector3D vector, Vector3D planeNormal)
	{
		return vector - Project(vector, planeNormal);
	}

	public static Vector3D Reflect(Vector3D inDirection, Vector3D inNormal)
	{
		return -2.0 * Dot(inNormal, inDirection) * inNormal + inDirection;
	}

	public static Vector3D RotateTowards(Vector3D current, Vector3D target, double maxRadiansDelta, double maxMagnitudeDelta)
	{
		INTERNAL_CALL_RotateTowards(ref current, ref target, maxRadiansDelta, maxMagnitudeDelta, out var result);
		return result;
	}

	public static Vector3D Scale(Vector3D a, Vector3D b)
	{
		return new Vector3D(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
	}

	public static Vector3D Slerp(Vector3D a, Vector3D b, double t)
	{
		INTERNAL_CALL_Slerp(ref a, ref b, t, out var result);
		return result;
	}

	public static Vector3D SlerpUnclamped(Vector3D a, Vector3D b, double t)
	{
		INTERNAL_CALL_SlerpUnclamped(ref a, ref b, t, out var result);
		return result;
	}

	public static Vector3D SmoothDamp(Vector3D current, Vector3D target, ref Vector3D currentVelocity, double smoothTime, double deltaTime)
	{
		return SmoothDamp(current, target, ref currentVelocity, smoothTime, double.PositiveInfinity, deltaTime);
	}

	public static Vector3D SmoothDamp(Vector3D current, Vector3D target, ref Vector3D currentVelocity, double smoothTime, double maxSpeed, double deltaTime)
	{
		smoothTime = System.Math.Max(0.0001, smoothTime);
		double num = 2.0 / smoothTime;
		double num2 = num * deltaTime;
		double d = 1.0 / (1.0 + num2 + 0.48 * num2 * num2 + 0.235 * num2 * num2 * num2);
		Vector3D vector = current - target;
		Vector3D vector2 = target;
		double maxLength = maxSpeed * smoothTime;
		vector = ClampMagnitude(vector, maxLength);
		target = current - vector;
		Vector3D vector3 = (currentVelocity + num * vector) * deltaTime;
		currentVelocity = (currentVelocity - num * vector3) * d;
		Vector3D vector4 = target + (vector + vector3) * d;
		if (Dot(vector2 - current, vector4 - vector2) > 0.0)
		{
			vector4 = vector2;
			currentVelocity = (vector4 - vector2) / deltaTime;
		}
		return vector4;
	}

	public override bool Equals(object other)
	{
		if (other is not Vector3D vector)
		{
			return false;
		}
		return X.Equals(vector.X) && Y.Equals(vector.Y) && Z.Equals(vector.Z);
	}

	public override int GetHashCode()
	{
		return X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);
	}

	public void Normalize()
	{
		double num = Magnitude;
		if (num > 1E-06)
		{
			this /= num;
		}
		else
		{
			this = Zero;
		}
	}

	public void Scale(Vector3D scale)
	{
		X *= scale.X;
		Y *= scale.Y;
		Z *= scale.Z;
	}

	public void Set(double new_x, double new_y, double new_z)
	{
		X = new_x;
		Y = new_y;
		Z = new_z;
	}

	public string ToString(string format)
	{
		return $"({X.ToString(format)}, {Y.ToString(format)}, {Z.ToString(format)})";
	}

	public override string ToString()
	{
		return $"({X:0.###}, {Y:0.###}, {Z:0.###})";
	}

	public static Vector3D operator +(Vector3D a, Vector3D b)
	{
		return new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
	}

	public static Vector3D operator /(Vector3D a, double d)
	{
		return new Vector3D(a.X / d, a.Y / d, a.Z / d);
	}

	public static bool operator ==(Vector3D lhs, Vector3D rhs)
	{
		return (lhs - rhs).SqrMagnitude < 9.999999E-11;
	}

	public static bool operator !=(Vector3D lhs, Vector3D rhs)
	{
		return (lhs - rhs).SqrMagnitude >= 9.999999E-11;
	}

	public static Vector3D operator *(double d, Vector3D a)
	{
		return new Vector3D(a.X * d, a.Y * d, a.Z * d);
	}

	public static Vector3D operator *(Vector3D a, double d)
	{
		return new Vector3D(a.X * d, a.Y * d, a.Z * d);
	}

	public static Vector3D operator -(Vector3D a, Vector3D b)
	{
		return new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
	}

	public static Vector3D operator -(Vector3D a)
	{
		return new Vector3D(0.0 - a.X, 0.0 - a.Y, 0.0 - a.Z);
	}

	private static void INTERNAL_CALL_Internal_OrthoNormalize2(ref Vector3D a, ref Vector3D b)
	{
		a.Normalize();
		double dot0 = Dot(a, b);
		b -= dot0 * a;
		b.Normalize();
	}

	private static void INTERNAL_CALL_Internal_OrthoNormalize3(ref Vector3D a, ref Vector3D b, ref Vector3D c)
	{
		a.Normalize();
		double dot0 = Dot(a, b);
		b -= dot0 * a;
		b.Normalize();
		double dot1 = Dot(b, c);
		dot0 = Dot(a, c);
		c -= dot0 * a + dot1 * b;
		c.Normalize();
	}

	private static void INTERNAL_CALL_RotateTowards(ref Vector3D current, ref Vector3D target, double maxRadiansDelta, double maxMagnitudeDelta, out Vector3D value)
	{
		value = Zero;
		throw new Exception("INTERNAL_CALL_RotateTowards IS NOT IMPLEMENTED");
	}

	private static void INTERNAL_CALL_Slerp(ref Vector3D a, ref Vector3D b, double t, out Vector3D value)
	{
		value = Zero;
		throw new Exception("INTERNAL_CALL_Slerp IS NOT IMPLEMENTED");
	}

	private static void INTERNAL_CALL_SlerpUnclamped(ref Vector3D a, ref Vector3D b, double t, out Vector3D value)
	{
		value = Zero;
		throw new Exception("INTERNAL_CALL_SlerpUnclamped IS NOT IMPLEMENTED");
	}
}
