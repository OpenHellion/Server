using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public class Jetpack : Item, ICargo
{
	private Dictionary<int, float> ResourceChangedCounter = new Dictionary<int, float>();

	private CargoCompartmentData OxygenCompartment;

	private CargoCompartmentData PropellantCompartment;

	public float OxygenConsumption;

	public float PropellantConsumption;

	public Helmet Helmet;

	private List<CargoCompartmentData> _Compartments;

	public override DynamicObjectStats StatsNew => new JetpackStats
	{
		Oxygen = ((OxygenCompartment.Resources != null && OxygenCompartment.Resources.Count > 0) ? OxygenCompartment.Resources[0] : null),
		OxygenCapacity = OxygenCompartment.Capacity,
		Propellant = ((PropellantCompartment.Resources != null && PropellantCompartment.Resources.Count > 0) ? PropellantCompartment.Resources[0] : null),
		PropellantCapacity = PropellantCompartment.Capacity
	};

	public bool HasFuel => currentFuel > float.Epsilon;

	public bool HasOxygen => currentOxygen > float.Epsilon;

	private float maxFuel => PropellantCompartment.Capacity;

	private float currentFuel => (PropellantCompartment.Resources.Count > 0) ? PropellantCompartment.Resources[0].Quantity : 0f;

	private float maxOxygen => OxygenCompartment.Capacity;

	private float currentOxygen => (OxygenCompartment.Resources.Count > 0) ? OxygenCompartment.Resources[0].Quantity : 0f;

	public List<CargoCompartmentData> Compartments => _Compartments;

	public float ChargeAmount => 1f;

	public Jetpack(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
	}

	protected override void ChangeEquip(Inventory.EquipType equipType)
	{
		if (!(base.DynamicObj.Parent is Player))
		{
			return;
		}
		Player pl = base.DynamicObj.Parent as Player;
		if (equipType == Inventory.EquipType.EquipInventory)
		{
			pl.CurrentJetpack = this;
			if (pl.CurrentHelmet != null)
			{
				Helmet = pl.CurrentHelmet;
				Helmet.Jetpack = this;
			}
		}
		else if (pl.CurrentJetpack == this)
		{
			pl.CurrentJetpack = null;
			if (Helmet != null)
			{
				Helmet.Jetpack = null;
				Helmet = null;
			}
		}
	}

	public void ConsumeResources(float? propellant = null, float? oxygen = null)
	{
		if (oxygen.HasValue && oxygen.Value > 0f && OxygenCompartment.Resources.Count > 0)
		{
			ChangeQuantityBy(OxygenCompartment.ID, OxygenCompartment.Resources[0].ResourceType, 0f - oxygen.Value);
		}
		if (propellant.HasValue && propellant.Value > 0f && PropellantCompartment.Resources.Count > 0)
		{
			ChangeQuantityBy(PropellantCompartment.ID, PropellantCompartment.Resources[0].ResourceType, 0f - propellant.Value);
		}
	}

	public static bool CanChangeValue(float currentAmount, float amount, float min, float max)
	{
		return (amount.IsNotEpsilonZero() && (!(amount < 0f) || !currentAmount.IsEpsilonEqual(min))) || (amount > 0f && currentAmount.IsEpsilonEqual(max));
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
		CargoCompartmentData compartment = Compartments.Find((CargoCompartmentData m) => m.ID == compartmentID);
		CargoResourceData res = compartment.Resources.Find((CargoResourceData m) => m.ResourceType == resourceType);
		if (res == null)
		{
			res = new CargoResourceData
			{
				ResourceType = resourceType,
				Quantity = 0f
			};
			compartment.Resources.Add(res);
		}
		float freeSpace = compartment.Capacity - compartment.Resources.Sum((CargoResourceData m) => m.Quantity);
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
		if (ResourceChangedCounter.ContainsKey(compartmentID))
		{
			ResourceChangedCounter[compartmentID] += qty;
		}
		else
		{
			ResourceChangedCounter[compartmentID] = qty;
		}
		if (System.Math.Abs(ResourceChangedCounter[compartmentID]) / compartment.Capacity >= 0.01f)
		{
			base.DynamicObj.SendStatsToClient();
			ResourceChangedCounter[compartmentID] = 0f;
		}
		if (res.Quantity <= float.Epsilon && !compartment.AllowOnlyOneType)
		{
			compartment.Resources.Remove(res);
		}
		return qty;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataJetpack data = new PersistenceObjectDataJetpack();
		FillPersistenceData(data);
		data.JetpackData = GetJetpackData();
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataJetpack data))
			{
				Dbg.Warning("PersistenceObjectDataJetpack data is null", base.GUID);
			}
			else
			{
				SetData(data.JetpackData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		JetpackData jd = data as JetpackData;
		PropellantCompartment = jd.PropellantCompartment;
		OxygenCompartment = jd.OxygenCompartment;
		ApplyTierMultiplier();
		_Compartments = new List<CargoCompartmentData> { OxygenCompartment, PropellantCompartment };
		OxygenConsumption = jd.OxygenConsumption;
		PropellantConsumption = jd.PropellantConsumption;
		base.MaxHealth = jd.MaxHealth;
		base.Health = jd.Health;
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			PropellantCompartment.Capacity *= base.TierMultiplier;
			OxygenCompartment.Capacity *= base.AuxValue;
		}
		base.ApplyTierMultiplier();
	}

	private JetpackData GetJetpackData()
	{
		JetpackData jd = new JetpackData
		{
			OxygenCompartment = OxygenCompartment,
			PropellantCompartment = PropellantCompartment,
			OxygenConsumption = OxygenConsumption,
			PropellantConsumption = PropellantConsumption
		};
		FillBaseAuxData(jd);
		return jd;
	}
}
