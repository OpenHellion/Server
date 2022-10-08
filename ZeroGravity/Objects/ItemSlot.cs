using System.Collections.Generic;
using ZeroGravity.Data;

namespace ZeroGravity.Objects;

public class ItemSlot : IItemSlot
{
	public short ID;

	public List<ItemType> ItemTypes;

	public List<GenericItemSubType> GenericSubTypes;

	public List<MachineryPartType> MachineryPartTypes;

	public Item Item { get; set; }

	public SpaceObject Parent { get; set; }

	public ItemSlot(ItemSlotData data)
	{
		ID = data.ID;
		ItemTypes = data.ItemTypes;
		GenericSubTypes = data.GenericSubTypes;
		MachineryPartTypes = data.MachineryPartTypes;
	}

	public bool FitItem(Item item)
	{
		if (CanFitItem(item))
		{
			Item = item;
			item.ItemSlotID = ID;
			return true;
		}
		return false;
	}

	public bool CanFitItem(Item item)
	{
		return CanFitItem(item.Type, (item is GenericItem) ? (item as GenericItem).SubType : GenericItemSubType.None, (item is MachineryPart) ? (item as MachineryPart).PartType : MachineryPartType.None);
	}

	public bool CanFitItem(ItemType itemType, GenericItemSubType subType, MachineryPartType partType)
	{
		return itemType switch
		{
			ItemType.GenericItem => GenericSubTypes.Contains(subType), 
			ItemType.MachineryPart => MachineryPartTypes.Contains(partType), 
			_ => ItemTypes.Contains(itemType), 
		};
	}

	public ItemSlotData GetData()
	{
		return new ItemSlotData
		{
			ID = ID,
			ItemTypes = ItemTypes,
			GenericSubTypes = GenericSubTypes,
			MachineryPartTypes = MachineryPartTypes
		};
	}
}
