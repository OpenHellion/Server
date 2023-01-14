using System;

namespace ZeroGravity;

public static class GUIDFactory
{
	public static class Range
	{
		public const long PlayerFakeFrom = 1000000000000L;

		public const long PlayerFakeTo = long.MaxValue;

		public const long VesselFrom = 1000000L;

		public const long VesselTo = 999999999999L;

		public const long ObjectFrom = 1000L;

		public const long ObjectTo = 999999L;

		public const long SystemFrom = 100L;

		public const long SystemTo = 999L;
	}

	private static Random rand = new Random();

	public static long NextLongRandom(long min, long max)
	{
		byte[] buf = new byte[8];
		rand.NextBytes(buf);
		long longRand = BitConverter.ToInt64(buf, 0);
		return System.Math.Abs(longRand % (max - min)) + min;
	}

	public static long NextObjectGUID()
	{
		long newGUID;
		do
		{
			newGUID = NextLongRandom(1000L, 999999L);
		}
		while (Server.Instance.DoesObjectExist(newGUID));
		return newGUID;
	}

	public static long NextPlayerFakeGUID()
	{
		long newGUID;
		do
		{
			newGUID = NextLongRandom(1000000000000L, long.MaxValue);
		}
		while (Server.Instance.DoesObjectExist(newGUID));
		return newGUID;
	}

	public static long NextVesselGUID()
	{
		long newGUID;
		do
		{
			newGUID = NextLongRandom(1000000L, 999999999999L);
		}
		while (Server.Instance.DoesObjectExist(newGUID));
		return newGUID;
	}

	public static long PlayerIdToGuid(string steamId)
	{
		return 0x7FFFFFFFFFFFFFFFL & steamId.GetHashCode();
	}
}
