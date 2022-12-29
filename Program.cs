using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using ZeroGravity;
using ZeroGravity.Network;

public static class Program
{
	private static void ProcessExit(object sender, EventArgs e)
	{
		Console.WriteLine("Exiting safely...");
		Server.IsRunning = false;
		if (Server.PersistenceSaveInterval > 0.0)
		{
			Server.SavePersistenceDataOnShutdown = true;
		}
		Server.MainLoopEnded.WaitOne(10000);
	}

	private static void Main(string[] args)
	{
		// Error handlers.
		AppDomain.CurrentDomain.ProcessExit += new EventHandler(ProcessExit);
		Console.CancelKeyPress += new ConsoleCancelEventHandler(ProcessExit);
#if HELLION_SP
		if (!ParentProcess.FileName.ToLower().Contains("hellion") && !ParentProcess.FileName.ToLower().Contains("unity"))
		{
			//Process.Start("steam://run/588210");

			Dbg.Error("This executable is for creating the single player server, and has to be executed by either the Unity Editor or the Hellion distributable. That means you can't launch this manually.");
			return;
		}

		// Parse arguments.
		Server.InitProperties(args);
#else
		bool shutdown = false;
		string gport = null;
		string sport = null;
		string randomships = null;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].ToLower() == "-configdir" && args.Length > i + 1)
			{
				Server.ConfigDir = args[++i];
				if (!Server.ConfigDir.EndsWith("/"))
				{
					Server.ConfigDir += "/";
				}
			}
			else if (args[i].ToLower() == "-clean" || args[i].ToLower() == "-noload")
			{
				Server.CleanStart = true;
			}
			else if (args[i].ToLower() == "-load" && args.Length > i + 1)
			{
				Server.LoadPersistenceFromFile = args[++i];
			}
			else if (args[i].ToLower() == "-gport" && args.Length > i + 1)
			{
				gport = args[++i];
			}
			else if (args[i].ToLower() == "-sport" && args.Length > i + 1)
			{
				sport = args[++i];
			}
			else if (args[i].ToLower() == "-randomships" && args.Length > i + 1)
			{
				randomships = args[++i];
			}
			else if (args[i].ToLower() == "-shutdown")
			{
				shutdown = true;
			}
			else if (args[i].ToLower() == "-scan")
			{
				ScanInstances();
				Environment.Exit(0);
			}
			else if (args[i].ToLower() == "-cleanup")
			{
				CleanupAllInstances();
				Environment.Exit(0);
			}
		}
		Server.Properties = new Properties(Server.ConfigDir + "GameServer.ini");
		if (gport != null)
		{
			Server.Properties.SetProperty("game_client_port", gport);
		}
		if (sport != null)
		{
			Server.Properties.SetProperty("status_port", sport);
		}
		if (randomships != null)
		{
			Server.Properties.SetProperty("spawn_random_ships_count", randomships);
		}
		if (shutdown)
		{
			ShutdownServerInstance();
			return;
		}

		if (!File.Exists(Server.ConfigDir + "GameServer.ini"))
		{
			Console.WriteLine("GameServer.ini not found in folder " + Server.ConfigDir);
			return;
		}

		CheckIniFields();
#endif

		Dbg.OutputDir = Server.ConfigDir;
		Dbg.Initialize();
		AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
		try
		{
			// Start the server.
			Server server = new Server();
			HiResTime.Start();
			server.MainLoop();
			HiResTime.Stop();
		}
		catch (Exception ex)
		{
			HiResTime.Stop();
			Dbg.UnformattedMessage("******************** MAIN EXCEPTION ********************");
			Dbg.Exception(ex);
		}
	}

	private static void CheckIniFields()
	{
		try
		{
			if (Server.Properties.GetProperty<string>("server_name").Trim().IsNullOrEmpty())
			{
				throw new Exception();
			}
		}
		catch
		{
			Console.Out.WriteLine("Invalid 'server_name' field.");
			Environment.Exit(0);
		}
		try
		{
			Server.Properties.GetProperty<ushort>("game_client_port");
		}
		catch
		{
			Console.Out.WriteLine("Invalid 'game_client_port' field.");
			Environment.Exit(0);
		}
		try
		{
			Server.Properties.GetProperty<ushort>("status_port");
		}
		catch
		{
			Console.Out.WriteLine("Invalid 'status_port' field.");
			Environment.Exit(0);
		}
	}

	private static void CleanupAllInstances()
	{
		DirectoryInfo dinfo = new DirectoryInfo(".");
		foreach (FileInfo file2 in dinfo.EnumerateFiles("*.save"))
		{
			file2.Delete();
		}
		string[] directories = Directory.GetDirectories(".");
		foreach (string dir in directories)
		{
			string configDir = Path.GetFileName(dir);
			string path = configDir + "/GameServer.ini";
			if (!File.Exists(path))
			{
				continue;
			}
			dinfo = new DirectoryInfo(configDir);
			foreach (FileInfo file in dinfo.EnumerateFiles("*.save"))
			{
				file.Delete();
			}
		}
	}

	private static void ScanInstances()
	{
		DirectoryInfo dinfo = new DirectoryInfo(".");
		foreach (FileInfo file2 in dinfo.EnumerateFiles("Start_*.bat"))
		{
			file2.Delete();
		}
		foreach (FileInfo file in dinfo.EnumerateFiles("Stop_*.bat"))
		{
			file.Delete();
		}
		string exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
		string startAll = "start cmd /c call \"Start_DEFAULT.bat\"\r\n";
		string stopAll = "start cmd /c call \"Stop_DEFAULT.bat\"\r\n";
		File.WriteAllText("Start_DEFAULT.bat", "@TITLE Game Server: DEFAULT \r\n@" + exe);
		File.WriteAllText("Stop_DEFAULT.bat", exe + " -shutdown");
		string[] directories = Directory.GetDirectories(".");
		foreach (string dir in directories)
		{
			string configDir = Path.GetFileName(dir);
			string path = configDir + "/GameServer.ini";
			if (File.Exists(path))
			{
				string start = "@TITLE Game Server: " + configDir + "\r\n@" + exe + " -configdir \"" + configDir + "\"";
				string stop = start + " -shutdown";
				string startBatFile = "Start_" + configDir + ".bat";
				string stopBatFile = "Stop_" + configDir + ".bat";
				File.WriteAllText(startBatFile, start);
				File.WriteAllText(stopBatFile, stop);
				startAll = startAll + "start cmd /c call \"" + startBatFile + "\"\r\n";
				stopAll = stopAll + "start cmd /c call \"" + stopBatFile + "\"\r\n";
			}
		}
		File.WriteAllText("Start_ALL.bat", startAll);
		File.WriteAllText("Stop_ALL.bat", stopAll);
	}

	private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
	{
		try
		{
			Dbg.UnformattedMessage("******************** UNHANDLED EXCEPTION ********************");
			Dbg.Exception((Exception)args.ExceptionObject);
		}
		catch
		{
			File.WriteAllText(Server.ConfigDir + "unhandled_exception.txt", ((Exception)args.ExceptionObject).Message + ", " + ((Exception)args.ExceptionObject).StackTrace);
		}
		finally
		{
			Environment.Exit(1);
		}
	}

	private static void ShutdownServerInstance()
	{
		int StatusPort = Server.Properties.GetProperty<int>("status_port");
		if (StatusPort <= 0)
		{
			return;
		}
		try
		{
			TcpClient tcpClient = new TcpClient();
			IAsyncResult ar = tcpClient.BeginConnect("127.0.0.1", StatusPort, null, null);
			WaitHandle wh = ar.AsyncWaitHandle;
			try
			{
				if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2.0), exitContext: false))
				{
					tcpClient.Close();
					throw new TimeoutException();
				}
				tcpClient.EndConnect(ar);
			}
			finally
			{
				wh.Close();
			}
			NetworkStream ns = null;
			try
			{
				ns = tcpClient.GetStream();
			}
			catch
			{
				return;
			}
			ns.ReadTimeout = 1000;
			ns.WriteTimeout = 1000;
			ServerShutDownMessage msg = new ServerShutDownMessage();
			byte[] buffer = Serializer.Serialize(msg);
			ns.Write(buffer, 0, buffer.Length);
			ns.Flush();
			ns.Close();
			tcpClient.Close();
		}
		catch
		{
		}
	}
}
