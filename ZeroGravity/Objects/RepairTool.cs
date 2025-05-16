using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

	private float currentFuel => FuelCompartment.Resources.Count > 0 ? FuelCompartment.Resources[0].Quantity : 0f;

	public float FreeSpace => FuelCompartment.Capacity - FuelCompartment.Resources.Sum((CargoResourceData m) => m.Quantity);

	public List<CargoCompartmentData> Compartments => _Compartments;

	public override DynamicObjectStats StatsNew => _StatsNew;

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		RepairToolStats rts = stats as RepairToolStats;
		if (rts.Active.HasValue)
		{
			Active = rts.Active.Value;
			_StatsNew.Active = Active;
		}
		await DynamicObj.SendStatsToClient();
		return false;
	}

	private RepairTool()
	{
	}

	public static async Task<RepairTool> CreateAsync(DynamicObjectAuxData data)
	{
		RepairTool repairTool = new();
		if (data != null)
		{
			await repairTool.SetData(data);
		}

		return repairTool;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
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

	public async Task RepairVessel(VesselObjectID repairPointID)
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
			float amount = repairPoint.MaxHealth - repairPoint.Health < repairAmount ? repairPoint.MaxHealth - repairPoint.Health : repairAmount;
			await vessel.ChangeHealthBy(amount);
			await repairPoint.SetHealthAsync(repairPoint.Health + amount);
			if (vessel.MaxHealth - vessel.Health < float.Epsilon)
			{
				await repairPoint.SetHealthAsync(repairPoint.MaxHealth);
			}
			if (amount > 0f)
			{
				CargoResourceData res = FuelCompartment.Resources[0];
				await ChangeQuantityByAsync(FuelCompartment.ID, res.ResourceType, (0f - amount) * FuelConsumption);
				_StatsNew.FuelResource = res;
				await DynamicObj.SendStatsToClient();
			}
		}
	}

	public async Task RepairItem(long guid)
	{
		SpaceObject obj = Server.Instance.GetObject(guid);
		if (obj is not DynamicObject dynamicObject)
		{
			return;
		}
		Item item = dynamicObject.Item;
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
				await ConsumeFuel(repairedAmount * FuelConsumption);
			}
			await item.DynamicObj.SendStatsToClient();
		}
	}

	public async Task ConsumeFuel(float amount)
	{
		CargoResourceData res = FuelCompartment.Resources[0];
		await ChangeQuantityByAsync(FuelCompartment.ID, res.ResourceType, 0f - amount);
		_StatsNew.FuelResource = res;
		await DynamicObj.SendStatsToClient();
		await DynamicObj.SendStatsToClient();
	}

	public CargoCompartmentData GetCompartment(int? id = null)
	{
		if (id.HasValue)
		{
			return _Compartments.Find((CargoCompartmentData m) => m.ID == id.Value);
		}
		return _Compartments[0];
	}

	public async Task<float> ChangeQuantityByAsync(int compartmentID, ResourceType resourceType, float quantity, bool wholeAmount = false)
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
		DynamicObj.StatsChanged = true;
		_StatsNew.FuelResource = GetCompartment(compartmentID).Resources[0];
		await DynamicObj.SendStatsToClient();
		return qty;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataRepairTool data = new PersistenceObjectDataRepairTool();
		FillPersistenceData(data);
		data.RepairToolData = GetData();
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataRepairTool data)
		{
			Debug.LogWarning("PersistenceObjectDataJetpack data is null", GUID);
		}
		else
		{
			await SetData(data.RepairToolData);
		}
	}
}
