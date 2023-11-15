using System;
using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Weapon : Item
{
	private WeaponStats _stats;

	private ItemSlot magazineSlot;

	private int _currentModIndex;

	private List<WeaponModData> weaponMods = new List<WeaponModData>();

	private double lastShotTime;

	public override DynamicObjectStats StatsNew => _stats;

	public float Damage
	{
		get
		{
			return CurrentMod.Damage;
		}
		set
		{
		}
	}

	public bool HasAmmo => Magazine != null && Magazine.HasAmmo;

	public Magazine Magazine => magazineSlot != null ? magazineSlot.Item as Magazine : null;

	public int CurrentModIndex
	{
		get
		{
			return _currentModIndex;
		}
		set
		{
			_currentModIndex = value;
			CurrentMod = weaponMods[_currentModIndex];
			_stats.CurrentMod = _currentModIndex;
		}
	}

	public WeaponModData CurrentMod { get; private set; }

	public float ChargeAmount => 1f;

	public Weapon(DynamicObjectAuxData data)
	{
		_stats = new WeaponStats();
		if (data == null)
		{
			return;
		}
		WeaponData wd = data as WeaponData;
		SetData(wd);
		if (Slots == null)
		{
			return;
		}
		magazineSlot = Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.FirstOrDefault((ItemType n) => ItemTypeRange.IsAmmo(n)) != ItemType.None).Value;
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		WeaponData wd = data as WeaponData;
		weaponMods = wd.weaponMods;
		foreach (WeaponModData wmod in weaponMods)
		{
			wmod.RateOfFire *= 0.95f;
		}
		if (wd.CurrentMod < 0)
		{
			CurrentModIndex = 0;
		}
		else
		{
			CurrentModIndex = wd.CurrentMod;
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		WeaponStats ws = stats as WeaponStats;
		if (ws.CurrentMod.HasValue)
		{
			CurrentModIndex = MathHelper.Clamp(ws.CurrentMod.Value, 0, weaponMods.Count - 1);
			return true;
		}
		return false;
	}

	public void ConsumePower(double amount)
	{
	}

	public bool CanShoot()
	{
		if (Magazine.BulletCount > 0 && Server.Instance.SolarSystem.CurrentTime - lastShotTime > (double)CurrentMod.RateOfFire)
		{
			Magazine.ChangeQuantity(-1);
			lastShotTime = Server.Instance.SolarSystem.CurrentTime;
			base.DynamicObj.StatsChanged = true;
			return true;
		}
		return false;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataWeapon data = new PersistenceObjectDataWeapon();
		FillPersistenceData(data);
		data.WeaponData = new WeaponData();
		FillBaseAuxData(data.WeaponData);
		data.WeaponData.CurrentMod = CurrentModIndex;
		data.WeaponData.weaponMods = weaponMods;
		return data;
	}

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataWeapon data))
			{
				Dbg.Warning("PersistenceObjectDataWeapon data is null", base.GUID);
			}
			else
			{
				SetData(data.WeaponData);
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
