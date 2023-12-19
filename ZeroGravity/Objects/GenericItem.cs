using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class GenericItem : Item
{
	public GenericItemSubType SubType;

	private GenericItemStats stats;

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

	public GenericItem(DynamicObjectAuxData data)
	{
		stats = new GenericItemStats();
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		GenericItemData i = data as GenericItemData;
		stats.Health = base.Health;
		SubType = i.SubType;
		Look = i.Look;
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (persistenceData is not PersistenceObjectDataGenericItem data)
			{
				Debug.Warning("PersistenceObjectDataGenericItem data is null", base.GUID);
			}
			else
			{
				SetData(data.GenericData);
			}
		}
		catch (Exception e)
		{
			Debug.Exception(e);
		}
	}
}
