using System;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class MeleeWeapon : Item
{
	public float RateOfFire;

	public float Damage;

	public float Range;

	public override DynamicObjectStats StatsNew => null;

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		return false;
	}

	public MeleeWeapon(DynamicObjectAuxData data)
	{
		if (data != null)
		{
			SetData(data);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataMeleeWeapon data))
			{
				Dbg.Warning("PersistenceObjectDataMeleeWeapon data is null", base.GUID);
			}
			else
			{
				SetData(data.MeleeWeaponData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
