using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class HandheldAsteroidScanner : Item
{
	private int penetrationLevel;

	public override DynamicObjectStats StatsNew => null;

	private HandheldAsteroidScanner()
	{
	}

	public static async Task<HandheldAsteroidScanner> CreateAsync(DynamicObjectAuxData data)
	{
		HandheldAsteroidScanner asteroidScanner = new();
		if (data != null)
		{
			await asteroidScanner.SetData(data);
		}

		return asteroidScanner;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
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

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataHandheldAsteroidScanner data)
		{
			Debug.LogWarning("PersistenceObjectDataHandheldAsteroidScanner data is null", GUID);
		}
		else
		{
			await SetData(data.ScannerData);
		}
	}

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(true);
	}
}
