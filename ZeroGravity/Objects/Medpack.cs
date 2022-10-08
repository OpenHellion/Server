using System;
using System.Timers;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Medpack : Item
{
	public float RegenRate;

	public float MaxHp;

	private MedpackStats medStats;

	private Timer destroyTimer;

	public override DynamicObjectStats StatsNew => medStats;

	public Medpack(DynamicObjectAuxData data)
	{
		medStats = new MedpackStats();
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		MedpackData md = data as MedpackData;
		RegenRate = md.RegenRate;
		MaxHp = md.MaxHP;
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		MedpackStats ms = stats as MedpackStats;
		if (ms.Use)
		{
			if (base.DynamicObj.Parent is Player)
			{
				(base.DynamicObj.Parent as Player).Stats.HealOverTime(RegenRate, MaxHp / RegenRate);
			}
			base.DynamicObj.SendStatsToClient();
			destroyTimer = new Timer(2500.0);
			destroyTimer.Elapsed += delegate
			{
				DestroyItem();
			};
			destroyTimer.Enabled = true;
			return true;
		}
		return false;
	}

	public override void DestroyItem()
	{
		base.DestroyItem();
		if (destroyTimer != null)
		{
			destroyTimer.Dispose();
		}
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataMedpack data = new PersistenceObjectDataMedpack();
		FillPersistenceData(data);
		data.MedpackData = new MedpackData();
		FillBaseAuxData(data.MedpackData);
		data.MedpackData.MaxHP = MaxHp;
		data.MedpackData.RegenRate = RegenRate;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataMedpack data))
			{
				Dbg.Warning("PersistenceObjectDataMedpack data is null", base.GUID);
			}
			else
			{
				SetData(data.MedpackData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
