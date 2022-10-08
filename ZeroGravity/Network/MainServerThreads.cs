using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ZeroGravity.Network;

public class MainServerThreads
{
	public void Send(NetworkData data)
	{
		Task.Run(delegate
		{
			NetworkData data2 = data;
			try
			{
				Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				socket.Connect(Server.Instance.NetworkController.MainServerAddres, Server.Instance.NetworkController.MainServerPort);
				Stream stream = new NetworkStream(socket);
				byte[] array = Serializer.Serialize(data2);
				stream.Write(array, 0, array.Length);
				stream.Flush();
				try
				{
					NetworkData networkData = Serializer.ReceiveData(stream);
					if (networkData != null)
					{
						Server.Instance.NetworkController.EventSystem.Invoke(networkData);
					}
				}
				catch (SocketException ex)
				{
					Dbg.Error("Connection broken", ex.Message, ex.StackTrace);
				}
				catch (Serializer.ZeroDataException)
				{
				}
			}
			catch (Exception ex3)
			{
				Dbg.Error("Unable to connect to main server", ex3.Message, ex3.StackTrace);
				if (!Server.CheckInPassed)
				{
					Server.IsRunning = false;
					Server.Instance.UpdateDataFinished.Set();
				}
			}
		});
	}
}
