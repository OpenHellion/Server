using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

internal class Canister : Item, ICargo
{
	private CargoCompartmentData cargoCompartment;

	private List<CargoCompartmentData> _compartments;

	private Dictionary<ResourceType, float> ResourceChangedCounter = new Dictionary<ResourceType, float>();

	public override DynamicObjectStats StatsNew => new CanisterStats
	{
		Resources = new List<CargoResourceData>(cargoCompartment.Resources),
		Capacity = cargoCompartment.Capacity
	};

	public bool HasSpace => FreeSpace > float.Epsilon;

	public float FreeSpace => cargoCompartment.Capacity - cargoCompartment.Resources.Sum((CargoResourceData m) => m.Quantity);

	public List<CargoCompartmentData> Compartments => _compartments;

	public Canister(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		SetCanisterData((data as CanisterData).CargoCompartment);
		ApplyTierMultiplier();
	}

	private void SetCanisterData(CargoCompartmentData compartmetData)
	{
		cargoCompartment = compartmetData;
		_compartments = new List<CargoCompartmentData> { cargoCompartment };
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			cargoCompartment.Capacity *= base.TierMultiplier;
		}
		base.ApplyTierMultiplier();
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		if (!(stats is CanisterStats data))
		{
			return false;
		}
		if (data.UseCanister.HasValue && data.UseCanister.Value && base.DynamicObj.Parent is Player)
		{
			Player player = base.DynamicObj.Parent as Player;
			if (player.CurrentJetpack != null)
			{
				foreach (CargoCompartmentData compJ in player.CurrentJetpack.Compartments)
				{
					foreach (CargoCompartmentData com in Compartments)
					{
						List<CargoResourceData> forRemoval = new List<CargoResourceData>();
						foreach (CargoResourceData resC in com.Resources)
						{
							if (resC.Quantity > 0f && compJ.AllowedResources.Contains(resC.ResourceType) && compJ.AllowOnlyOneType)
							{
								CargoResourceData resJ = compJ.Resources.Find((CargoResourceData x) => x.ResourceType == resC.ResourceType);
								bool control = false;
								if (resJ == null)
								{
									control = true;
									resJ = new CargoResourceData();
									resJ.ResourceType = resC.ResourceType;
								}
								float availableCapacity = compJ.Capacity - resJ.Quantity;
								if (resC.Quantity <= availableCapacity)
								{
									resJ.Quantity += resC.Quantity;
									resC.Quantity = 0f;
									forRemoval.Add(resC);
								}
								else
								{
									resJ.Quantity += availableCapacity;
									resC.Quantity -= availableCapacity;
								}
								if (control)
								{
									compJ.Resources.Add(resJ);
								}
							}
						}
						foreach (CargoResourceData remRes in forRemoval)
						{
							com.Resources.Remove(remRes);
						}
					}
				}
				base.DynamicObj.SendStatsToClient();
				player.CurrentJetpack.DynamicObj.SendStatsToClient();
			}
		}
		return false;
	}

	public void ChangeQuantity(Dictionary<ResourceType, float> newResources)
	{
		foreach (KeyValuePair<ResourceType, float> res in newResources)
		{
			changeQuantityBy(res.Key, res.Value, sendStats: false);
		}
		base.DynamicObj.SendStatsToClient();
	}

	private float changeQuantityBy(ResourceType resourceType, float quantity, bool sendStats = true)
	{
		CargoResourceData res = cargoCompartment.Resources.Find((CargoResourceData m) => m.ResourceType == resourceType);
		if (res == null)
		{
			res = new CargoResourceData
			{
				ResourceType = resourceType,
				Quantity = 0f
			};
			cargoCompartment.Resources.Add(res);
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
		if (ResourceChangedCounter.ContainsKey(resourceType))
		{
			ResourceChangedCounter[resourceType] += qty;
		}
		else
		{
			ResourceChangedCounter[resourceType] = qty;
		}
		if (res.Quantity <= float.Epsilon)
		{
			cargoCompartment.Resources.Remove(res);
		}
		base.DynamicObj.StatsChanged = true;
		if (System.Math.Abs(ResourceChangedCounter[resourceType] / cargoCompartment.Capacity) >= 0.01f)
		{
			base.DynamicObj.SendStatsToClient();
			ResourceChangedCounter[resourceType] = 0f;
		}
		return qty;
	}

	public CargoCompartmentData GetCompartment(int? id = null)
	{
		if (id.HasValue)
		{
			return _compartments.Find((CargoCompartmentData m) => m.ID == id.Value);
		}
		return _compartments[0];
	}

	public float ChangeQuantityBy(int compartmentID, ResourceType resourceType, float quantity, bool wholeAmount = false)
	{
		return changeQuantityBy(resourceType, quantity);
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataCanister data = new PersistenceObjectDataCanister();
		FillPersistenceData(data);
		data.Compartment = cargoCompartment;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataCanister data))
			{
				Dbg.Warning("PersistenceObjectDataCanister data is null", base.GUID);
			}
			else
			{
				SetCanisterData(data.Compartment);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
