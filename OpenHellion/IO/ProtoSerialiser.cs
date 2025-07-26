using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
	private static NetworkData Deserialize(Stream ms)
	{
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
	/// 	Unpack data sent by the server.
	/// 	Reads the size of the message, then reads the message itself.
	/// </summary>
	/// <param name="stream">The stream to read from.</param>
	/// <param name="maxMessageSize">Max number of bytes to accept.</param>
	/// <returns></returns>
	/// <exception cref="ArgumentException">If message is too large.</exception>
	/// <exception cref="ZeroDataException">If message is empty.</exception>
	public static async Task<NetworkData> Unpack(Stream stream, int maxMessageSize, CancellationToken cancellationToken = default)
	{
		int dataRead = 0;
		int readSize;

		byte[] dataLengthBuffer = new byte[4];
		do
		{
			readSize = await stream.ReadAsync(dataLengthBuffer.AsMemory(dataRead, dataLengthBuffer.Length - dataRead), cancellationToken);
			if (readSize == 0)
			{
				throw new ZeroDataException("Received zero data message.");
			}

			dataRead += readSize;
		} while (dataRead < dataLengthBuffer.Length);

		uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(dataLengthBuffer);
		if (dataLength > maxMessageSize)
		{
				await SkipAsync(stream, dataLength, cancellationToken);

				throw new ArgumentException($"Message too large. Declared {dataLength}, maximum allowed is {maxMessageSize}.");
		}

		// Read following contents.
		byte[] buffer = new byte[dataLength];
		dataRead = 0;
		do
		{
			readSize = await stream.ReadAsync(buffer.AsMemory(dataRead, buffer.Length - dataRead), cancellationToken);
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
	public static async Task<byte[]> Pack(NetworkData data, CancellationToken cancellationToken = default)
	{
		await using MemoryStream ms = new MemoryStream();

		try
		{
			Serializer.Serialize(ms, data);
		}
		catch (Exception ex)
		{
			Debug.LogException(ex);
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
		await outMs.WriteAsync(BitConverter.GetBytes((uint)ms.Length).AsMemory(0, 4), cancellationToken);
		await outMs.WriteAsync(ms.ToArray().AsMemory(0, (int)ms.Length), cancellationToken);
		await outMs.FlushAsync(cancellationToken);
		return outMs.ToArray();
	}

	/// <summary>
	/// 	Skips a specified number of bytes in the stream asynchronously.
	/// </summary>
	/// <param name="stream">Stream to skip on.</param>
	/// <param name="bytesToSkip">Bytes to skip.</param>
	/// <param name="cancellationToken"></param>
	/// <exception cref="EndOfStreamException">Stream ended unexpectedly.</exception>
	public static async Task SkipAsync(Stream stream, long bytesToSkip, CancellationToken cancellationToken = default)
	{
		if (bytesToSkip <= 0) return;

		// If the stream supports seeking, do it in one go
		if (stream.CanSeek)
		{
			long toSkip = Math.Min(stream.Length - stream.Position, bytesToSkip);
			stream.Seek(toSkip, SeekOrigin.Current);
			return;
		}

		// Otherwise, read-and-discard in chunks
		const int discardBufferSize = 8192;
		byte[] discardBuffer = new byte[discardBufferSize];
		long remaining = bytesToSkip;
		while (remaining > 0)
		{
			int chunk = (int)Math.Min(discardBufferSize, remaining);
			int read = await stream.ReadAsync(discardBuffer.AsMemory(0, chunk), cancellationToken);
			if (read == 0)
			{
				// Stream ended prematurely
				throw new EndOfStreamException(
					$"Stream ended while skipping {bytesToSkip} bytes (skipped {bytesToSkip - remaining}).");
			}
			remaining -= read;
		}
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
