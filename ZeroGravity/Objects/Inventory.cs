using System.Linq;
using OpenHellion.Networking;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Inventory
{
	public enum EquipType
	{
		None,
		Hands,
		EquipInventory,
		Inventory
	}

	private Player parentPlayer;

	private Corpse parentCorpse;

	public Outfit CurrOutfit { get; private set; }

	public InventorySlot HandsSlot { get; private set; }

	public InventorySlot OutfitSlot { get; private set; }

	public SpaceObject Parent
	{
		get
		{
			if (parentPlayer != null)
			{
				return parentPlayer;
			}
			return parentCorpse;
		}
	}

	public Inventory()
	{
		HandsSlot = new InventorySlot(InventorySlot.Type.Hands, -1, null, mustBeEmptyToRemoveOutfit: true, null, this);
		OutfitSlot = new InventorySlot(InventorySlot.Type.Equip, -2, null, mustBeEmptyToRemoveOutfit: false, null, this);
	}

	public Inventory(Player pl)
	{
		parentPlayer = pl;
		parentCorpse = null;
		HandsSlot = new InventorySlot(InventorySlot.Type.Hands, -1, null, mustBeEmptyToRemoveOutfit: true, null, this);
		OutfitSlot = new InventorySlot(InventorySlot.Type.Equip, -2, null, mustBeEmptyToRemoveOutfit: false, null, this);
	}

	public void ChangeParent(Corpse corpse)
	{
		parentPlayer = null;
		parentCorpse = corpse;
		if (HandsSlot.Item != null)
		{
			HandsSlot.Item.DynamicObj.Parent = corpse;
		}
		if (OutfitSlot.Item != null)
		{
			OutfitSlot.Item.DynamicObj.Parent = corpse;
		}
		if (CurrOutfit == null)
		{
			return;
		}
		foreach (InventorySlot sl in CurrOutfit.InventorySlots.Values)
		{
			if (sl.Item != null)
			{
				sl.Item.DynamicObj.Parent = corpse;
			}
		}
	}

	public Item GetHandsItemIfType<T>()
	{
		if (HandsSlot.Item != null && typeof(T).IsAssignableFrom(HandsSlot.Item.GetType()))
		{
			return HandsSlot.Item;
		}
		return null;
	}

	private bool EquipOutfit(Outfit outfit)
	{
		if (CurrOutfit != null)
		{
			return false;
		}
		if (outfit.Slot != null && outfit.Slot.Item == outfit)
		{
			outfit.Slot.Item = null;
		}
		CurrOutfit = outfit;
		CurrOutfit.SetInventorySlot(OutfitSlot);
		if (parentPlayer != null)
		{
			CurrOutfit.DynamicObj.Parent = parentPlayer;
			foreach (InventorySlot sl2 in CurrOutfit.InventorySlots.Values)
			{
				if (sl2.Item != null)
				{
					sl2.Item.DynamicObj.Parent = parentPlayer;
				}
			}
			CurrOutfit.ExternalTemperature = parentPlayer.AmbientTemperature.HasValue ? parentPlayer.AmbientTemperature.Value : parentPlayer.CoreTemperature;
			CurrOutfit.InternalTemperature = parentPlayer.CoreTemperature;
		}
		foreach (InventorySlot sl in CurrOutfit.InventorySlots.Values)
		{
			sl.SetInventory(this);
		}
		return true;
	}

	private bool TakeOffOutfit(short slotID)
	{
		if (CurrOutfit == null)
		{
			return false;
		}
		foreach (InventorySlot sl in CurrOutfit.InventorySlots.Values)
		{
			if (sl.Item != null)
			{
				sl.Item.DynamicObj.Parent = CurrOutfit.DynamicObj;
			}
			sl.SetInventory(null);
		}
		switch (slotID)
		{
		case -1:
			CurrOutfit.SetInventorySlot(HandsSlot);
			break;
		case -1111:
			CurrOutfit.SetInventorySlot(null);
			break;
		default:
			return false;
		}
		OutfitSlot.Item = null;
		CurrOutfit = null;
		return true;
	}

	public bool AddItemToInventory(Item item, short slotID)
	{
		item.DynamicObj.PickedUp();
		if (item is Outfit outfit && slotID == -2)
		{
			return EquipOutfit(outfit);
		}
		if (item is Outfit && CurrOutfit == item && slotID != -2)
		{
			return TakeOffOutfit(slotID);
		}
		InventorySlot newSlot = null;
		if (slotID == -1)
		{
			newSlot = HandsSlot;
		}
		else if (CurrOutfit != null && CurrOutfit.InventorySlots.TryGetValue(slotID, out var slot))
		{
			newSlot = slot;
		}
		if (newSlot == null || !newSlot.CanStoreItem(item))
		{
			return false;
		}
		if (newSlot.Item != null && newSlot.Item != item)
		{
			InventorySlot itemSlot = item.Slot;
			if (item.DynamicObj.Parent is DynamicObject dynamicObject && dynamicObject.Item != null)
			{
				Item parentItem = dynamicObject.Item;
				if (parentItem == newSlot.Item)
				{
					return false;
				}
				newSlot.Item.DynamicObj.Parent = parentItem.DynamicObj;
			}
			else
			{
				if (item.Slot == null)
				{
					return false;
				}
				if (!item.Slot.CanStoreItem(newSlot.Item))
				{
					InventorySlot tmp = CurrOutfit.InventorySlots.Values.FirstOrDefault((InventorySlot m) => m.Item == null && m.CanStoreItem(newSlot.Item));
					if (tmp == null)
					{
						return false;
					}
					itemSlot = tmp;
				}
			}
			Item targetSlotItem = newSlot.Item;
			targetSlotItem.SetInventorySlot(itemSlot);
			DynamicObjectStatsMessage dosm = new DynamicObjectStatsMessage
			{
				Info = new DynamicObjectInfo
				{
					GUID = targetSlotItem.GUID,
					Stats = targetSlotItem.StatsNew
				},
				AttachData = targetSlotItem.DynamicObj.GetCurrAttachData()
			};
			NetworkController.Instance.SendToClientsSubscribedTo(dosm, -1L, targetSlotItem.DynamicObj.GetParents(includeMe: false).ToArray());
		}
		item.SetInventorySlot(newSlot);
		if (parentCorpse != null)
		{
			parentCorpse.CheckInventoryDestroy();
		}
		return true;
	}

	public bool DropItem(short slotID)
	{
		if (slotID == -2)
		{
			return TakeOffOutfit(-1111);
		}
		InventorySlot dropSlot = null;
		if (slotID == -1)
		{
			dropSlot = HandsSlot;
		}
		else if (CurrOutfit != null && CurrOutfit.InventorySlots.TryGetValue(slotID, out var slot))
		{
			dropSlot = slot;
		}
		if (dropSlot == null)
		{
			return false;
		}
		dropSlot.Item.SetInventorySlot(null);
		dropSlot.Item = null;
		if (parentCorpse != null)
		{
			parentCorpse.CheckInventoryDestroy();
		}
		return true;
	}
}
