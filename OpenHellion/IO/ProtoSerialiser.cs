using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProtoBuf;
using ZeroGravity.Network;

namespace OpenHellion.IO;

/// <summary>
/// 	Class for deserialisation of network messages. Handles safe deserialisation of packets from the client.
/// </summary>
public static class ProtoSerialiser
{
	private class ZeroDataException : Exception
	{
		public ZeroDataException(string message)
			: base(message)
		{
		}
	}

	private class StatisticsHelper
	{
		public long ByteSum;

		public int PacketNumber;

		public long BytesSinceLastCheck;

		public StatisticsHelper(long bytes)
		{
			ByteSum = bytes;
			PacketNumber = 1;
			BytesSinceLastCheck = bytes;
		}
	}

	private static DateTime _statisticUpdateResetTime = DateTime.UtcNow;

	private static DateTime _lastStatisticUpdateTime;

	private static readonly double _statisticsLogUpdateTime = 1.0;

	private static readonly Dictionary<Type, StatisticsHelper> _sentStatistics =
		new Dictionary<Type, StatisticsHelper>();

	private static readonly Dictionary<Type, StatisticsHelper> _receivedStatistics =
		new Dictionary<Type, StatisticsHelper>();

	/// <summary>
	/// 	For deserialisation of data not sent through network.
	/// </summary>
	/// <exception cref="ArgumentNullException" />
	private static NetworkData Deserialize(Stream ms)
	{
		ArgumentNullException.ThrowIfNull(ms);
		NetworkData networkData;
		try
		{
			networkData = Serializer.Deserialize<NetworkData>(ms);
		}
		catch
		{
			return null;
		}

		ms.Position = 0L;

		if (_statisticsLogUpdateTime > 0.0)
		{
			try
			{
				ProcessStatistics(networkData, ms, _receivedStatistics);
				return networkData;
			}
			catch
			{
				return networkData;
			}
		}

		return networkData;
	}

	/// <summary>
	/// 	Unpack data from network.
	/// </summary>
	/// <param name="stream">The stream which data is sent. Usually a NetworkStream.</param>
	/// <returns>NetworkData</returns>
	/// <exception cref="ProtoException" />
	/// <exception cref="ZeroDataException" />
	/// <exception cref="ArgumentNullException" />
	public static async Task<NetworkData> Unpack(Stream stream)
	{
		int dataRead = 0;
		int readSize;

		byte[] dataLengthBuffer = new byte[4];
		do
		{
			readSize = await stream.ReadAsync(dataLengthBuffer.AsMemory(dataRead, dataLengthBuffer.Length - dataRead));
			if (readSize == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}

			dataRead += readSize;
		} while (dataRead < dataLengthBuffer.Length);

		uint dataLength = BitConverter.ToUInt32(dataLengthBuffer, 0);
		if (dataLength > 1000000)
		{
			Debug.Log("Received message with a payload of over 1MB.");
		}

		// Read following contents.
		byte[] buffer = new byte[dataLength];
		dataRead = 0;
		do
		{
			readSize = await stream.ReadAsync(buffer.AsMemory(dataRead, buffer.Length - dataRead));
			if (readSize == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}

			dataRead += readSize;
		} while (dataRead < buffer.Length);

		// Make the stream into NetworkData.
		MemoryStream ms = new MemoryStream(buffer, 0, buffer.Length);
		return Deserialize(ms);
	}

	/// <summary>
	/// 	Pack NetworkData into a binary array.
	/// </summary>
	/// <param name="data">NetworkData to serialise.</param>
	/// <returns>Data as a binary array.</returns>
	/// <exception cref="ProtoException" />
	/// <exception cref="ArgumentNullException" />
	public static async Task<byte[]> Pack(NetworkData data)
	{
		ArgumentNullException.ThrowIfNull(data);

		await using MemoryStream ms = new MemoryStream();

		try
		{
			Serializer.Serialize(ms, data);
		}
		catch
		{
			return null;
		}

		if (_statisticsLogUpdateTime > 0.0)
		{
			try
			{
				ProcessStatistics(data, ms, _sentStatistics);
			}
			catch
			{
				// Ignored.
			}
		}

		await using MemoryStream outMs = new MemoryStream();
		await outMs.WriteAsync(BitConverter.GetBytes((uint)ms.Length).AsMemory(0, 4));
		await outMs.WriteAsync(ms.ToArray().AsMemory(0, (int)ms.Length));
		await outMs.FlushAsync();
		return outMs.ToArray();
	}

	private static void ProcessStatistics(NetworkData data, Stream ms,
		Dictionary<Type, StatisticsHelper> stat)
	{
		Type type = data.GetType();
		if (stat.TryGetValue(type, out var value))
		{
			value.ByteSum += ms.Length;
			value.PacketNumber++;
			value.BytesSinceLastCheck += ms.Length;
		}
		else
		{
			stat[type] = new StatisticsHelper(ms.Length);
		}

		if (!(DateTime.UtcNow.Subtract(_lastStatisticUpdateTime).TotalSeconds >= _statisticsLogUpdateTime))
		{
			return;
		}

		TimeSpan timeSpan = DateTime.UtcNow.Subtract(_statisticUpdateResetTime);
		string text = (stat != _sentStatistics)
			? ("Received packets statistics (" + timeSpan.ToString("h':'mm':'ss") + "): \n")
			: ("Sent packets statistics (" + timeSpan.ToString("h':'mm':'ss") + "): \n");
		long num = 0L;
		string text2;
		foreach (KeyValuePair<Type, StatisticsHelper> item in stat
						.OrderBy((KeyValuePair<Type, StatisticsHelper> m) => m.Value.ByteSum).Reverse())
		{
			text2 = text;
			text = text2 + item.Key.Name + ": " + item.Value.PacketNumber + " (" +
					(item.Value.ByteSum / 1000f).ToString("##,0") + " kB), \n";
			item.Value.BytesSinceLastCheck = 0L;
			num += item.Value.ByteSum;
		}

		text2 = text;
		text = text2 + "-----------------------------------------\nTotal: " +
				(num / 1000f).ToString("##,0") + " kB (avg: " +
				(num / timeSpan.TotalSeconds / 1000.0).ToString("##,0") + " kB/s)";

		_lastStatisticUpdateTime = DateTime.UtcNow;
	}

	public static void ResetStatistics()
	{
		_sentStatistics.Clear();
		_receivedStatistics.Clear();
		_statisticUpdateResetTime = DateTime.UtcNow;
		_lastStatisticUpdateTime = DateTime.UtcNow;
	}
}
