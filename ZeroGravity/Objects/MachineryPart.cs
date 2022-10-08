using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class MachineryPart : Item, IPersistantObject
{
	public MachineryPartType PartType;

	public float WearMultiplier = 1f;

	public override DynamicObjectStats StatsNew => new MachineryPartStats
	{
		Health = base.Health
	};

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
	}

	public MachineryPart(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		MachineryPartData mpd = data as MachineryPartData;
		PartType = mpd.PartType;
		ApplyTierMultiplier();
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			if (PartType == MachineryPartType.CarbonFilters)
			{
				base.MaxHealth *= base.AuxValue;
				base.Health *= base.AuxValue;
			}
			else if (PartType == MachineryPartType.NaniteCore || PartType == MachineryPartType.MillitaryNaniteCore || PartType == MachineryPartType.WarpCell)
			{
				base.MaxHealth *= base.TierMultiplier;
				base.Health *= base.TierMultiplier;
			}
		}
		base.ApplyTierMultiplier();
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataMachineryPart data = new PersistenceObjectDataMachineryPart();
		FillPersistenceData(data);
		data.PartType = PartType;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataMachineryPart data))
			{
				Dbg.Warning("PersistenceObjectDataMachineryPart data is null", base.GUID);
			}
			else
			{
				PartType = data.PartType;
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
