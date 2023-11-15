using System.Collections.Generic;
using ZeroGravity.Data;

namespace ZeroGravity.Objects;

public class VesselAttachPoint : IItemSlot
{
	public SpaceObjectVessel Vessel;

	public int InSceneID;

	public AttachPointType Type;

	public List<ItemType> ItemTypes;

	public List<GenericItemSubType> GenericSubTypes;

	public List<MachineryPartType> MachineryPartTypes;

	public Item Item { get; set; }

	public SpaceObject Parent => Vessel;

	public bool CanSpawnItems => Type == AttachPointType.Simple || Type == AttachPointType.Active || Type == AttachPointType.MachineryPartSlot || Type == AttachPointType.Scrap;

	public bool CanFitItem(Item item)
	{
		return CanFitItem(item.Type, item is GenericItem genericItem ? genericItem.SubType : GenericItemSubType.None, item is MachineryPart part ? part.PartType : MachineryPartType.None);
	}

	public bool CanFitItem(ItemType itemType, GenericItemSubType subType, MachineryPartType partType)
	{
		if (Type == AttachPointType.ItemRecycler && ItemTypes.Count == 0 && GenericSubTypes.Count == 0 && MachineryPartTypes.Count == 0)
		{
			return true;
		}
		return itemType switch
		{
			ItemType.GenericItem => GenericSubTypes.Contains(subType), 
			ItemType.MachineryPart => MachineryPartTypes.Contains(partType), 
			_ => ItemTypes.Contains(itemType), 
		};
	}
}
