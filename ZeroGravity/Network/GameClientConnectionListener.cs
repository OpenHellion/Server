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

	public void StopImmidiate()
	{
		if (listeningThread.IsAlive)
		{
			runThread = false;
			tcpListener.Stop();
			listeningThread.Interrupt();
			listeningThread.Abort();
		}
	}

	public void Start(int port)
	{
		runThread = true;
		tcpListener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
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
