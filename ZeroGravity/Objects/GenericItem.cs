using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class GenericItem : Item
{
	public GenericItemSubType SubType;

	private GenericItemStats stats = new GenericItemStats();

	public override DynamicObjectStats StatsNew => stats;

	public string Look
	{
		get
		{
			return stats != null ? stats.Look : "";
		}
		set
		{
			stats.Look = value;
		}
	}

	private GenericItem()
	{
	}

	public static async Task<GenericItem> CreateAsync(DynamicObjectAuxData data)
	{
		GenericItem genericItem = new();
		if (data != null)
		{
			await genericItem.SetData(data);
		}

		return genericItem;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		GenericItemData i = data as GenericItemData;
		stats.Health = Health;
		SubType = i.SubType;
		Look = i.Look;
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(false);
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataGenericItem data = new PersistenceObjectDataGenericItem();
		FillPersistenceData(data);
		data.GenericData = new GenericItemData();
		FillBaseAuxData(data.GenericData);
		data.GenericData.SubType = SubType;
		data.GenericData.Look = Look;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataGenericItem data)
		{
			Debug.LogWarning("PersistenceObjectDataGenericItem data is null", GUID);
		}
		else
		{
			await SetData(data.GenericData);
		}
	}
}
