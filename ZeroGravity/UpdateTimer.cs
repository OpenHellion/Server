namespace ZeroGravity;

public class UpdateTimer
{
	public enum TimerStep
	{
		Step_0_1_sec = 1,
		Step_0_5_sec,
		Step_1_0_sec,
		Step_1_0_min,
		Step_15_0_min,
		Step_1_0_hr
	}

	public delegate void TimeStepDelegate(double dbl);

	private double timePassed;

	private double updateInverval;

	public TimeStepDelegate OnTick;

	public TimerStep Step { get; private set; }

	public UpdateTimer(TimerStep type)
	{
		Step = type;
		switch (type)
		{
		case TimerStep.Step_0_1_sec:
			updateInverval = 0.1;
			break;
		case TimerStep.Step_0_5_sec:
			updateInverval = 0.5;
			break;
		case TimerStep.Step_1_0_sec:
			updateInverval = 1.0;
			break;
		case TimerStep.Step_1_0_min:
			updateInverval = 60.0;
			break;
		case TimerStep.Step_15_0_min:
			updateInverval = 900.0;
			break;
		case TimerStep.Step_1_0_hr:
			updateInverval = 3600.0;
			break;
		}
	}

	public void AddTime(double deltaTime)
	{
		timePassed += deltaTime;
		if (timePassed > updateInverval)
		{
			if (OnTick != null)
			{
				OnTick(timePassed);
			}
			timePassed = 0.0;
		}
	}
}
