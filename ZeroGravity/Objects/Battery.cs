using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Battery : Item
{
	private readonly BatteryStats _stats = new();

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

	private Battery()
	{
	}

	public static async Task<Battery> CreateBatteryAsync(DynamicObjectAuxData data)
	{
		Battery battery = new();
		if (data != null)
		{
			await battery.SetData(data);
		}

		return battery;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		BatteryData bd = data as BatteryData;
		MaxPower = bd.MaxPower;
		CurrentPower = bd.CurrentPower;
		ApplyTierMultiplier();
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			MaxPower *= TierMultiplier;
			CurrentPower *= TierMultiplier;
		}
		base.ApplyTierMultiplier();
	}

	public async Task ChangeQuantity(float amount)
	{
		float prevPower = CurrentPower;
		CurrentPower = MathHelper.Clamp(CurrentPower + amount, 0f, MaxPower);
		if (CurrentPower == 0f || CurrentPower == MaxPower || (int)prevPower != (int)CurrentPower)
		{
			await DynamicObj.SendStatsToClient();
		}
		else
		{
			DynamicObj.StatsChanged = true;
		}
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(false);
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

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataBattery data)
		{
			Debug.LogWarning("PersistenceObjectDataBattery data is null", GUID);
		}
		else
		{
			await SetData(data.BatteryData);
		}
	}
}
