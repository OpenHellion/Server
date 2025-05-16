using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Helmet : Item, IBatteryConsumer, IUpdateable
{
	private HelmetStats _stats = new();

	public bool IsVisorToggleable;

	public float HUDPowerConsumption;

	public float LightPowerConsumption;

	private bool _isVisorActive;

	private bool _isLightActive;

	public Jetpack Jetpack;

	public float DamageReduction;

	public float DamageResistance = 1f;

	public override DynamicObjectStats StatsNew => _stats;

	public ItemSlot BatterySlot { get; set; }

	public Battery Battery => BatterySlot != null ? BatterySlot.Item as Battery : null;

	public float BatteryPower => Battery != null ? MathHelper.Clamp(Battery.CurrentPower / Battery.MaxPower, 0f, 1f) : 0f;

	public bool IsVisorActive
	{
		get
		{
			return _isVisorActive;
		}
		set
		{
			_isVisorActive = value;
			_stats.isVisorActive = value;
		}
	}

	public bool IsLightActive
	{
		get
		{
			return _isLightActive;
		}
		set
		{
			_isLightActive = value;
			_stats.isLightActive = value;
		}
	}

	private Helmet()
	{
	}

	public static async Task<Helmet> CreateAsync(DynamicObjectAuxData data)
	{
		if (data == null)
		{
			return null;
		}
		Helmet helmet = new();
		await helmet.SetData(data);

		if (helmet.Slots != null)
		{
			helmet.BatterySlot = helmet.Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.FirstOrDefault((ItemType n) => n == ItemType.AltairHandDrillBattery) != ItemType.None).Value;
		}
		helmet.IsVisorActive = true;

		return helmet;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		HelmetData hd = data as HelmetData;
		IsLightActive = hd.IsLightActive;
		IsVisorActive = hd.IsVisorActive;
		HUDPowerConsumption = hd.HUDPowerConsumption;
		LightPowerConsumption = hd.LightPowerConsumption;
		IsVisorToggleable = hd.IsVisorToggleable;
		DamageReduction = hd.DamageReduction;
		DamageResistance = hd.DamageResistance;
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		HelmetStats hs = stats as HelmetStats;
		bool retVal = false;
		if (hs.isLightActive.HasValue && hs.isLightActive.Value != IsLightActive && (!hs.isLightActive.Value || BatteryPower > float.Epsilon))
		{
			IsLightActive = hs.isLightActive.Value;
			retVal = true;
		}
		if (IsVisorToggleable && hs.isVisorActive.HasValue && hs.isVisorActive.Value != IsVisorActive)
		{
			IsVisorActive = hs.isVisorActive.Value;
			retVal = true;
		}
		return Task.FromResult(retVal);
	}

	protected override void ChangeEquip(Inventory.EquipType equipType)
	{
		if (DynamicObj.Parent is not Player)
		{
			return;
		}
		Player pl = DynamicObj.Parent as Player;
		if (equipType == Inventory.EquipType.EquipInventory)
		{
			pl.CurrentHelmet = this;
			if (pl.CurrentJetpack != null)
			{
				Jetpack = pl.CurrentJetpack;
				Jetpack.Helmet = this;
			}
		}
		else if (pl.CurrentHelmet == this)
		{
			pl.CurrentHelmet = null;
			if (Jetpack != null)
			{
				Jetpack.Helmet = null;
				Jetpack = null;
			}
		}
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataHelmet data = new PersistenceObjectDataHelmet();
		FillPersistenceData(data);
		data.HelmetData = new HelmetData();
		FillBaseAuxData(data.HelmetData);
		data.HelmetData.IsVisorToggleable = IsVisorToggleable;
		data.HelmetData.IsLightActive = IsLightActive;
		data.HelmetData.IsVisorActive = IsVisorActive;
		data.HelmetData.LightPowerConsumption = LightPowerConsumption;
		data.HelmetData.HUDPowerConsumption = HUDPowerConsumption;
		data.HelmetData.DamageReduction = DamageReduction;
		data.HelmetData.DamageResistance = DamageResistance;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataHelmet data)
		{
			Debug.LogWarning("PersistenceObjectDataHelmet data is null", GUID);
		}
		else
		{
			await SetData(data.HelmetData);
		}
	}

	public async Task Update(double deltaTime)
	{
		if (Battery != null)
		{
			float cons = 0f;
			if (IsLightActive)
			{
				cons -= LightPowerConsumption * (float)deltaTime;
			}
			if (IsVisorActive)
			{
				cons -= HUDPowerConsumption * (float)deltaTime;
			}
			if (cons != 0f)
			{
				await Battery.ChangeQuantity(cons);
			}
		}
		if (BatteryPower < float.Epsilon && IsLightActive)
		{
			IsLightActive = false;
			await DynamicObj.SendStatsToClient();
		}
	}
}
