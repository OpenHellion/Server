using ZeroGravity.Math;

namespace ZeroGravity;

public class DoomedShipController : IPersistantObject
{
	public static double DestroyTimerMinSec = 1800.0;

	public static double DestroyTimerMaxSec = 7200.0;

	public static double SpawnFrequencySec = 10800.0;

	public static double SpawnChance = 0.5;

	public static double AdditionalSpawnChance1 = 0.75;

	public static double AdditionalSpawnChance2 = 0.75;

	private double spawnTimer;

	public void SubscribeToTimer()
	{
		if (SpawnFrequencySec > 60.0)
		{
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_min, UpdateTimerCallback);
		}
		else
		{
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_0_5_sec, UpdateTimerCallback);
		}
	}

	private void UpdateTimerCallback(double deltaTime)
	{
		spawnTimer += deltaTime;
		if (spawnTimer > SpawnFrequencySec)
		{
			if (MathHelper.RandomNextDouble() <= SpawnChance)
			{
				SpawnDoomedShip();
			}
			spawnTimer = 0.0;
		}
	}

	public void SpawnDoomedShip()
	{
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceDataDoomController data = new PersistenceDataDoomController();
		data.SpawnTimer = spawnTimer;
		return data;
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		if (!(persistenceData is PersistenceDataDoomController data))
		{
			Dbg.Warning("PersistenceDataDoomController wrong type");
		}
		else
		{
			spawnTimer = data.SpawnTimer;
		}
	}
}
