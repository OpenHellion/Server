using System.Threading.Tasks;
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

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		DisposableHackingToolStats dhs = stats as DisposableHackingToolStats;
		if (dhs.Use)
		{
			objStats.Use = dhs.Use;
			await DynamicObj.SendStatsToClient();
			await TakeDamage(TypeOfDamage.None, 1f);
			return true;
		}
		return false;
	}

	public async Task Destroy()
	{
		SetInventorySlot(null);
		SetAttachPoint(null);
		await DynamicObj.DestroyDynamicObject();
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataHackingTool data = new PersistenceObjectDataHackingTool();
		FillPersistenceData(data);
		data.HackingToolData = new DisposableHackingToolData();
		FillBaseAuxData(data.HackingToolData);
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataHackingTool data)
		{
			Debug.LogWarning("PersistenceObjectDataHackingTool data is null", GUID);
		}
		else
		{
			await SetData(data.HackingToolData);
			ApplyTierMultiplier();
		}
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			MaxHealth *= TierMultiplier;
			Health *= TierMultiplier;
		}
		base.ApplyTierMultiplier();
	}
}
