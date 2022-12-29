using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

internal class RepairTool : Item, ICargo
{
	public float RepairAmount;

	public float UsageCooldown;

	public float Range;

	public CargoCompartmentData FuelCompartment;

	public float FuelConsumption;

	public bool Active;

	private List<CargoCompartmentData> _Compartments;

	private RepairToolStats _StatsNew = new RepairToolStats();

	private float maxFuel => FuelCompartment.Capacity;

	private float currentFuel => (FuelCompartment.Resources.Count > 0) ? FuelCompartment.Resources[0].Quantity : 0f;

	public float FreeSpace => FuelCompartment.Capacity - FuelCompartment.Resources.Sum((CargoResourceData m) => m.Quantity);

	public List<CargoCompartmentData> Compartments => _Compartments;

	public override DynamicObjectStats StatsNew => _StatsNew;

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		RepairToolStats rts = stats as RepairToolStats;
		if (rts.Active.HasValue)
		{
			Active = rts.Active.Value;
			_StatsNew.Active = Active;
		}
		base.DynamicObj.SendStatsToClient();
		return false;
	}

	public RepairTool(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data as RepairToolData);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		RepairToolData rtd = data as RepairToolData;
		RepairAmount = rtd.RepairAmount;
		UsageCooldown = rtd.UsageCooldown;
		Range = rtd.Range;
		FuelCompartment = rtd.FuelCompartment;
		FuelConsumption = rtd.FuelConsumption;
		_Compartments = new List<CargoCompartmentData> { FuelCompartment };
		_StatsNew.FuelResource = FuelCompartment.Resources[0];
		_StatsNew.Active = Active;
	}

	private RepairToolData GetData()
	{
		RepairToolData rtd = new RepairToolData
		{
			RepairAmount = RepairAmount,
			UsageCooldown = UsageCooldown,
			Range = Range,
			FuelCompartment = FuelCompartment,
			FuelConsumption = FuelConsumption
		};
		FillBaseAuxData(rtd);
		return rtd;
	}

	public void RepairVessel(VesselObjectID repairPointID)
	{
		SpaceObjectVessel vessel = Server.Instance.GetVessel(repairPointID.VesselGUID);
		if (vessel == null)
		{
			return;
		}
		VesselRepairPoint repairPoint = vessel.RepairPoints.Find((VesselRepairPoint m) => m.ID.Equals(repairPointID));
		if (repairPoint != null)
		{
			float repairAmount = RepairAmount;
			float fuelNeeded = repairAmount * FuelConsumption;
			if (fuelNeeded > currentFuel)
			{
				repairAmount = currentFuel / FuelConsumption;
			}
			float amount = ((repairPoint.MaxHealth - repairPoint.Health < repairAmount) ? (repairPoint.MaxHealth - repairPoint.Health) : repairAmount);
			vessel.ChangeHealthBy(amount);
			repairPoint.Health += amount;
			if (vessel.MaxHealth - vessel.Health < float.Epsilon)
			{
				repairPoint.Health = repairPoint.MaxHealth;
			}
			if (amount > 0f)
			{
				CargoResourceData res = FuelCompartment.Resources[0];
				ChangeQuantityBy(FuelCompartment.ID, res.ResourceType, (0f - amount) * FuelConsumption);
				_StatsNew.FuelResource = res;
				base.DynamicObj.SendStatsToClient();
			}
		}
	}

	public void RepairItem(long guid)
	{
		SpaceObject obj = Server.Instance.GetObject(guid);
		if (!(obj is DynamicObject))
		{
			return;
		}
		Item item = (obj as DynamicObject).Item;
		if (item != null)
		{
			float repairAmount = RepairAmount;
			float fuelNeeded = repairAmount * FuelConsumption;
			if (fuelNeeded > currentFuel)
			{
				repairAmount = currentFuel / FuelConsumption;
			}
			float oldHealth = item.Health;
			item.Health += repairAmount;
			float repairedAmount = item.Health - oldHealth;
			if (repairedAmount > 0f)
			{
				ConsumeFuel(repairedAmount * FuelConsumption);
			}
			item.DynamicObj.SendStatsToClient();
		}
	}

	public void ConsumeFuel(float amount)
	{
		CargoResourceData res = FuelCompartment.Resources[0];
		ChangeQuantityBy(FuelCompartment.ID, res.ResourceType, 0f - amount);
		_StatsNew.FuelResource = res;
		base.DynamicObj.SendStatsToClient();
		base.DynamicObj.SendStatsToClient();
	}

	public CargoCompartmentData GetCompartment(int? id = null)
	{
		if (id.HasValue)
		{
			return _Compartments.Find((CargoCompartmentData m) => m.ID == id.Value);
		}
		return _Compartments[0];
	}

	public float ChangeQuantityBy(int compartmentID, ResourceType resourceType, float quantity, bool wholeAmount = false)
	{
		CargoResourceData res = FuelCompartment.Resources.Find((CargoResourceData m) => m.ResourceType == resourceType);
		if (res == null)
		{
			res = new CargoResourceData
			{
				ResourceType = resourceType,
				Quantity = 0f
			};
			FuelCompartment.Resources.Add(res);
		}
		float freeSpace = FreeSpace;
		float qty = quantity;
		float resourceAvailable = res.Quantity;
		if (quantity > 0f && quantity > freeSpace)
		{
			qty = freeSpace;
		}
		else if (quantity < 0f && 0f - qty > resourceAvailable)
		{
			qty = 0f - resourceAvailable;
		}
		res.Quantity = resourceAvailable + qty;
		base.DynamicObj.StatsChanged = true;
		_StatsNew.FuelResource = GetCompartment(compartmentID).Resources[0];
		base.DynamicObj.SendStatsToClient();
		return qty;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataRepairTool data = new PersistenceObjectDataRepairTool();
		FillPersistenceData(data);
		data.RepairToolData = GetData();
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataRepairTool data))
			{
				Dbg.Warning("PersistenceObjectDataJetpack data is null", base.GUID);
			}
			else
			{
				SetData(data.RepairToolData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
