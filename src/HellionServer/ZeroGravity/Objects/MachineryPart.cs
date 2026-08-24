using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class MachineryPart : Item, IPersistantObject
{
	public MachineryPartType PartType;

	public float WearMultiplier = 1f;

	public override DynamicObjectStats StatsNew => new MachineryPartStats
	{
		Health = Health
	};

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(false);
	}

	private MachineryPart()
	{
	}

	public static async Task<MachineryPart> CreateAsync(DynamicObjectAuxData data)
	{
		MachineryPart machineryPart = new();
		if (data != null)
		{
			await machineryPart.SetData(data);
		}

		return machineryPart;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
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
				MaxHealth *= AuxValue;
				Health *= AuxValue;
			}
			else if (PartType is MachineryPartType.NaniteCore or MachineryPartType.MillitaryNaniteCore or MachineryPartType.WarpCell)
			{
				MaxHealth *= TierMultiplier;
				Health *= TierMultiplier;
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

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataMachineryPart data)
		{
			Debug.LogWarning("PersistenceObjectDataMachineryPart data is null", GUID);
		}
		else
		{
			PartType = data.PartType;
		}
	}
}
