using System.Collections.Generic;
using ZeroGravity.Data;

namespace ZeroGravity.Objects;

public class InventorySlot
{
	public enum Type
	{
		Hands,
		General,
		Equip
	}

	public const short NoneSlotID = -1111;

	public const short HandsSlotID = -1;

	public const short StartSlotID = 1;

	public const short OutfitSlotID = -2;

	public Inventory Inventory;

	public Outfit Outfit { get; private set; }

	public Type SlotType { get; private set; }

	public short SlotID { get; private set; }

	public List<ItemType> ItemTypes { get; private set; }

	public bool MustBeEmptyToRemoveOutfit { get; private set; }

	public Item Item { get; set; }

	public InventorySlot(Type slotType, short slotID, List<ItemType> itemTypes, bool mustBeEmptyToRemoveOutfit, Outfit outfit, Inventory inventory)
	{
		SlotType = slotType;
		SlotID = slotID;
		MustBeEmptyToRemoveOutfit = mustBeEmptyToRemoveOutfit;
		if (itemTypes != null)
		{
			ItemTypes = new List<ItemType>(itemTypes);
		}
		Outfit = outfit;
		Inventory = inventory;
	}

	public void SetInventory(Inventory inv)
	{
		Inventory = inv;
	}

	public bool CanStoreItem(Item item)
	{
		return CanStoreItem(item.Type);
	}

	public bool CanStoreItem(ItemType itemType)
	{
		return SlotType == Type.Hands || (this == Inventory.OutfitSlot && itemType >= ItemType.AltairPressurisedSuit && itemType <= (ItemType)399) || (ItemTypes?.Contains(itemType) ?? false);
	}

	public SpaceObject GetParent()
	{
		if (Inventory != null)
		{
			return Inventory.Parent;
		}
		if (Outfit != null)
		{
			return Outfit.DynamicObj;
		}
		Debug.Error("Slot has no parent", SlotID, SlotType);
		return null;
	}

	public Inventory.EquipType GetEquipType()
	{
		if (SlotType == Type.Hands)
		{
			return Inventory.EquipType.Hands;
		}
		if (SlotType == Type.Equip)
		{
			return Inventory.EquipType.EquipInventory;
		}
		if (SlotType == Type.General)
		{
			return Inventory.EquipType.Inventory;
		}
		return Inventory.EquipType.None;
	}

	public bool DropItem()
	{
		return Inventory.DropItem(SlotID);
	}
}
