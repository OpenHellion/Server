using System;
using System.Diagnostics;
using System.IO;

public static class Debug
{
	public static string OutputDir = "";

	public const bool AddTimestamp = true;

	public const string TimestampFormat = "yyyy/MM/dd HH:mm:ss.ffff";

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

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void LogIf(bool condition, string message)
	{
		if (condition)
		{
			WriteToLog($"[Debug] {message}");
		}
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void LogIf(bool condition, params object[] values)
	{
		if (condition)
		{
			Log(values);
		}
	}

	public static void UnformattedMessage(string message)
	{
		Trace.WriteLine(message);
	}

	public static void Info(string message)
	{
		WriteToLog($"[Info] {message}");
	}

	public static void Info(params object[] values)
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

	public static void Warning(string message)
	{
		WriteToLog("[Warn] " + message);
	}

	public static void Warning(params object[] values)
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

	public static void Error(string message)
	{
		WriteToLog($"[Error] {message}");
	}

	public static void Error(params object[] values)
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

	public static void Exception(Exception ex)
	{
		WriteToLog($"[Exception] {ex}");
	}
}
