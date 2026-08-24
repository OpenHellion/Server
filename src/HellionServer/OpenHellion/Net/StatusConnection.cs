using System;
using System.Net.Sockets;
using System.Threading;
using OpenHellion.IO;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Net;

public class StatusConnection
{
	private const int MAX_MESSAGE_SIZE = 100000;

	private readonly Socket _socket;

	private Thread _listeningThread;

	public StatusConnection(Socket soc)
	{
		_socket = soc;
	}

	public void Start()
	{
		_listeningThread = new Thread(Listen)
		{
			IsBackground = true
		};
		_listeningThread.Start();
	}

	public void Stop()
	{
		_socket.Close();
	}

	private async void Listen()
	{
		try
		{
			NetworkData data = await ProtoSerialiser.Unpack(new NetworkStream(_socket), MAX_MESSAGE_SIZE);
			switch (data)
			{
				case null:
					return;
				case ServerShutDownMessage msg:
				{
					string ipAddress = _socket.RemoteEndPoint.ToString().Split(":".ToCharArray(), 2)[0];
					if (Server.Instance.IsAddressAutorized(ipAddress))
					{
						Server.Restart = msg.Restrat;
						Server.CleanRestart = msg.CleanRestart;
						Server.SavePersistenceDataOnShutdown = (!Server.Restart && Server.PersistenceSaveInterval > 0.0) || (Server.Restart && !Server.CleanRestart);
						Server.IsRunning = false;
					}
					return;
				}
				case DeleteCharacterRequest dcr:
				{
					if (dcr.ServerId == NetworkController.ServerId)
					{
						Player pl = Server.Instance.GetPlayerFromPlayerId(dcr.PlayerId);
						if (!NetworkController.IsPlayerConnected(pl.Guid))
						{
							await pl.Destroy();
						}
					}

					break;
				}
				case LatencyTestMessage:
					try
					{
						_socket.Send(await ProtoSerialiser.Pack(data));
					}
					catch (ArgumentNullException)
					{
						Debug.LogError("Serialized data buffer is null", data.GetType().ToString(), data);
						throw;
					}

					break;
			}
		}
		catch (SocketException)
		{
			Stop();
			Debug.LogError("Error when trying to listen to status connection, socket failed.");
		}
	}
}
