using System;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Battery : Item
{
	private BatteryStats _stats;

	private float _CurrentPower;

	private float _MaxPower;

	public override DynamicObjectStats StatsNew => _stats;

	public bool HasPower => CurrentPower > float.Epsilon;

	public float CurrentPower
	{
		get
		{
			return _CurrentPower;
		}
		set
		{
			_CurrentPower = value;
			_stats.CurrentPower = value;
		}
	}

	public float MaxPower
	{
		get
		{
			return _MaxPower;
		}
		set
		{
			_MaxPower = value;
			_stats.MaxPower = value;
		}
	}

	public float ChargeAmount => 1f;

	public Battery(DynamicObjectAuxData data)
	{
		_stats = new BatteryStats();
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		BatteryData bd = data as BatteryData;
		MaxPower = bd.MaxPower;
		CurrentPower = bd.CurrentPower;
		ApplyTierMultiplier();
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			MaxPower *= base.TierMultiplier;
			CurrentPower *= base.TierMultiplier;
		}
		base.ApplyTierMultiplier();
	}

	public void ChangeQuantity(float amount)
	{
		float prevPower = CurrentPower;
		CurrentPower = MathHelper.Clamp(CurrentPower + amount, 0f, MaxPower);
		if (CurrentPower == 0f || CurrentPower == MaxPower || (int)prevPower != (int)CurrentPower)
		{
			base.DynamicObj.SendStatsToClient();
		}
		else
		{
			base.DynamicObj.StatsChanged = true;
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataBattery data = new PersistenceObjectDataBattery();
		FillPersistenceData(data);
		data.BatteryData = new BatteryData();
		FillBaseAuxData(data.BatteryData);
		data.BatteryData.CurrentPower = CurrentPower;
		data.BatteryData.MaxPower = MaxPower;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (persistenceData is not PersistenceObjectDataBattery data)
			{
				Debug.Warning("PersistenceObjectDataBattery data is null", base.GUID);
			}
			else
			{
				SetData(data.BatteryData);
			}
		}
		catch (Exception e)
		{
			Debug.Exception(e);
		}
	}
}
