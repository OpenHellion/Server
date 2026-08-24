using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class MeleeWeapon : Item
{
	public float RateOfFire;

	public float Damage;

	public float Range;

	public override DynamicObjectStats StatsNew => null;

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		return Task.FromResult(false);
	}

	private MeleeWeapon()
	{
	}

	public static async Task<MeleeWeapon> CreateAsync(DynamicObjectAuxData data)
	{
		MeleeWeapon meleeWeapon = new();
		if (data != null)
		{
			await meleeWeapon.SetData(data);
		}

		return meleeWeapon;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		MeleeWeaponData mwd = data as MeleeWeaponData;
		RateOfFire = mwd.RateOfFire;
		Damage = mwd.Damage;
		Range = mwd.Range;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataMeleeWeapon data = new PersistenceObjectDataMeleeWeapon();
		FillPersistenceData(data);
		data.MeleeWeaponData = new MeleeWeaponData();
		FillBaseAuxData(data.MeleeWeaponData);
		data.MeleeWeaponData.Damage = Damage;
		data.MeleeWeaponData.Range = Range;
		data.MeleeWeaponData.RateOfFire = RateOfFire;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataMeleeWeapon data)
		{
			Debug.LogWarning("PersistenceObjectDataMeleeWeapon data is null", GUID);
		}
		else
		{
			await SetData(data.MeleeWeaponData);
		}
	}
}
