using System;
using System.Threading.Tasks;
using System.Timers;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Grenade : Item
{
	private GrenadeStats gs = new GrenadeStats();

	private bool isActive;

	private float detonationTime;

	public long PlayerGUID;

	private double activationTime;

	private Timer destroyTimer;

	private bool isCanceled;

	public override DynamicObjectStats StatsNew => gs;

	private Grenade()
	{
	}

	public static async Task<Grenade> CreateAsync(DynamicObjectAuxData data)
	{
		Grenade grenade = new();
		if (data != null)
		{
			await grenade.SetData(data);
		}

		return grenade;
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		GrenadeStats gstats = stats as GrenadeStats;
		if (gstats.IsActive.HasValue && gstats.IsActive.Value != isActive)
		{
			PlayerGUID = DynamicObj.Parent.Guid;
			if (isActive && gstats.IsActive == false)
			{
				isCanceled = true;
				activationTime = -1.0;
				destroyTimer.Dispose();
			}
			gs.IsActive = isActive = gstats.IsActive.Value;
			if (gs.IsActive == true)
			{
				isCanceled = false;
				activationTime = Server.Instance.SolarSystem.CurrentTime;
				CallBlastAfterTime();
			}
		}
		return Task.FromResult(false);
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		GrenadeData i = data as GrenadeData;
		gs.IsActive = i.IsActive;
		detonationTime = i.DetonationTime;
		if (i.IsActive)
		{
			CallBlastAfterTime();
		}
	}

	public void CallBlastAfterTime(double? time = null)
	{
		destroyTimer = new Timer(TimeSpan.FromSeconds(detonationTime).TotalMilliseconds);
		destroyTimer.Elapsed += async delegate
		{
			await Blast();
		};
		destroyTimer.Enabled = true;
	}

	private async Task Blast()
	{
		if (DynamicObj.Parent != null)
		{
			if (!isActive || isCanceled || (Health > float.Epsilon && (activationTime == -1.0 || Server.Instance.SolarSystem.CurrentTime - activationTime < detonationTime * 0.9f)))
			{
				isCanceled = false;
				activationTime = -1.0;
			}
			else
			{
				gs.Blast = true;
				await DynamicObj.SendStatsToClient();
			}
		}
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataGrenade data = new PersistenceObjectDataGrenade();
		FillPersistenceData(data);
		data.GrenadeData = new GrenadeData();
		FillBaseAuxData(data.GrenadeData);
		data.GrenadeData.DetonationTime = detonationTime;
		data.GrenadeData.IsActive = false;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataGrenade data)
		{
			Debug.LogWarning("PersistenceObjectDataHandheldGrenade data is null", GUID);
		}
		else
		{
			await SetData(data.GrenadeData);
		}
	}
}
