using System;
using System.Collections.Generic;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Outfit : Item
{
	public float InternalTemperature;

	public float ExternalTemperature;

	public float DamageReductionTorso;

	public float DamageReductionAbdomen;

	public float DamageReductionArms;

	public float DamageReductionLegs;

	public float DamageResistanceTorso = 1f;

	public float DamageResistanceAbdomen = 1f;

	public float DamageResistanceArms = 1f;

	public float DamageResistanceLegs = 1f;

	public float CollisionResistance = 1f;

	public override DynamicObjectStats StatsNew => null;

	public Dictionary<short, InventorySlot> InventorySlots { get; private set; }

	public Outfit()
	{
	}

	public Outfit(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		base.Armor *= base.TierMultiplier;
		OutfitData od = data as OutfitData;
		InventorySlots = new Dictionary<short, InventorySlot>();
		foreach (InventorySlotData isd in od.InventorySlots)
		{
			InventorySlots.Add(isd.SlotID, new InventorySlot(isd.SlotType, isd.SlotID, isd.ItemTypes, isd.MustBeEmptyToRemoveOutfit, this, null));
		}
		DamageReductionTorso = od.DamageReductionTorso;
		DamageReductionAbdomen = od.DamageReductionAbdomen;
		DamageReductionArms = od.DamageReductionArms;
		DamageReductionLegs = od.DamageReductionLegs;
		DamageResistanceTorso = od.DamageResistanceTorso;
		DamageResistanceAbdomen = od.DamageResistanceAbdomen;
		DamageResistanceArms = od.DamageResistanceArms;
		DamageResistanceLegs = od.DamageResistanceLegs;
		CollisionResistance = od.CollisionResistance;
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataOutfit data = new PersistenceObjectDataOutfit();
		FillPersistenceData(data);
		data.OutfitData = new OutfitData();
		FillBaseAuxData(data.OutfitData);
		data.OutfitData.InventorySlots = new List<InventorySlotData>();
		foreach (KeyValuePair<short, InventorySlot> slot in InventorySlots)
		{
			data.OutfitData.InventorySlots.Add(new InventorySlotData
			{
				SlotID = slot.Value.SlotID,
				SlotType = slot.Value.SlotType,
				ItemTypes = slot.Value.ItemTypes,
				MustBeEmptyToRemoveOutfit = slot.Value.MustBeEmptyToRemoveOutfit
			});
		}
		data.OutfitData.ItemType = Type;
		data.OutfitData.DamageReductionTorso = DamageReductionTorso;
		data.OutfitData.DamageReductionAbdomen = DamageReductionAbdomen;
		data.OutfitData.DamageReductionArms = DamageReductionArms;
		data.OutfitData.DamageReductionLegs = DamageReductionLegs;
		data.OutfitData.DamageResistanceTorso = DamageResistanceTorso;
		data.OutfitData.DamageResistanceAbdomen = DamageResistanceAbdomen;
		data.OutfitData.DamageResistanceArms = DamageResistanceArms;
		data.OutfitData.DamageResistanceLegs = DamageResistanceLegs;
		data.OutfitData.CollisionResistance = CollisionResistance;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			if (!(persistenceData is PersistenceObjectDataOutfit data))
			{
				Dbg.Warning("PersistenceObjectDataOutfit data is null", base.GUID);
			}
			else
			{
				SetData(data.OutfitData);
				base.LoadPersistenceData(persistenceData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
