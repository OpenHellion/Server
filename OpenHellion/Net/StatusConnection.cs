using System;
using System.Net.Sockets;
using System.Threading;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Networking;

public class StatusConnection
{
	private Socket socket;

	private Thread listeningThread;

	public StatusConnection(Socket soc)
	{
		socket = soc;
	}

	public void Start()
	{
		listeningThread = new Thread(Listen);
		listeningThread.IsBackground = true;
		listeningThread.Start();

		Dbg.Log("Started server status thread.");
	}

	public void Stop()
	{
		socket.Close();
	}

	private void Listen()
	{
		try
		{
			NetworkData data = Serializer.ReceiveData(socket);
			if (data == null)
			{
				return;
			}
			else if (data is ServerShutDownMessage msg)
			{
				string ipAddress = socket.RemoteEndPoint.ToString().Split(":".ToCharArray(), 2)[0];
				if (Server.Instance.IsAddressAutorized(ipAddress))
				{
					Server.Restart = msg.Restrat;
					Server.CleanRestart = msg.CleanRestart;
					Server.SavePersistenceDataOnShutdown = (!Server.Restart && Server.PersistenceSaveInterval > 0.0) || (Server.Restart && !Server.CleanRestart);
					Server.IsRunning = false;
				}
				return;
			}
			else if (data is ServerStatusRequest req)
			{
				ServerStatusResponse ssr = Server.Instance.GetServerStatusResponse(req);
				try
				{
					socket.Send(Serializer.Package(ssr));
					return;
				}
				catch (ArgumentNullException)
				{
					Dbg.Error("Serialized data buffer is null", req.GetType().ToString(), req);
					throw;
				}
			}
			else if (data is DeleteCharacterRequest dcr)
			{
				if (dcr.ServerId == NetworkController.ServerId)
				{
					Player pl = Server.Instance.GetPlayerFromPlayerId(dcr.PlayerId);
					if (!NetworkController.Instance.ContainsClient(pl.GUID))
					{
						pl.Destroy();
					}
				}
			}
			else if (data is LatencyTestMessage)
			{
				try
				{
					socket.Send(Serializer.Package(data));
					return;
				}
				catch (ArgumentNullException)
				{
					Dbg.Error("Serialized data buffer is null", data.GetType().ToString(), data);
					throw;
				}
			}
		}
		catch (Exception)
		{
		}
	}
}
