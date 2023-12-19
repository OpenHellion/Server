using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class GlowStick : Item
{
	public GlowStickStats stats = new GlowStickStats();

	public bool isOn;

	public override DynamicObjectStats StatsNew => stats;

	public GlowStick(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		GlowStickStats gss = stats as GlowStickStats;
		isOn = true;
		base.DynamicObj.SendStatsToClient();
		return false;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataGlowStick data = new PersistenceObjectDataGlowStick();
		FillPersistenceData(data);
		data.GlowStickData = new GlowStickData();
		FillBaseAuxData(data.GlowStickData);
		data.GlowStickData.IsOn = isOn;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (persistenceData is not PersistenceObjectDataGlowStick data)
			{
				Debug.Warning("PersistenceObjectDataGlowStick data is null", base.GUID);
			}
			else
			{
				SetData(data.GlowStickData);
			}
		}
		catch (Exception e)
		{
			Debug.Exception(e);
		}
	}
}
