using System;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class LogItem : Item
{
	private int logId;

	private LogItemStats lis;

	public int LogID
	{
		get
		{
			return logId;
		}
		set
		{
			logId = value;
			lis.LogID = value;
		}
	}

	public override DynamicObjectStats StatsNew => lis;

	public LogItem(DynamicObjectAuxData data)
	{
		lis = new LogItemStats();
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		LogItemData i = data as LogItemData;
		int tmpInt = i.logID;
		if (tmpInt == -1)
		{
			tmpInt = MathHelper.RandomRange(0, Enum.GetValues(typeof(LogItemTypes)).Length);
		}
		LogID = tmpInt;
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataLogItem data))
			{
				Dbg.Warning("PersistenceObjectDataLogItem data is null", base.GUID);
			}
			else
			{
				SetData(data.LogItemData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
