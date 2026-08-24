using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Magazine : Item
{
	private MagazineStats _stats = new();

	private int _bulletCount;

	public override DynamicObjectStats StatsNew => _stats;

	public int BulletCount
	{
		get
		{
			return _bulletCount;
		}
	}

	public int MaxBulletCount { get; private set; }

	public bool HasAmmo => BulletCount > 0;

	private Magazine()
	{
	}

	public static async Task<Magazine> CreateMagazineAsync(DynamicObjectAuxData data)
	{
		Magazine magazine = new Magazine();
		if (data != null)
		{
			await magazine.SetData(data);
		}

		return magazine;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		MagazineData md = data as MagazineData;
		await SetBulletCountAsync(md.BulletCount);
		MaxBulletCount = md.MaxBulletCount;
	}

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		MagazineStats ms = stats as MagazineStats;
		if (ms.BulletsFrom.HasValue && ms.BulletsTo.HasValue && (ms.BulletsFrom.Value == GUID || ms.BulletsTo.Value == GUID))
		{
			Magazine magFrom = ms.BulletsFrom.Value == GUID ? this : Server.Instance.GetItem(ms.BulletsFrom.Value) as Magazine;
			Magazine magTo = ms.BulletsTo.Value == GUID ? this : Server.Instance.GetItem(ms.BulletsTo.Value) as Magazine;
			if (magFrom != null && magTo != null)
			{
				await SplitMagazines(magFrom, magTo);
			}
		}
		return false;
	}

	private static async Task SplitMagazines(Magazine fromMag, Magazine toMag)
	{
		int splitCount = toMag.MaxBulletCount - toMag.BulletCount;
		if (toMag.BulletCount == 0)
		{
			splitCount = fromMag.BulletCount / 2;
		}
		else if (fromMag.BulletCount < splitCount)
		{
			splitCount = fromMag.BulletCount;
		}
		await toMag.SetBulletCountAsync(splitCount);
		await fromMag.SetBulletCountAsync(splitCount);
		await fromMag.DynamicObj.SendStatsToClient();
		await toMag.DynamicObj.SendStatsToClient();
	}

	public async Task ChangeQuantity(int amount)
	{
		await SetBulletCountAsync(amount);
		if (BulletCount == 0)
		{
			await DynamicObj.SendStatsToClient();
		}
		else
		{
			DynamicObj.StatsChanged = true;
		}
	}

	private async Task SetBulletCountAsync(int amount)
	{
		_bulletCount = amount;
		_stats.BulletCount = amount;
		if (amount <= 0)
		{
			await DestroyItem();
		}
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataMagazine data = new PersistenceObjectDataMagazine();
		FillPersistenceData(data);
		data.MagazineData = new MagazineData();
		FillBaseAuxData(data.MagazineData);
		data.MagazineData.BulletCount = BulletCount;
		data.MagazineData.MaxBulletCount = MaxBulletCount;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataMagazine data)
		{
			Debug.LogWarning("PersistenceObjectDataMagazine data is null", GUID);
		}
		else
		{
			await SetData(data.MagazineData);
		}
	}
}
