using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace ZeroGravity.Network;

public class GameClientThread
{
	private volatile bool runThread;

	private Socket socket;

	private Thread listeningThread;

	private Thread sendingThread;

	private EventWaitHandle waitHandle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);

	private NetworkController.Client client;

	private ConcurrentQueue<NetworkData> NetworkDataQueue = new ConcurrentQueue<NetworkData>();

	private float timeOut;

	public GameClientThread(Socket soc, float time = 3f)
	{
		socket = soc;
		timeOut = time;
	}

	public void Send(NetworkData data)
	{
		NetworkDataQueue.Enqueue(data);
		waitHandle.Set();
	}

	public void ClearEverytingAndSend(NetworkData data)
	{
		NetworkDataQueue = new ConcurrentQueue<NetworkData>();
		NetworkDataQueue.Enqueue(data);
		waitHandle.Set();
	}

	public void Start()
	{
		runThread = true;
		sendingThread = new Thread(Send);
		sendingThread.IsBackground = true;
		sendingThread.Start();
		listeningThread = new Thread(Listen);
		listeningThread.IsBackground = true;
		listeningThread.Start();
	}

	public void Stop()
	{
		runThread = false;
		waitHandle.Set();
		if (socket != null)
		{
			socket.Close();
		}
		listeningThread.Interrupt();
		sendingThread.Interrupt();
	}

	// TODO: Broken.
	public void StopImmediate()
	{
		runThread = false;
		waitHandle.Set();
		if (socket != null)
		{
			socket.Close();
		}
		listeningThread.Interrupt();
		#pragma warning disable SYSLIB0006
		listeningThread.Abort();
		sendingThread.Interrupt();
		sendingThread.Abort();
		#pragma warning restore SYSLIB0006
	}

	private void Send()
	{
		while (Server.IsRunning && runThread)
		{
			try
			{
				waitHandle.WaitOne();
			}
			catch
			{
			}
			if (!Server.IsRunning || !runThread)
			{
				break;
			}
			NetworkData data = null;
			while (NetworkDataQueue.Count > 0)
			{
				try
				{
					NetworkDataQueue.TryDequeue(out data);
				}
				catch (Exception ex3)
				{
					Dbg.Info("Problem occured while dequeueing network data", ex3.Message, ex3.StackTrace);
					Disconnect();
					return;
				}
				if (data is LogOutResponse)
				{
					byte[] bytes = Serializer.Serialize(data);
					try
					{
						IAsyncResult ar = socket.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, null, null);
						if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeOut), exitContext: false))
						{
							throw new TimeoutException();
						}
					}
					catch (TimeoutException)
					{
					}
					catch (Exception ex2)
					{
						if (!(ex2 is SocketException) && !(ex2 is ObjectDisposedException))
						{
							Dbg.Error("SendToGameClient logout exception", (client != null && client.Player != null) ? client.Player.GUID.ToString() : "", ex2.Message, ex2.StackTrace);
						}
					}
					finally
					{
						Disconnect();
					}
					continue;
				}
				try
				{
					socket.Send(Serializer.Serialize(data));
				}
				catch (ArgumentNullException)
				{
					Dbg.Error("Serialized data buffer is null", data.GetType().ToString(), data);
				}
				catch (Exception ex)
				{
					if (!(ex is SocketException) && !(ex is ObjectDisposedException))
					{
						Dbg.Error("SendToGameClient exception", (client != null && client.Player != null) ? client.Player.GUID.ToString() : "", ex.Message, ex.StackTrace);
					}
					Disconnect();
					break;
				}
			}
		}
		if (client != null && client.Player != null)
		{
			Dbg.Info("Finished game client send thread", client.Player.GUID, client.Player.FakeGuid, client.Player.Name);
		}
		else
		{
			Dbg.Info("Finished game client send thread (no player)", client);
		}
	}

	private void Disconnect()
	{
		Server.Instance.NetworkController.DisconnectClient(client);
	}

	private void Listen()
	{
		client = Server.Instance.NetworkController.AddClient(socket, this);
		try
		{
			while (Server.IsRunning && runThread)
			{
				NetworkData data = Serializer.ReceiveData(socket);
				if (data != null)
				{
					if (client.Player != null)
					{
						data.Sender = client.Player.GUID;
					}
					else
					{
						data.Sender = Server.Instance.NetworkController.GetClientGuid(client);
					}
					Server.Instance.NetworkController.EventSystem.Invoke(data);
					continue;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
			Disconnect();
		}
		if (client != null && client.Player != null)
		{
			client.Player.RemovePlayerFromTrigger();
		}
	}
}
