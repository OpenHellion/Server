using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Weapon : Item
{
	private WeaponStats _stats = new();

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

	public bool HasAmmo => Magazine is { HasAmmo: true };

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

	private Weapon()
	{
	}

	public static async Task<Weapon> CreateAsync(DynamicObjectAuxData data)
	{
		if (data == null)
		{
			return null;
		}
		Weapon weapon = new();
		await weapon.SetData(data);

		if (weapon.Slots == null)
		{
			return null;
		}
		weapon.magazineSlot = weapon.Slots.FirstOrDefault((KeyValuePair<short, ItemSlot> m) => m.Value.ItemTypes.FirstOrDefault((ItemType n) => ItemTypeRange.IsAmmo(n)) != ItemType.None).Value;


		return weapon;
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
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

	public override Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		WeaponStats ws = stats as WeaponStats;
		if (ws.CurrentMod.HasValue)
		{
			CurrentModIndex = MathHelper.Clamp(ws.CurrentMod.Value, 0, weaponMods.Count - 1);
			return Task.FromResult(true);
		}
		return Task.FromResult(false);
	}

	public void ConsumePower(double amount)
	{
	}

	public async Task<bool> CanShoot()
	{
		if (Magazine.BulletCount > 0 && Server.Instance.SolarSystem.CurrentTime - lastShotTime > CurrentMod.RateOfFire)
		{
			await Magazine.ChangeQuantity(-1);
			lastShotTime = Server.Instance.SolarSystem.CurrentTime;
			DynamicObj.StatsChanged = true;
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

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataWeapon data)
		{
			Debug.LogWarning("PersistenceObjectDataWeapon data is null", GUID);
		}
		else
		{
			await SetData(data.WeaponData);
		}
	}
}
