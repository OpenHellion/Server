using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class HandDrill : Item
{
	private HandDrillStats _stats;

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

	public bool CanDrill => Battery != null && Battery.CurrentPower > float.Epsilon && Canister.HasSpace && DrillBit != null && DrillBit.Health > float.Epsilon;

	public HandDrill(DynamicObjectAuxData data)
	{
		_stats = new HandDrillStats();
		if (data == null)
		{
			return;
		}
		HandDrillData hd = data as HandDrillData;
		SetData(hd);
		if (Slots != null)
		{
			batterySlot = Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.Contains(ItemType.AltairHandDrillBattery)).Value;
			canisterSlot = Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.Contains(ItemType.AltairHandDrillCanister)).Value;
			drillBitSlot = Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.GenericSubTypes.Contains(GenericItemSubType.DiamondCoreDrillBit)).Value;
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		HandDrillData hd = data as HandDrillData;
		BatteryUsage = hd.BatteryConsumption;
		DrillingStrength = hd.DrillingStrength;
	}

	public override void SendAllStats()
	{
		if (Battery != null && Battery.DynamicObj.StatsChanged)
		{
			Battery.DynamicObj.SendStatsToClient();
		}
		if (Canister != null && Canister.DynamicObj.StatsChanged)
		{
			Canister.DynamicObj.SendStatsToClient();
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		HandDrillStats hds = stats as HandDrillStats;
		if (hds.InAsteroidGUID.HasValue)
		{
			InAsteroidGUID = hds.InAsteroidGUID.Value;
		}
		return true;
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataHandDrill data))
			{
				Dbg.Warning("PersistenceObjectDataHandDrill data is null", base.GUID);
			}
			else
			{
				SetData(data.HandDrillData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
