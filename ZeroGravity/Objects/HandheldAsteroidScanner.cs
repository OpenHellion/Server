using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class HandheldAsteroidScanner : Item
{
	private int penetrationLevel;

	public override DynamicObjectStats StatsNew => null;

	public HandheldAsteroidScanner(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		HandheldAsteroidScannerData hasd = data as HandheldAsteroidScannerData;
		penetrationLevel = hasd.penetrationLevel;
	}

	public void UpdateResources(double dbl)
	{
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataHandheldAsteroidScanner data = new PersistenceObjectDataHandheldAsteroidScanner();
		FillPersistenceData(data);
		data.ScannerData = new HandheldAsteroidScannerData();
		FillBaseAuxData(data.ScannerData);
		data.ScannerData.penetrationLevel = penetrationLevel;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataHandheldAsteroidScanner data))
			{
				Dbg.Warning("PersistenceObjectDataHandheldAsteroidScanner data is null", base.GUID);
			}
			else
			{
				SetData(data.ScannerData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return true;
	}
}
