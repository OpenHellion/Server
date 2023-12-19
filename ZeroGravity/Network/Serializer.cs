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
	private static NetworkData Deserialize(MemoryStream ms)
	{
		NetworkData networkData = null;
		ms.Position = 0L;
		try
		{
			networkData = ProtoBuf.Serializer.Deserialize<NetworkDataTransportWrapper>(ms).data;
		}
		catch (Exception ex)
		{
			Debug.Error("Failed to deserialize communication data", ex.Message, ex.StackTrace);
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
		if (soc is not { Connected: true })
		{
			return null;
		}
		return Unpackage(new NetworkStream(soc));
	}

	public static NetworkData Unpackage(Stream str)
	{
		byte[] bufferSize = new byte[4];
		int dataReadSize = 0;
		int size;
		do
		{
			size = str.Read(bufferSize, dataReadSize, bufferSize.Length - dataReadSize);
			if (size == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}
			dataReadSize += size;
		}
		while (dataReadSize < bufferSize.Length);
		uint bufferLength = BitConverter.ToUInt32(bufferSize, 0);
		byte[] buffer = new byte[bufferLength];
		dataReadSize = 0;
		do
		{
			size = str.Read(buffer, dataReadSize, buffer.Length - dataReadSize);
			if (size == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}
			dataReadSize += size;
		}
		while (dataReadSize < buffer.Length);
		MemoryStream ms = new MemoryStream(buffer, 0, buffer.Length);
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
			ProtoBuf.Serializer.Serialize(ms, ndtw);
		}
		catch (Exception ex)
		{
			Debug.Error("Failed to serialize communication data", ex.Message, ex.StackTrace);
			return null;
		}
		if (statisticsLogUpdateTime > 0.0)
		{
			try
			{
				ProcessStatistics(data, ms, sentStatistics);
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
		string printVal = stat != sentStatistics ? "Received packets statistics (" + timeFromReset.ToString("h':'mm':'ss") + "): \n" : "Sent packets statistics (" + timeFromReset.ToString("h':'mm':'ss") + "): \n";
		long totalBytes = 0L;
		foreach (KeyValuePair<Type, StatisticsHelper> kv in stat.OrderBy((KeyValuePair<Type, StatisticsHelper> m) => m.Value.ByteSum).Reverse())
		{
			printVal = printVal + kv.Key.Name + ": " + kv.Value.PacketNubmer + " (" + ((float)kv.Value.ByteSum / 1000f).ToString("##,0") + " kB), \n";
			kv.Value.BytesSinceLastCheck = 0L;
			totalBytes += kv.Value.ByteSum;
		}

		printVal = printVal + "-----------------------------------------\nTotal: " + ((float)totalBytes / 1000f).ToString("##,0") + " kB (avg: " + ((double)totalBytes / timeFromReset.TotalSeconds / 1000.0).ToString("##,0") + " kB/s)";

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
