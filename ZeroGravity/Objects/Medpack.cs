using System.Threading.Tasks;
using System.Timers;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Medpack : Item
{
	public float RegenRate;

	public float MaxHp;

	private MedpackStats medStats = new();

	private Timer destroyTimer;

	public override DynamicObjectStats StatsNew => medStats;

	private Medpack()
	{
	}

	public static async Task<Medpack> CreateAsync(DynamicObjectAuxData data)
	{
		Medpack medpack = new();
		if (data != null)
		{
			await medpack.SetData(data);
		}

		return medpack;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		MedpackData md = data as MedpackData;
		RegenRate = md.RegenRate;
		MaxHp = md.MaxHP;
	}

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		MedpackStats ms = stats as MedpackStats;
		if (ms.Use)
		{
			if (DynamicObj.Parent is Player)
			{
				(DynamicObj.Parent as Player).Stats.HealOverTime(RegenRate, MaxHp / RegenRate);
			}
			await DynamicObj.SendStatsToClient();
			destroyTimer = new Timer(2500.0);
			destroyTimer.Elapsed += async delegate
			{
				await DestroyItem();
			};
			destroyTimer.Enabled = true;
			return true;
		}
		return false;
	}

	public override async Task DestroyItem()
	{
		await base.DestroyItem();
		if (destroyTimer != null)
		{
			destroyTimer.Dispose();
		}
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataMedpack data = new PersistenceObjectDataMedpack();
		FillPersistenceData(data);
		data.MedpackData = new MedpackData();
		FillBaseAuxData(data.MedpackData);
		data.MedpackData.MaxHP = MaxHp;
		data.MedpackData.RegenRate = RegenRate;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataMedpack data)
		{
			Debug.LogWarning("PersistenceObjectDataMedpack data is null", GUID);
		}
		else
		{
			await SetData(data.MedpackData);
		}
	}
}
