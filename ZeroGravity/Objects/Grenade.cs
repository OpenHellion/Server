using System;
using System.Timers;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Grenade : Item
{
	private GrenadeStats gs;

	private bool isActive;

	private float detonationTime;

	public long PlayerGUID;

	private double activationTime;

	private Timer destroyTimer;

	private bool isCanceled;

	public override DynamicObjectStats StatsNew => gs;

	public Grenade(DynamicObjectAuxData data)
	{
		gs = new GrenadeStats();
		if (data != null)
		{
			SetData(data as GrenadeData);
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		GrenadeStats gstats = stats as GrenadeStats;
		if (gstats.IsActive.HasValue && gstats.IsActive.Value != isActive)
		{
			PlayerGUID = base.DynamicObj.Parent.GUID;
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
		return false;
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
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
		destroyTimer.Elapsed += delegate
		{
			Blast();
		};
		destroyTimer.Enabled = true;
	}

	private void Blast()
	{
		if (base.DynamicObj.Parent != null)
		{
			if (!isActive || isCanceled || (base.Health > float.Epsilon && (activationTime == -1.0 || Server.Instance.SolarSystem.CurrentTime - activationTime < (double)(detonationTime * 0.9f))))
			{
				isCanceled = false;
				activationTime = -1.0;
			}
			else
			{
				gs.Blast = true;
				base.DynamicObj.SendStatsToClient();
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataGrenade data))
			{
				Dbg.Warning("PersistenceObjectDataHandheldGrenade data is null", base.GUID);
			}
			else
			{
				SetData(data.GrenadeData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
