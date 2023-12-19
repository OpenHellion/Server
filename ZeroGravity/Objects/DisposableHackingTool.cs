using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class DisposableHackingTool : Item
{
	private DisposableHackingToolStats objStats = new DisposableHackingToolStats();

	public override DynamicObjectStats StatsNew => objStats;

	public DisposableHackingTool(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
			ApplyTierMultiplier();
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		DisposableHackingToolStats dhs = stats as DisposableHackingToolStats;
		if (dhs.Use)
		{
			objStats.Use = dhs.Use;
			base.DynamicObj.SendStatsToClient();
			TakeDamage(TypeOfDamage.None, 1f);
			return true;
		}
		return false;
	}

	public void Destroy()
	{
		SetInventorySlot(null);
		SetAttachPoint(null);
		base.DynamicObj.DestroyDynamicObject();
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataHackingTool data = new PersistenceObjectDataHackingTool();
		FillPersistenceData(data);
		data.HackingToolData = new DisposableHackingToolData();
		FillBaseAuxData(data.HackingToolData);
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (persistenceData is not PersistenceObjectDataHackingTool data)
			{
				Debug.Warning("PersistenceObjectDataHackingTool data is null", base.GUID);
			}
			else
			{
				SetData(data.HackingToolData);
				ApplyTierMultiplier();
			}
		}
		catch (Exception e)
		{
			Debug.Exception(e);
		}
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			base.MaxHealth *= base.TierMultiplier;
			base.Health *= base.TierMultiplier;
		}
		base.ApplyTierMultiplier();
	}
}
