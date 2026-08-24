using System;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class LogItem : Item
{
	private int logId;

	private LogItemStats itemStats = new();

	public int LogID
	{
		get
		{
			return logId;
		}
		set
		{
			logId = value;
			itemStats.LogID = value;
		}
	}

	public override DynamicObjectStats StatsNew => itemStats;

	private LogItem()
	{
	}

	public static async Task<LogItem> CreateAsync(DynamicObjectAuxData data)
	{
		LogItem item = new();
		if (data != null)
		{
			await item.SetData(data);
		}

		return item;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		LogItemData i = data as LogItemData;
		int tmpInt = i.logID;
		if (tmpInt == -1)
		{
			tmpInt = MathHelper.RandomRange(0, Enum.GetValues(typeof(LogItemTypes)).Length);
		}
		LogID = tmpInt;
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(false);
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataLogItem data = new PersistenceObjectDataLogItem();
		FillPersistenceData(data);
		data.LogItemData = new LogItemData();
		FillBaseAuxData(data.LogItemData);
		data.LogItemData.logID = logId;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataLogItem data)
		{
			Debug.LogWarning("PersistenceObjectDataLogItem data is null", GUID);
		}
		else
		{
			await SetData(data.LogItemData);
		}
	}
}
