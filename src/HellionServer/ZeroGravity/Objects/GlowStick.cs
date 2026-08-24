using System.Threading.Tasks;
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

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		isOn = true;
		await DynamicObj.SendStatsToClient();
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

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataGlowStick data)
		{
			Debug.LogWarning("PersistenceObjectDataGlowStick data is null", GUID);
		}
		else
		{
			await SetData(data.GlowStickData);
		}
	}
}
