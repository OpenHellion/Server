using System;
using System.Diagnostics;
using System.IO;

public static class Debug
{
	public static string OutputDir = "";

	public const bool AddTimestamp = true;

	public const string TimestampFormat = "HH:mm:ss.ffff";

	public static void Initialize()
	{
		string fileName = OutputDir + "output_log.txt";
		try
		{
			if (File.Exists(fileName))
			{
				string backupFileName = OutputDir + "output_log_backup.txt";
				if (File.Exists(backupFileName))
				{
					File.Delete(backupFileName);
				}
				File.Move(fileName, backupFileName);
			}
		}
		catch
		{
			// ignored
		}

		Trace.Listeners.Clear();

		// Logging to console creates issues for the client if it is singleplayer. Plus it is pointless.
		ConsoleTraceListener consoleListener = new ConsoleTraceListener(useErrorStream: false)
		{
			TraceOutputOptions = TraceOptions.None
		};
		Trace.Listeners.Add(consoleListener);

		TextWriterTraceListener writerListener = new TextWriterTraceListener(new StreamWriter(fileName, append: false))
		{
			TraceOutputOptions = TraceOptions.None
		};
		Trace.Listeners.Add(writerListener);

		Trace.AutoFlush = true;
	}

	public static void Destroy()
	{
		Trace.Listeners.Clear();
	}

	private static void WriteToLog(string message)
	{
		try
		{
			Trace.WriteLine($"{(AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + " -" ) : "")} {message}");
		}
		catch
		{
			// ignored
		}
	}

	private static string GetString(object value)
	{
		return value != null ? value.ToString() : "NULL";
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void Log(string message)
	{
		WriteToLog($"[Debug] {message}");
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void LogFormat(string message, params object[] args)
	{
		WriteToLog(string.Format($"[Debug] {message}", args));
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void Log(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog($"[Debug] {GetString(values[0])}");
		}
		else
		{
			WriteToLog($"[Debug] {string.Join(" ", values)}");
		}
	}

	public static void UnformattedMessage(string message)
	{
		Trace.WriteLine(message);
	}

	public static void LogInfo(string message)
	{
		WriteToLog($"[Info] {message}");
	}

	public static void LogInfoFormat(string message, params object[] values)
	{
		WriteToLog(string.Format($"[Info] {message}", values));
	}

	public static void LogInfo(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog($"[Info] {GetString(values[0])}");
		}
		else
		{
			WriteToLog($"[Info] {string.Join(" ", values)}");
		}
	}

	public static void LogWarning(string message)
	{
		WriteToLog("[Warn] " + message);
	}

	public static void LogWarningFormat(string message, params object[] values)
	{
		WriteToLog(string.Format($"[Warn] {message}", values));
	}

	public static void LogWarning(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog("[Warn] " + GetString(values[0]));
		}
		else
		{
			WriteToLog("[Warn] " + string.Join(" ", values));
		}
	}

	public static void LogError(string message)
	{
		WriteToLog($"[Error] {message}");
	}

	public static void LogErrorFormat(string message, params object[] values)
	{
		WriteToLog(string.Format($"[Error] {message}", values));
	}


	public static void LogError(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog("[Error] " + GetString(values[0]));
		}
		else
		{
			WriteToLog("[Error] " + string.Join(" ", values));
		}
	}

	public static void LogException(Exception ex)
	{
		WriteToLog($"[Exception] {ex}");
	}
}
