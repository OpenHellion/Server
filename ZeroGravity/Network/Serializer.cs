using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using ProtoBuf;

namespace ZeroGravity.Network;

/// <summary>
/// 	Class for deserialisation of network messages. Handles safe deserialsation of packets from the server.
/// </summary>
public static class Serializer
{
	public class ZeroDataException : Exception
	{
		public ZeroDataException(string message)
			: base(message)
		{
		}
	}

	public class StatisticsHelper
	{
		public long ByteSum;

		public int PacketNubmer;

		public long BytesSinceLastCheck;

		public StatisticsHelper(long bytes)
		{
			ByteSum = bytes;
			PacketNubmer = 1;
			BytesSinceLastCheck = bytes;
		}
	}

	private static DateTime statisticUpdateResetTime = DateTime.UtcNow;

	private static DateTime lastStatisticUpdateTime;

	private static double statisticsLogUpdateTime = 1.0;

	private static Dictionary<Type, StatisticsHelper> sentStatistics = new Dictionary<Type, StatisticsHelper>();

	private static Dictionary<Type, StatisticsHelper> receivedStatistics = new Dictionary<Type, StatisticsHelper>();

	/// <summary>
	/// 	For deserialisation of data not sent through network.
	/// </summary>
	public static NetworkData Deserialize(MemoryStream ms)
	{
		NetworkData networkData = null;
		ms.Position = 0L;
		try
		{
			networkData = ProtoBuf.Serializer.Deserialize<NetworkDataTransportWrapper>(ms).data;
		}
		catch (Exception ex)
		{
			Dbg.Error("Failed to deserialize communication data", ex.Message, ex.StackTrace);
		}
		if (statisticsLogUpdateTime > 0.0)
		{
			try
			{
				ProcessStatistics(networkData, ms, receivedStatistics);
				return networkData;
			}
			catch
			{
				return networkData;
			}
		}
		return networkData;
	}

	public static NetworkData ReceiveData(Socket soc)
	{
		if (soc == null || !soc.Connected)
		{
			return null;
		}
		return Unpackage(new NetworkStream(soc));
	}

	public static NetworkData Unpackage(Stream str)
	{
		byte[] array = new byte[4];
		int num2 = 0;
		int num;
		do
		{
			num = str.Read(array, num2, array.Length - num2);
			if (num == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}
			num2 += num;
		}
		while (num2 < array.Length);
		uint num3 = BitConverter.ToUInt32(array, 0);
		byte[] array2 = new byte[num3];
		num2 = 0;
		do
		{
			num = str.Read(array2, num2, array2.Length - num2);
			if (num == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}
			num2 += num;
		}
		while (num2 < array2.Length);
		MemoryStream ms = new MemoryStream(array2, 0, array2.Length);
		return Deserialize(ms);
	}

	public static byte[] Package(NetworkData data)
	{
		using MemoryStream outMs = new MemoryStream();
		using MemoryStream ms = new MemoryStream();
		try
		{
			NetworkDataTransportWrapper ndtw = new NetworkDataTransportWrapper
			{
				data = data
			};
			ProtoBuf.Serializer.Serialize(outMs, ndtw);
		}
		catch (Exception ex)
		{
			Dbg.Error("Failed to serialize communication data", ex.Message, ex.StackTrace);
			return null;
		}
		if (statisticsLogUpdateTime > 0.0)
		{
			try
			{
				ProcessStatistics(data, outMs, sentStatistics);
			}
			catch
			{
			}
		}
		outMs.Write(BitConverter.GetBytes((uint)ms.Length), 0, 4);
		outMs.Write(ms.ToArray(), 0, (int)ms.Length);
		outMs.Flush();
		return outMs.ToArray();
	}

	private static void ProcessStatistics(NetworkData data, MemoryStream ms, Dictionary<Type, StatisticsHelper> stat)
	{
		Type packetType = data.GetType();
		if (stat.TryGetValue(packetType, out var sh))
		{
			sh.ByteSum += ms.Length;
			sh.PacketNubmer++;
			sh.BytesSinceLastCheck += ms.Length;
		}
		else
		{
			stat[packetType] = new StatisticsHelper(ms.Length);
		}
		if (!(DateTime.UtcNow.Subtract(lastStatisticUpdateTime).TotalSeconds >= statisticsLogUpdateTime))
		{
			return;
		}
		TimeSpan timeFromReset = DateTime.UtcNow.Subtract(statisticUpdateResetTime);
		string printVal = ((stat != sentStatistics) ? ("Received packets statistics (" + timeFromReset.ToString("h':'mm':'ss") + "): \n") : ("Sent packets statistics (" + timeFromReset.ToString("h':'mm':'ss") + "): \n"));
		long totalBytes = 0L;
		foreach (KeyValuePair<Type, StatisticsHelper> kv in stat.OrderBy((KeyValuePair<Type, StatisticsHelper> m) => m.Value.ByteSum).Reverse())
		{
			printVal = printVal + kv.Key.Name + ": " + kv.Value.PacketNubmer + " (" + ((float)kv.Value.ByteSum / 1000f).ToString("##,0") + " kB), \n";
			kv.Value.BytesSinceLastCheck = 0L;
			totalBytes += kv.Value.ByteSum;
		}
		lastStatisticUpdateTime = DateTime.UtcNow;
	}

	public static void ResetStatistics()
	{
		sentStatistics.Clear();
		receivedStatistics.Clear();
		statisticUpdateResetTime = DateTime.UtcNow;
		lastStatisticUpdateTime = DateTime.UtcNow;
	}
}
