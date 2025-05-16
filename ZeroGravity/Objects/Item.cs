using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public abstract class Item : IPersistantObject, IDamageable
{
	public ItemType Type;

	public int Tier = 1;

	public float[] TierMultipliers;

	public float[] AuxValues;

	public double AttachmentChangeTime;

	private InventorySlot _Slot;

	private VesselObjectID _AttachPointID;

	private AttachPointType _attachPointType = AttachPointType.None;

	private float _MaxHealth = 100f;

	private float _Health = 100f;

	private float _Armor;

	public float MeleeDamage;

	public Dictionary<short, ItemSlot> Slots;

	public short ItemSlotID;

	public float ExplosionRadius;

	public float ExplosionDamage;

	public TypeOfDamage ExplosionDamageType;

	protected bool TierMultiplierApplied;

	public InventorySlot Slot
	{
		get
		{
			return _Slot;
		}
		protected set
		{
			_Slot = value;
			AttachmentChangeTime = Server.SolarSystemTime;
		}
	}

	public abstract DynamicObjectStats StatsNew { get; }

	public VesselObjectID AttachPointID
	{
		get
		{
			return _AttachPointID;
		}
		private set
		{
			_AttachPointID = value;
			AttachmentChangeTime = Server.SolarSystemTime;
		}
	}

	public AttachPointType AttachPointType
	{
		get
		{
			return _attachPointType;
		}
		private set
		{
			_attachPointType = value;
			AttachmentChangeTime = Server.SolarSystemTime;
		}
	}

	public long GUID => DynamicObj.Guid;

	public DynamicObject DynamicObj { get; private set; }

	public float MaxHealth
	{
		get
		{
			return _MaxHealth;
		}
		set
		{
			_MaxHealth = value < 0f ? 0f : value;
		}
	}

	public float Health
	{
		get
		{
			return _Health;
		}
		set
		{
			_Health = value > MaxHealth ? MaxHealth : value < 0f ? 0f : value;
		}
	}

	public float Armor
	{
		get
		{
			return _Armor;
		}
		set
		{
			_Armor = value < 0f ? 0f : value;
		}
	}

	public bool Damageable { get; set; }

	public bool Repairable { get; set; }

	public float UsageWear { get; set; }

	public ItemCompoundType CompoundType
	{
		get
		{
			if (Type == ItemType.GenericItem)
			{
				ItemCompoundType itemCompoundType = new ItemCompoundType();
				itemCompoundType.Type = Type;
				itemCompoundType.SubType = (this as GenericItem).SubType;
				itemCompoundType.PartType = MachineryPartType.None;
				itemCompoundType.Tier = Tier;
				return itemCompoundType;
			}
			if (Type == ItemType.MachineryPart)
			{
				ItemCompoundType itemCompoundType = new ItemCompoundType();
				itemCompoundType.Type = Type;
				itemCompoundType.SubType = GenericItemSubType.None;
				itemCompoundType.PartType = (this as MachineryPart).PartType;
				itemCompoundType.Tier = Tier;
				return itemCompoundType;
			}
			return new ItemCompoundType
			{
				Type = Type,
				SubType = GenericItemSubType.None,
				PartType = MachineryPartType.None,
				Tier = Tier
			};
		}
	}

	public string TypeName
	{
		get
		{
			if (this is GenericItem)
			{
				return (this as GenericItem).SubType.ToString();
			}
			if (this is MachineryPart)
			{
				return (this as MachineryPart).PartType.ToString();
			}
			return Type.ToString();
		}
	}

	public float TierMultiplier
	{
		get
		{
			if (Tier < 1 || TierMultipliers == null || Tier > TierMultipliers.Length)
			{
				return 1f;
			}
			return TierMultipliers[Tier - 1];
		}
	}

	public float AuxValue
	{
		get
		{
			if (Tier < 1 || AuxValues == null || Tier > AuxValues.Length)
			{
				return 0f;
			}
			return AuxValues[Tier - 1];
		}
	}

	public abstract Task<bool> ChangeStats(DynamicObjectStats stats);

	public virtual void SetInventorySlot(InventorySlot slot)
	{
		if (Slot != null && Slot.Item == this)
		{
			Slot.Item = null;
		}
		Slot = slot;
		if (slot != null)
		{
			Slot.Item = this;
		}
		if (slot != null)
		{
			DynamicObj.Parent = slot.GetParent();
			ChangeEquip(slot.GetEquipType());
		}
		else
		{
			ChangeEquip(Inventory.EquipType.None);
		}
	}

	public virtual void SetAttachPoint(AttachPointDetails data)
	{
		if (data is not { InSceneID: > 0 })
		{
			if (AttachPointID != null)
			{
				SpaceObjectVessel ves = Server.Instance.GetVessel(AttachPointID.VesselGUID);
				if (ves != null && ves.AttachPoints.TryGetValue(AttachPointID.InSceneID, out var point))
				{
					point.Item = null;
				}
			}
			AttachPointID = null;
			AttachPointType = AttachPointType.None;
		}
		else if (DynamicObj.Parent is SpaceObjectVessel)
		{
			AttachPointID = new VesselObjectID(DynamicObj.Parent.Guid, data.InSceneID);
			AttachPointType apType = AttachPointType.None;
			(DynamicObj.Parent as SpaceObjectVessel).AttachPointsTypes.TryGetValue(AttachPointID, out apType);
			AttachPointType = apType;
			if (apType == AttachPointType.ResourcesAutoTransferPoint && this is Canister && DynamicObj.Parent is Ship)
			{
				AutoTransferResources();
			}
			if ((DynamicObj.Parent as SpaceObjectVessel).AttachPoints.ContainsKey(AttachPointID.InSceneID))
			{
				(DynamicObj.Parent as SpaceObjectVessel).AttachPoints[AttachPointID.InSceneID].Item = this;
			}
		}
	}

	internal async Task<Item> GetCopy()
	{
		return (await DynamicObj.GetCopy()).Item;
	}

	private void AutoTransferResources()
	{
		ICargo cargo = this as ICargo;
		CargoCompartmentData comp = cargo.GetCompartment();
		Ship ship = DynamicObj.Parent as Ship;
		foreach (ResourceContainer rc in ship.DistributionManager.GetResourceContainers())
		{
			foreach (CargoCompartmentData ccd in rc.Compartments)
			{
				if (ccd.Type != CargoCompartmentType.RCS && ccd.Type != CargoCompartmentType.AirGeneratorOxygen && ccd.Type != CargoCompartmentType.AirGeneratorNitrogen && ccd.Type != CargoCompartmentType.PowerGenerator && ccd.Type != CargoCompartmentType.Engine)
				{
					continue;
				}
				List<CargoResourceData> resources = new List<CargoResourceData>(comp.Resources);
				foreach (CargoResourceData res in resources)
				{
					Server.Instance.TransferResources(cargo, comp.ID, rc, ccd.ID, res.ResourceType, res.Quantity);
				}
			}
		}
	}

	public static async Task<Item> Create(DynamicObject dobj, ItemType type, DynamicObjectAuxData data)
	{
		Item it = null;
		if (ItemTypeRange.IsHelmet(type))
		{
			it = await Helmet.CreateAsync(data);
		}
		else if (ItemTypeRange.IsJetpack(type))
		{
			it = await Jetpack.CreateAsync(data);
		}
		else if (ItemTypeRange.IsWeapon(type))
		{
			it = await Weapon.CreateAsync(data);
		}
		else if (ItemTypeRange.IsOutfit(type))
		{
			it = await Outfit.CreateOutfitAsync(data);
		}
		else if (ItemTypeRange.IsAmmo(type))
		{
			it = await Magazine.CreateMagazineAsync(data);
		}
		else if (ItemTypeRange.IsMachineryPart(type))
		{
			it = await MachineryPart.CreateAsync(data);
		}
		else if (ItemTypeRange.IsBattery(type))
		{
			it = await Battery.CreateBatteryAsync(data);
		}
		else if (ItemTypeRange.IsCanister(type))
		{
			it = await Canister.CreateAsync(data);
		}
		else if (ItemTypeRange.IsDrill(type))
		{
			it = await HandDrill.CreateAsync(data);
		}
		else if (ItemTypeRange.IsMelee(type))
		{
			it = await MeleeWeapon.CreateAsync(data);
		}
		else if (ItemTypeRange.IsGlowStick(type))
		{
			it = new GlowStick(data);
		}
		else if (ItemTypeRange.IsMedpack(type))
		{
			it = await Medpack.CreateAsync(data);
		}
		else if (ItemTypeRange.IsHackingTool(type))
		{
			it = new DisposableHackingTool(data);
		}
		else if (ItemTypeRange.IsAsteroidScanningTool(type))
		{
			it = await HandheldAsteroidScanner.CreateAsync(data);
		}
		else if (ItemTypeRange.IsLogItem(type))
		{
			it = await LogItem.CreateAsync(data);
		}
		else if (ItemTypeRange.IsGenericItem(type))
		{
			it = await GenericItem.CreateAsync(data);
		}
		else if (ItemTypeRange.IsGrenade(type))
		{
			it = await Grenade.CreateAsync(data);
		}
		else if (ItemTypeRange.IsPortableTurret(type))
		{
			it = await PortableTurret.CreateAsync(data);
		}
		else if (ItemTypeRange.IsRepairTool(type))
		{
			it = await RepairTool.CreateAsync(data);
		}
		if (it != null)
		{
			it.Type = type;
			it.DynamicObj = dobj;
			if (data == null)
			{
				data = ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList[it.DynamicObj.ItemID].DefaultAuxData);
				if (data != null)
				{
					await it.SetData(data);
				}
			}
			List<ItemSlotData> slots = data.Slots;
			if (slots is { Count: > 0 })
			{
				foreach (ItemSlotData isd in data.Slots)
				{
					if (it.Slots.TryGetValue(isd.ID, out var isl))
					{
						isl.Parent = dobj;
						if (isd.SpawnItem.Type != 0 || isd.SpawnItem.SubType != 0 || isd.SpawnItem.PartType != 0)
						{
							await DynamicObject.SpawnDynamicObject(isd.SpawnItem.Type, isd.SpawnItem.SubType, isd.SpawnItem.PartType, it.DynamicObj, -1, null, null, null, itemSlot: isl, tier: isd.SpawnItem.Tier);
						}
					}
				}
			}
		}
		return it;
	}

	public virtual Task SetData(DynamicObjectAuxData data)
	{
		Tier = data.Tier;
		TierMultipliers = data.TierMultipliers;
		AuxValues = data.AuxValues;
		MaxHealth = data.MaxHealth;
		Health = data.Health;
		Armor = data.Armor;
		Damageable = data.Damageable;
		Repairable = data.Repairable;
		UsageWear = data.UsageWear;
		MeleeDamage = data.MeleeDamage;
		ExplosionDamage = data.ExplosionDamage;
		ExplosionDamageType = data.ExplosionDamageType;
		ExplosionRadius = data.ExplosionRadius;
		if (Slots == null && data.Slots != null)
		{
			Slots = data.Slots.ToDictionary((ItemSlotData k) => k.ID, (ItemSlotData v) => new ItemSlot(v));
		}

		return Task.CompletedTask;
	}

	protected virtual void ChangeEquip(Inventory.EquipType equipType)
	{
	}

	public virtual Task SendAllStats()
	{
		return null;
	}

	public void FillPersistenceData(PersistenceObjectDataItem data)
	{
		DynamicObj.FillPersistenceData(data);
		data.GUID = GUID;
		data.Health = Health;
		data.Armor = Armor;
		data.Tier = Tier;
		if (AttachPointType != 0)
		{
			data.AttachPointType = AttachPointType;
			data.AttachPointID = AttachPointID.InSceneID;
		}
		if (Slot != null)
		{
			data.SlotID = Slot.SlotID;
		}
		if (ItemSlotID > 0)
		{
			data.ItemSlotID = ItemSlotID;
		}
	}

	public virtual PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataItem data = new PersistenceObjectDataItem();
		FillPersistenceData(data);
		return data;
	}

	public virtual async Task DestroyItem()
	{
		await DynamicObj.DestroyDynamicObject();
	}

	public virtual async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		PersistenceObjectDataItem data = persistenceData as PersistenceObjectDataItem;
		await DynamicObj.LoadPersistenceData(data);
		Health = data.Health;
		Armor = data.Armor;
		AttachPointDetails apd = null;
		if (data.AttachPointID is > 0)
		{
			apd = new AttachPointDetails
			{
				InSceneID = data.AttachPointID.Value
			};
			try
			{
				SetAttachPoint(apd);
			}
			catch
			{
			}
		}
		DynamicObj.APDetails = apd;
		if (DynamicObj.Parent is DynamicObject { Item: not null } parentDynObj)
		{
			if (data.SlotID.HasValue && parentDynObj.Item is Outfit outfit && outfit.InventorySlots.TryGetValue(data.SlotID.Value, out var inventorySlot))
			{
				SetInventorySlot(inventorySlot);
			}
		}
		if (DynamicObj.Parent is Player player && data.SlotID.HasValue)
		{
			await player.PlayerInventory.AddItemToInventory(this, data.SlotID.Value);
		}
		if (DynamicObj.Parent is DynamicObject dynamicObject && data.ItemSlotID.HasValue && dynamicObject.Item.Slots != null && dynamicObject.Item.Slots.TryGetValue(data.ItemSlotID.Value, out var slot))
		{
			slot.Item = this;
			ItemSlotID = slot.ID;
		}
	}

	public virtual async Task TakeDamage(TypeOfDamage type, float damage, bool forceTakeDamage = false)
	{
		await TakeDamage(new Dictionary<TypeOfDamage, float> { { type, damage } }, forceTakeDamage);
	}

	public virtual async Task TakeDamage(Dictionary<TypeOfDamage, float> damages, bool forceTakeDamage = false)
	{
		if (!forceTakeDamage && !Damageable)
		{
			return;
		}
		float amount = 0f;
		foreach (KeyValuePair<TypeOfDamage, float> damage in damages)
		{
			amount += damage.Value;
		}
		if (!((amount -= Armor) < float.Epsilon))
		{
			Health -= amount;
			if (StatsNew != null)
			{
				StatsNew.Health = Health;
				StatsNew.Damages = damages;
			}
			await DynamicObj.SendStatsToClient();
		}
	}

	public void FillBaseAuxData<T>(T data) where T : DynamicObjectAuxData
	{
		data.ItemType = Type;
		data.Tier = Tier;
		data.TierMultipliers = TierMultipliers;
		data.MaxHealth = MaxHealth;
		data.Health = Health;
		data.Armor = Armor;
		data.Damageable = Damageable;
		data.Repairable = Repairable;
		data.UsageWear = UsageWear;
		data.MeleeDamage = MeleeDamage;
		data.ExplosionRadius = ExplosionRadius;
		data.ExplosionDamage = ExplosionDamage;
		data.ExplosionDamageType = ExplosionDamageType;
		data.Slots = Slots.Values.Select((ItemSlot m) => m.GetData()).ToList();
	}

	public static Dictionary<ResourceType, float> GetRecycleResources(Item item)
	{
		if (item is GenericItem genericItem)
		{
			return GetRecycleResources(genericItem.Type, genericItem.SubType, MachineryPartType.None, genericItem.Tier);
		}
		if (item is MachineryPart part)
		{
			return GetRecycleResources(part.Type, GenericItemSubType.None, part.PartType, part.Tier);
		}
		return GetRecycleResources(item.Type, GenericItemSubType.None, MachineryPartType.None, item.Tier);
	}

	public static Dictionary<ResourceType, float> GetRecycleResources(ItemType itemType, GenericItemSubType subType, MachineryPartType partType, int tier)
	{
		ItemIngredientsData ingredientsData = StaticData.ItemsIngredients.FirstOrDefault((ItemIngredientsData m) => m.Type == itemType && m.SubType == subType && m.PartType == partType);
		if (ingredientsData != null)
		{
			KeyValuePair<int, ItemIngredientsTierData>? kv = ingredientsData.IngredientsTiers.OrderBy((KeyValuePair<int, ItemIngredientsTierData> m) => m.Key).Reverse().FirstOrDefault((KeyValuePair<int, ItemIngredientsTierData> m) => m.Key <= tier);
			if (kv.HasValue && kv.Value.Value.Recycle is { Count: > 0 })
			{
				return kv.Value.Value.Recycle;
			}
		}
		return null;
	}

	public static Dictionary<ResourceType, float> GetCraftingResources(Item item)
	{
		if (item is GenericItem genericItem)
		{
			return GetCraftingResources(genericItem.Type, genericItem.SubType, MachineryPartType.None, genericItem.Tier);
		}
		if (item is MachineryPart part)
		{
			return GetCraftingResources(part.Type, GenericItemSubType.None, part.PartType, part.Tier);
		}
		return GetCraftingResources(item.Type, GenericItemSubType.None, MachineryPartType.None, item.Tier);
	}

	public static Dictionary<ResourceType, float> GetCraftingResources(ItemCompoundType compoundType)
	{
		return GetCraftingResources(compoundType.Type, compoundType.SubType, compoundType.PartType, compoundType.Tier);
	}

	public static Dictionary<ResourceType, float> GetCraftingResources(ItemType itemType, GenericItemSubType subType, MachineryPartType partType, int tier)
	{
		ItemIngredientsData ingredientsData = StaticData.ItemsIngredients.FirstOrDefault((ItemIngredientsData m) => m.Type == itemType && m.SubType == subType && m.PartType == partType);
		if (ingredientsData != null)
		{
			KeyValuePair<int, ItemIngredientsTierData>? kv = ingredientsData.IngredientsTiers.OrderBy((KeyValuePair<int, ItemIngredientsTierData> m) => m.Key).Reverse().FirstOrDefault((KeyValuePair<int, ItemIngredientsTierData> m) => m.Key <= tier);
			if (kv.HasValue && kv.Value.Value.Craft is { Count: > 0 })
			{
				return kv.Value.Value.Craft;
			}
		}
		return null;
	}

	public virtual void ApplyTierMultiplier()
	{
		TierMultiplierApplied = true;
	}
}
