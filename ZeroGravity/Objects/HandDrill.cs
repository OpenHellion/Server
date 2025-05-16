using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class HandDrill : Item
{
	private HandDrillStats _stats = new();

	private ItemSlot batterySlot;

	private ItemSlot canisterSlot;

	private ItemSlot drillBitSlot;

	private double _inAsteroidDistanceSqr;

	private Asteroid _inAsteroidObj;

	private long _inAsteroidGUID;

	public override DynamicObjectStats StatsNew => _stats;

	public Battery Battery => batterySlot != null ? batterySlot.Item as Battery : null;

	public Canister Canister => canisterSlot != null ? canisterSlot.Item as Canister : null;

	public Item DrillBit => drillBitSlot != null ? drillBitSlot.Item as GenericItem : null;

	public long InAsteroidGUID
	{
		get
		{
			return _inAsteroidGUID;
		}
		set
		{
			if (value > 0)
			{
				_inAsteroidObj = Server.Instance.GetObject(value) as Asteroid;
			}
			else
			{
				_inAsteroidObj = null;
			}
			_inAsteroidDistanceSqr = _inAsteroidObj != null ? System.Math.Pow(_inAsteroidObj.Radius + 100.0, 2.0) : 0.0;
			_inAsteroidGUID = value;
			_stats.InAsteroidGUID = value;
		}
	}

	public float BatteryUsage { get; private set; }

	public float DrillingStrength { get; private set; }

	public bool CanDrill => Battery is { CurrentPower: > float.Epsilon } && Canister.HasSpace && DrillBit is
	{
		Health: > float.Epsilon
	};

	private HandDrill()
	{
	}

		public static async Task<HandDrill> CreateAsync(DynamicObjectAuxData data)
	{
		HandDrill handDrill = new();
		if (data != null)
		{
			await handDrill.SetData(data);
		}

		if (handDrill.Slots != null)
		{
			handDrill.batterySlot = handDrill.Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.Contains(ItemType.AltairHandDrillBattery)).Value;
			handDrill.canisterSlot = handDrill.Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.Contains(ItemType.AltairHandDrillCanister)).Value;
			handDrill.drillBitSlot = handDrill.Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.GenericSubTypes.Contains(GenericItemSubType.DiamondCoreDrillBit)).Value;
		}

		return handDrill;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		HandDrillData hd = data as HandDrillData;
		BatteryUsage = hd.BatteryConsumption;
		DrillingStrength = hd.DrillingStrength;
	}

	public override async Task SendAllStats()
	{
		if (Battery != null && Battery.DynamicObj.StatsChanged)
		{
			await Battery.DynamicObj.SendStatsToClient();
		}
		if (Canister != null && Canister.DynamicObj.StatsChanged)
		{
			await Canister.DynamicObj.SendStatsToClient();
		}
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		HandDrillStats hds = stats as HandDrillStats;
		if (hds.InAsteroidGUID.HasValue)
		{
			InAsteroidGUID = hds.InAsteroidGUID.Value;
		}
		return Task.FromResult(true);
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataHandDrill data = new PersistenceObjectDataHandDrill();
		FillPersistenceData(data);
		data.HandDrillData = new HandDrillData();
		FillBaseAuxData(data.HandDrillData);
		data.HandDrillData.BatteryConsumption = BatteryUsage;
		data.HandDrillData.DrillingStrength = DrillingStrength;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataHandDrill data)
		{
			Debug.LogWarning("PersistenceObjectDataHandDrill data is null", GUID);
		}
		else
		{
			await SetData(data.HandDrillData);
		}
	}
}
