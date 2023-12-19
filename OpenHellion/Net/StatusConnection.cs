using System;
using System.Net.Sockets;
using System.Threading;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Net;

public class StatusConnection
{
	private readonly Socket _socket;

	private Thread _listeningThread;

	public StatusConnection(Socket soc)
	{
		_socket = soc;
	}

	public void Start()
	{
		_listeningThread = new Thread(Listen);
		_listeningThread.IsBackground = true;
		_listeningThread.Start();

		Debug.Log("Started server status thread.");
	}

	public void Stop()
	{
		_socket.Close();
	}

	private void Listen()
	{
		try
		{
			NetworkData data = Serializer.ReceiveData(_socket);
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
						if (!NetworkController.ContainsClient(pl.GUID))
						{
							pl.Destroy();
						}
					}

					break;
				}
				case LatencyTestMessage:
					try
					{
						_socket.Send(Serializer.Package(data));
					}
					catch (ArgumentNullException)
					{
						Debug.Error("Serialized data buffer is null", data.GetType().ToString(), data);
						throw;
					}

					break;
			}
		}
		catch (Exception ex)
		{
			Debug.Exception(ex);
		}
	}
}
