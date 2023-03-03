using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

public static class Dbg
{
	public static string OutputDir = "";

	public static bool AddTimestamp = true;

	public static string TimestampFormat = "yyyy/MM/dd HH:mm:ss.ffff";

	public static string TimestampSeparator = " - ";

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
		}
		Trace.Listeners.Clear();

#if !HELLION_SP
		// Logging to console creates issues for the client if it is singleplayer. Plus it is pointless.
		ConsoleTraceListener consoleListener = new ConsoleTraceListener(useErrorStream: false)
		{
			TraceOutputOptions = TraceOptions.None
		};
		Trace.Listeners.Add(consoleListener);
#endif

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
			StackFrame frame = new StackTrace(2, fNeedFileInfo: true).GetFrame(0);
			Trace.WriteLine($"{message}\r\n\t{Path.GetFileName(frame.GetFileName())} Ln {frame.GetFileLineNumber()} Col {frame.GetFileColumnNumber()}\r\n------------------------------------------------------------------------------");
		}
		catch
		{
		}
	}

	private static string GetString(object value)
	{
		return (value != null) ? value.ToString() : "NULL";
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void Log(string message)
	{
		WriteToLog(message);
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void Log(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog(GetString(values[0]));
		}
		else
		{
			WriteToLog(string.Join(" ", values));
		}
	}

	[Conditional("DEBUG")]
	[Conditional("SHOW_ALL_LOGS")]
	public static void LogIf(bool condition, string message)
	{
		if (condition)
		{
			WriteToLog(message);
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
		WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + message);
	}

	public static void Info(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + GetString(values[0]));
		}
		else
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + string.Join(" ", values));
		}
	}

	public static void Warning(string message)
	{
		WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[WARNING] " + message);
	}

	public static void Warning(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[WARNING] " + GetString(values[0]));
		}
		else
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[WARNING] " + string.Join(" ", values));
		}
	}

	public static void Error(string message)
	{
		WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[ERROR] " + message);
	}

	public static void Error(params object[] values)
	{
		if (values.Length == 1)
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[ERROR] " + GetString(values[0]));
		}
		else
		{
			WriteToLog((AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[ERROR] " + string.Join(" ", values));
		}
	}

	public static void Exception(Exception ex)
	{
		string msg = (AddTimestamp ? DateTime.UtcNow.ToString(TimestampFormat + TimestampSeparator) : "") + "[ERROR] " + ex.Message + "\r\n" + ex.StackTrace;
		if (ex.InnerException != null)
		{
			msg = msg + "\r\nInner Exception:" + ex.InnerException.Message + "\r\n" + ex.InnerException.StackTrace;
		}
		WriteToLog(msg);
	}
}
