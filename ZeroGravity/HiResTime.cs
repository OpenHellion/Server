using System.Runtime.InteropServices;

namespace ZeroGravity;

public static class HiResTime
{
	private static bool msTimerSupported;

	private static uint msHigh = 0u;

	private static uint msLastLow = 0u;

	public static ulong Milliseconds
	{
		get
		{
			if (!msTimerSupported)
			{
				return GetTickCount64();
			}
			uint msTickCount = timeGetTime();
			if (msTickCount < msLastLow)
			{
				msHigh++;
			}
			msLastLow = msTickCount;
			return msTickCount | ((ulong)msHigh << 32);
		}
	}

	[DllImport("kernel32")]
	public static extern ulong GetTickCount64();

	[DllImport("winmm.dll")]
	private static extern int timeBeginPeriod(int msec);

	[DllImport("winmm.dll")]
	private static extern int timeEndPeriod(int msec);

	[DllImport("winmm.dll")]
	private static extern uint timeGetTime();

	[DllImport("Kernel32")]
	private static extern bool QueryPerformanceFrequency(out ulong freq);

	[DllImport("Kernel32")]
	private static extern bool QueryPerformanceCounter(out ulong count);

	public static void Start()
	{
		msTimerSupported = timeBeginPeriod(1) == 0;
	}

	public static void Stop()
	{
		if (msTimerSupported)
		{
			timeEndPeriod(1);
		}
	}
}
