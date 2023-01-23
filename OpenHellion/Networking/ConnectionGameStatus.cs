using System;
using System.Net.Sockets;
using System.Threading;
using ZeroGravity;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace OpenHellion.Networking;

public class ConnectionGameStatus
{
	private Socket socket;

	private Thread listeningThread;

	public ConnectionGameStatus(Socket soc)
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
			string address = socket.RemoteEndPoint.ToString().Split(":".ToCharArray(), 2)[0];
			if (data is ServerShutDownMessage)
			{
				if (Server.Instance.IsAddressAutorized(address))
				{
					ServerShutDownMessage msg = data as ServerShutDownMessage;
					Server.Restart = msg.Restrat;
					Server.CleanRestart = msg.CleanRestart;
#if HELLION_SP
					Server.SavePersistenceDataOnShutdown = false;
#else
					Server.SavePersistenceDataOnShutdown = (!Server.Restart && Server.PersistenceSaveInterval > 0.0) || (Server.Restart && !Server.CleanRestart);
#endif
					Server.IsRunning = false;
				}
				return;
			}
			if (data is ServerStatusRequest)
			{
				ServerStatusRequest req = data as ServerStatusRequest;
				ServerStatusResponse ssr = Server.Instance.GetServerStatusResponse(req);
				try
				{
					socket.Send(Serializer.Package(ssr));
					return;
				}
				catch (ArgumentNullException)
				{
					Dbg.Error("Serialized data buffer is null", data.GetType().ToString(), data);
					throw;
				}
			}
			if (data is DeleteCharacterRequest)
			{
				DeleteCharacterRequest dcr = data as DeleteCharacterRequest;
				if (dcr.ServerId == NetworkController.ServerID)
				{
					Player pl = Server.Instance.GetPlayerFromPlayerId(dcr.SteamId);
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
