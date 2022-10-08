using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Magazine : Item
{
	private MagazineStats _stats;

	private int _bulletCount;

	public override DynamicObjectStats StatsNew => _stats;

	public int BulletCount
	{
		get
		{
			return _bulletCount;
		}
		private set
		{
			_bulletCount = value;
			_stats.BulletCount = value;
			if (value <= 0)
			{
				DestroyItem();
			}
		}
	}

	public int MaxBulletCount { get; private set; }

	public bool HasAmmo => BulletCount > 0;

	public Magazine(DynamicObjectAuxData data)
	{
		_stats = new MagazineStats();
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		MagazineData md = data as MagazineData;
		BulletCount = md.BulletCount;
		MaxBulletCount = md.MaxBulletCount;
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		MagazineStats ms = stats as MagazineStats;
		if (ms.BulletsFrom.HasValue && ms.BulletsTo.HasValue && (ms.BulletsFrom.Value == base.GUID || ms.BulletsTo.Value == base.GUID))
		{
			Magazine magFrom = ((ms.BulletsFrom.Value == base.GUID) ? this : (Server.Instance.GetItem(ms.BulletsFrom.Value) as Magazine));
			Magazine magTo = ((ms.BulletsTo.Value == base.GUID) ? this : (Server.Instance.GetItem(ms.BulletsTo.Value) as Magazine));
			if (magFrom != null && magTo != null)
			{
				SplitMagazines(magFrom, magTo);
			}
		}
		return false;
	}

	private static void SplitMagazines(Magazine fromMag, Magazine toMag)
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
		toMag.BulletCount += splitCount;
		fromMag.BulletCount -= splitCount;
		fromMag.DynamicObj.SendStatsToClient();
		toMag.DynamicObj.SendStatsToClient();
	}

	public void ChangeQuantity(int amount)
	{
		BulletCount += amount;
		if (BulletCount == 0)
		{
			base.DynamicObj.SendStatsToClient();
		}
		else
		{
			base.DynamicObj.StatsChanged = true;
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataMagazine data))
			{
				Dbg.Warning("PersistenceObjectDataMagazine data is null", base.GUID);
			}
			else
			{
				SetData(data.MagazineData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
