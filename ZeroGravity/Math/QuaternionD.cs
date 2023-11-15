using System;

namespace ZeroGravity.Math;

public struct QuaternionD
{
	private const double epsilon = 1E-06;

	public double X;

	public double Y;

	public double Z;

	public double W;

	public double this[int index]
	{
		get
		{
			return index switch
			{
				0 => X, 
				1 => Y, 
				2 => Z, 
				3 => W, 
				_ => throw new IndexOutOfRangeException("Invalid Quaternion index!"), 
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
			case 3:
				W = value;
				break;
			default:
				throw new IndexOutOfRangeException("Invalid Quaternion index!");
			}
		}
	}

	public static QuaternionD Identity => new QuaternionD(0.0, 0.0, 0.0, 1.0);

	public Vector3D EulerAngles
	{
		get
		{
			return Internal_ToEulerRad(this) * (180.0 / System.Math.PI);
		}
		set
		{
			this = Internal_FromEulerRad(value * (System.Math.PI / 180.0));
		}
	}

	public QuaternionD(double x, double y, double z, double w)
	{
		X = x;
		Y = y;
		Z = z;
		W = w;
	}

	public static QuaternionD operator *(QuaternionD lhs, QuaternionD rhs)
	{
		return new QuaternionD(lhs.W * rhs.X + lhs.X * rhs.W + lhs.Y * rhs.Z - lhs.Z * rhs.Y, lhs.W * rhs.Y + lhs.Y * rhs.W + lhs.Z * rhs.X - lhs.X * rhs.Z, lhs.W * rhs.Z + lhs.Z * rhs.W + lhs.X * rhs.Y - lhs.Y * rhs.X, lhs.W * rhs.W - lhs.X * rhs.X - lhs.Y * rhs.Y - lhs.Z * rhs.Z);
	}

	public static Vector3D operator *(QuaternionD rotation, Vector3D point)
	{
		double num1 = rotation.X * 2.0;
		double num5 = rotation.Y * 2.0;
		double num6 = rotation.Z * 2.0;
		double num7 = rotation.X * num1;
		double num8 = rotation.Y * num5;
		double num9 = rotation.Z * num6;
		double num10 = rotation.X * num5;
		double num11 = rotation.X * num6;
		double num12 = rotation.Y * num6;
		double num2 = rotation.W * num1;
		double num3 = rotation.W * num5;
		double num4 = rotation.W * num6;
		Vector3D retVal = default(Vector3D);
		retVal.X = (1.0 - (num8 + num9)) * point.X + (num10 - num4) * point.Y + (num11 + num3) * point.Z;
		retVal.Y = (num10 + num4) * point.X + (1.0 - (num7 + num9)) * point.Y + (num12 - num2) * point.Z;
		retVal.Z = (num11 - num3) * point.X + (num12 + num2) * point.Y + (1.0 - (num7 + num8)) * point.Z;
		return retVal;
	}

	public static bool operator ==(QuaternionD lhs, QuaternionD rhs)
	{
		return Dot(lhs, rhs) > 0.999998986721039;
	}

	public static bool operator !=(QuaternionD lhs, QuaternionD rhs)
	{
		return Dot(lhs, rhs) <= 0.999998986721039;
	}

	public void Set(double new_x, double new_y, double new_z, double new_w)
	{
		X = new_x;
		Y = new_y;
		Z = new_z;
		W = new_w;
	}

	public static double Dot(QuaternionD a, QuaternionD b)
	{
		return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
	}

	public static QuaternionD AngleAxis(double angle, Vector3D axis)
	{
		axis.Normalize();
		INTERNAL_CALL_AngleAxis(angle, ref axis, out var quaternion);
		return quaternion;
	}

	public void ToAngleAxis(out double angle, out Vector3D axis)
	{
		Internal_ToAxisAngleRad(this, out axis, out angle);
		angle *= 180.0 / System.Math.PI;
	}

	public static QuaternionD FromToRotation(Vector3D fromDirection, Vector3D toDirection)
	{
		INTERNAL_CALL_FromToRotation(ref fromDirection, ref toDirection, out var quaternion);
		return quaternion;
	}

	public void SetFromToRotation(Vector3D fromDirection, Vector3D toDirection)
	{
		this = FromToRotation(fromDirection, toDirection);
	}

	public static QuaternionD LookRotation(Vector3D forward, Vector3D upwards)
	{
		INTERNAL_CALL_LookRotation(ref forward, ref upwards, out var quaternion);
		return quaternion;
	}

	public static QuaternionD LookRotation(Vector3D forward)
	{
		Vector3D up = Vector3D.Up;
		INTERNAL_CALL_LookRotation(ref forward, ref up, out var quaternion);
		return quaternion;
	}

	public void SetLookRotation(Vector3D view)
	{
		SetLookRotation(view, Vector3D.Up);
	}

	public void SetLookRotation(Vector3D view, Vector3D up)
	{
		this = LookRotation(view, up);
	}

	public static QuaternionD Slerp(QuaternionD a, QuaternionD b, double t)
	{
		INTERNAL_CALL_Slerp(ref a, ref b, t, out var quaternion);
		return quaternion;
	}

	public static QuaternionD SlerpUnclamped(QuaternionD a, QuaternionD b, double t)
	{
		INTERNAL_CALL_SlerpUnclamped(ref a, ref b, t, out var quaternion);
		return quaternion;
	}

	public static QuaternionD Lerp(QuaternionD a, QuaternionD b, double t)
	{
		INTERNAL_CALL_Lerp(ref a, ref b, t, out var quaternion);
		return quaternion;
	}

	public static QuaternionD LerpUnclamped(QuaternionD a, QuaternionD b, double t)
	{
		INTERNAL_CALL_LerpUnclamped(ref a, ref b, t, out var quaternion);
		return quaternion;
	}

	public static QuaternionD RotateTowards(QuaternionD from, QuaternionD to, double maxDegreesDelta)
	{
		double num = Angle(from, to);
		if (num == 0.0)
		{
			return to;
		}
		double t = System.Math.Min(1.0, maxDegreesDelta / num);
		return SlerpUnclamped(from, to, t);
	}

	public static QuaternionD Inverse(QuaternionD rotation)
	{
		INTERNAL_CALL_Inverse(ref rotation, out var quaternion);
		return quaternion;
	}

	public override string ToString()
	{
		return $"({X:0.###}, {Y:0.###}, {Z:0.###}, {W:0.###})";
	}

	public string ToString(string format)
	{
		return $"({X.ToString(format)}, {Y.ToString(format)}, {Z.ToString(format)}, {W.ToString(format)})";
	}

	public static double Angle(QuaternionD a, QuaternionD b)
	{
		return System.Math.Acos(System.Math.Min(System.Math.Abs(Dot(a, b)), 1.0)) * 2.0 * (180.0 / System.Math.PI);
	}

	public static QuaternionD Euler(double x, double y, double z)
	{
		return Internal_FromEulerRad(new Vector3D(x, y, z) * (System.Math.PI / 180.0));
	}

	public static QuaternionD Euler(Vector3D euler)
	{
		return Internal_FromEulerRad(euler * (System.Math.PI / 180.0));
	}

	private static Vector3D Internal_ToEulerRad(QuaternionD rotation)
	{
		INTERNAL_CALL_ToEulerRad(ref rotation, out var vector3);
		return vector3;
	}

	private static QuaternionD Internal_FromEulerRad(Vector3D euler)
	{
		INTERNAL_CALL_FromEulerRad(ref euler, out var quaternion);
		return quaternion;
	}

	private static void Internal_ToAxisAngleRad(QuaternionD q, out Vector3D axis, out double angle)
	{
		INTERNAL_CALL_ToAxisAngleRad(ref q, out axis, out angle);
	}

	public override int GetHashCode()
	{
		return X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2) ^ (W.GetHashCode() >> 1);
	}

	public override bool Equals(object other)
	{
		if (!(other is QuaternionD quaternion))
		{
			return false;
		}
		if (X.Equals(quaternion.X) && Y.Equals(quaternion.Y) && Z.Equals(quaternion.Z))
		{
			return W.Equals(quaternion.W);
		}
		return false;
	}

	private static void INTERNAL_CALL_AngleAxis(double angle, ref Vector3D axis, out QuaternionD value)
	{
		value = Identity;
		if (axis.SqrMagnitude > 1E-06)
		{
			double sinOfHalf = System.Math.Sin(angle * (System.Math.PI / 180.0) * 0.5);
			double cosOfHalf = System.Math.Cos(angle * (System.Math.PI / 180.0) * 0.5);
			value.Set(axis.X * sinOfHalf, axis.Y * sinOfHalf, axis.Z * sinOfHalf, cosOfHalf);
		}
	}

	private static void INTERNAL_CALL_ToAxisAngleRad(ref QuaternionD q, out Vector3D axis, out double angle)
	{
		axis = Vector3D.Zero;
		angle = 0.0;
		double sqrLength = q.X * q.X + q.Y * q.Y + q.Z * q.Z;
		if (sqrLength > 1E-06)
		{
			angle = 2.0 * System.Math.Acos(q.W);
			axis = new Vector3D(q.X, q.Y, q.Z) / sqrLength;
		}
		else
		{
			angle = 0.0;
			axis = new Vector3D(1.0, 0.0, 0.0);
		}
	}

	private static void INTERNAL_CALL_FromEulerRad(ref Vector3D euler, out QuaternionD value)
	{
		double halfXAngle = euler.X * 0.5;
		double halfYAngle = euler.Y * 0.5;
		double halfZAngle = euler.Z * 0.5;
		double cx = System.Math.Cos(halfXAngle);
		double sx = System.Math.Sin(halfXAngle);
		double cy = System.Math.Cos(halfYAngle);
		double sy = System.Math.Sin(halfYAngle);
		double cz = System.Math.Cos(halfZAngle);
		double sz = System.Math.Sin(halfZAngle);
		QuaternionD[] quats = new QuaternionD[3]
		{
			new QuaternionD(sx, 0.0, 0.0, cx),
			new QuaternionD(0.0, sy, 0.0, cy),
			new QuaternionD(0.0, 0.0, sz, cz)
		};
		value = quats[2] * quats[0] * quats[1];
	}

	private static void INTERNAL_CALL_ToEulerRad(ref QuaternionD rotation, out Vector3D value)
	{
		double[,] mat = new double[3, 3]
		{
			{
				1.0 - (2.0 * rotation.Y * rotation.Y + 2.0 * rotation.Z * rotation.Z),
				2.0 * rotation.Y * rotation.X - 2.0 * rotation.Z * rotation.W,
				2.0 * rotation.Z * rotation.X + 2.0 * rotation.Y * rotation.W
			},
			{
				2.0 * rotation.Y * rotation.X + 2.0 * rotation.Z * rotation.W,
				1.0 - (2.0 * rotation.X * rotation.X + 2.0 * rotation.Z * rotation.Z),
				2.0 * rotation.Z * rotation.Y - 2.0 * rotation.X * rotation.W
			},
			{
				2.0 * rotation.Z * rotation.X - 2.0 * rotation.Y * rotation.W,
				2.0 * rotation.Z * rotation.Y + 2.0 * rotation.X * rotation.W,
				1.0 - (2.0 * rotation.X * rotation.X + 2.0 * rotation.Y * rotation.Y)
			}
		};
		value = Vector3D.Zero;
		double xAng = 0.0 - System.Math.Asin(mat[1, 2]);
		if (xAng >= System.Math.PI / 2.0)
		{
			value.Set(System.Math.PI / 2.0, System.Math.Atan2(mat[0, 1], mat[0, 0]), 0.0);
		}
		else if (xAng <= -System.Math.PI / 2.0)
		{
			value.Set(-System.Math.PI / 2.0, System.Math.Atan2(0.0 - mat[0, 1], mat[0, 0]), 0.0);
		}
		else
		{
			value.Set(xAng, System.Math.Atan2(mat[0, 2], mat[2, 2]), System.Math.Atan2(mat[1, 0], mat[1, 1]));
		}
	}

	private static void INTERNAL_CALL_Inverse(ref QuaternionD rotation, out QuaternionD value)
	{
		value = rotation;
		double lengthSq = rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z + rotation.W * rotation.W;
		if (lengthSq > 1E-06)
		{
			double i = 1.0 / lengthSq;
			value.X = (0.0 - rotation.X) * i;
			value.Y = (0.0 - rotation.Y) * i;
			value.Z = (0.0 - rotation.Z) * i;
			value.W = rotation.W * i;
		}
	}

	private static void INTERNAL_CALL_LookRotation(ref Vector3D forward, ref Vector3D up, out QuaternionD value)
	{
		forward = Vector3D.Normalize(forward);
		Vector3D right = Vector3D.Normalize(Vector3D.Cross(up, forward));
		up = Vector3D.Cross(forward, right);
		double m0 = right.X;
		double m = right.Y;
		double m2 = right.Z;
		double m3 = up.X;
		double m4 = up.Y;
		double m5 = up.Z;
		double m6 = forward.X;
		double m7 = forward.Y;
		double m8 = forward.Z;
		double num8 = m0 + m4 + m8;
		if (num8 > 0.0)
		{
			double num = System.Math.Sqrt(num8 + 1.0);
			value.W = num * 0.5;
			num = 0.5 / num;
			value.X = (m5 - m7) * num;
			value.Y = (m6 - m2) * num;
			value.Z = (m - m3) * num;
		}
		else if (m0 >= m4 && m0 >= m8)
		{
			double num7 = System.Math.Sqrt(1.0 + m0 - m4 - m8);
			double num4 = 0.5 / num7;
			value.X = 0.5 * num7;
			value.Y = (m + m3) * num4;
			value.Z = (m2 + m6) * num4;
			value.W = (m5 - m7) * num4;
		}
		else if (m4 > m8)
		{
			double num6 = System.Math.Sqrt(1.0 + m4 - m0 - m8);
			double num3 = 0.5 / num6;
			value.X = (m3 + m) * num3;
			value.Y = 0.5 * num6;
			value.Z = (m7 + m5) * num3;
			value.W = (m6 - m2) * num3;
		}
		else
		{
			double num5 = System.Math.Sqrt(1.0 + m8 - m0 - m4);
			double num2 = 0.5 / num5;
			value.X = (m6 + m2) * num2;
			value.Y = (m7 + m5) * num2;
			value.Z = 0.5 * num5;
			value.W = (m - m3) * num2;
		}
	}

	private static void INTERNAL_CALL_FromToRotation(ref Vector3D fromDirection, ref Vector3D toDirection, out QuaternionD value)
	{
		value = RotateTowards(LookRotation(fromDirection), LookRotation(toDirection), double.MaxValue);
	}

	private static void INTERNAL_CALL_Slerp(ref QuaternionD from, ref QuaternionD to, double t, out QuaternionD value)
	{
		INTERNAL_CALL_SlerpUnclamped(ref from, ref to, MathHelper.Clamp(t, 0.0, 1.0), out value);
	}

	private static void INTERNAL_CALL_SlerpUnclamped(ref QuaternionD from, ref QuaternionD to, double t, out QuaternionD value)
	{
		double val1 = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
		bool flag = false;
		if (val1 < 0.0)
		{
			flag = true;
			val1 = 0.0 - val1;
		}
		double val3;
		double val2;
		if (val1 > 0.999999)
		{
			val3 = 1.0 - t;
			val2 = flag ? 0.0 - t : t;
		}
		else
		{
			double val4 = System.Math.Acos(val1);
			double val5 = 1.0 / System.Math.Sin(val4);
			val3 = System.Math.Sin((1.0 - t) * val4) * val5;
			val2 = flag ? (0.0 - System.Math.Sin(t * val4)) * val5 : System.Math.Sin(t * val4) * val5;
		}
		value.X = val3 * from.X + val2 * to.X;
		value.Y = val3 * from.Y + val2 * to.Y;
		value.Z = val3 * from.Z + val2 * to.Z;
		value.W = val3 * from.W + val2 * to.W;
	}

	private static void INTERNAL_CALL_Lerp(ref QuaternionD from, ref QuaternionD to, double t, out QuaternionD value)
	{
		INTERNAL_CALL_LerpUnclamped(ref from, ref to, MathHelper.Clamp(t, 0.0, 1.0), out value);
	}

	private static void INTERNAL_CALL_LerpUnclamped(ref QuaternionD from, ref QuaternionD to, double t, out QuaternionD value)
	{
		double val1 = 1.0 - t;
		double val2 = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
		if (val2 >= 0.0)
		{
			value.X = val1 * from.X + t * to.X;
			value.Y = val1 * from.Y + t * to.Y;
			value.Z = val1 * from.Z + t * to.Z;
			value.W = val1 * from.W + t * to.W;
		}
		else
		{
			value.X = val1 * from.X - t * to.X;
			value.Y = val1 * from.Y - t * to.Y;
			value.Z = val1 * from.Z - t * to.Z;
			value.W = val1 * from.W - t * to.W;
		}
		double val3 = 1.0 / System.Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z + value.W * value.W);
		value.X *= val3;
		value.Y *= val3;
		value.Z *= val3;
		value.W *= val3;
	}
}
