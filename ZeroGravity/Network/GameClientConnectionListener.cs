using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ZeroGravity.Network;

public class GameClientConnectionListener
{
	private volatile bool runThread;

	private Thread listeningThread;

	private TcpListener tcpListener;

	public void Stop()
	{
		runThread = false;
		tcpListener.Stop();
		listeningThread.Interrupt();
	}

	// TODO: Broken.
	public void StopImmidiate()
	{
		if (listeningThread.IsAlive)
		{
			runThread = false;
			tcpListener.Stop();
			listeningThread.Interrupt();
			#pragma warning disable SYSLIB0006
			listeningThread.Abort();
			#pragma warning restore SYSLIB0006
		}
	}

	public void Start(int port)
	{
		runThread = true;
#if HELLION_SP
		tcpListener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port));
#else
		tcpListener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
#endif
		tcpListener.Start();
		listeningThread = new Thread(Listen);
		listeningThread.IsBackground = true;
		listeningThread.Start();
	}

	private void Listen()
	{
		try
		{
			while (Server.IsRunning && runThread)
			{
				Socket soc = tcpListener.AcceptSocket();
				if (!runThread)
				{
					break;
				}
				GameClientThread client = new GameClientThread(soc);
				client.Start();
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
		Dbg.Info("Finished game client connection listener thread", Server.IsRunning, runThread);
	}
}
