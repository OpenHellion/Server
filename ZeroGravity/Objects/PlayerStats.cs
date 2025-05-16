using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using OpenHellion.Net;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class PlayerStats
{
	public class HitInfo
	{
		public float damage;

		public bool isMelee;

		public HitBoxType hitType;

		public Vector3D direction;
	}

	public enum HitBoxType
	{
		None = -1,
		Head,
		Torso,
		Arms,
		Legs,
		Abdomen
	}

	public bool GodMode = false;

	public float HealthPoints = 100f;

	public float MaxHealthPoints = 100f;

	public double lastMeleeTime;

	public const float MeleeDamage = 30f;

	public const float MeleeRateOfFire = 1f;

	public Player pl;

	public Dictionary<int, HitInfo> hitQueue = new Dictionary<int, HitInfo>();

	private int hitIdentyfy;

	private PlayerStatsMessage psm = new PlayerStatsMessage();

	private float acummulatedDamage;

	private Timer healTimer;

	private float amountToHeal;

	private float amountToHealStep;

	public PlayerStats()
	{
		healTimer = new Timer(100.0);
		healTimer.Enabled = false;
		healTimer.Elapsed += async delegate
		{
			await HealOverTimeStep();
		};
	}

	public int QueueHit(HitBoxType colType, float damage, Vector3D direction, bool isMelee)
	{
		hitIdentyfy++;
		hitQueue.Add(hitIdentyfy, new HitInfo
		{
			damage = damage,
			hitType = colType,
			direction = direction,
			isMelee = isMelee
		});
		Timer aTimer = new Timer(1000.0);
		int tmpHitIdentifier = hitIdentyfy;
		aTimer.Elapsed += async delegate(object sender, ElapsedEventArgs args)
		{
			await WaitForHit(sender, tmpHitIdentifier);
		};
		return hitIdentyfy;
	}

	public void UnqueueHit(int hitId)
	{
		if (hitQueue.ContainsKey(hitId))
		{
			hitQueue.Remove(hitId);
		}
	}

	private static async Task WaitForHit(object source, int id)
	{
		PlayerStats obj = source as PlayerStats;
		if (obj.hitQueue.ContainsKey(id))
		{
			await obj.TakeHitDamage(id);
		}
	}

	public async Task TakeDamage(HurtType hurtType, float amount)
	{
		await TakeDamage(1f, new PlayerDamage
		{
			HurtType = hurtType,
			Amount = amount
		});
	}

	public async Task TakeDamage(HurtType hurtType, float amount, float deltaTime)
	{
		await TakeDamage(deltaTime, new PlayerDamage
		{
			HurtType = hurtType,
			Amount = amount
		});
	}

	public async Task TakeDamage(params PlayerDamage[] damages)
	{
		await TakeDamage(null, 1f, damages);
	}

	public async Task TakeDamage(float deltaTime, params PlayerDamage[] damages)
	{
		await TakeDamage(null, deltaTime, damages);
	}

	public async Task TakeDamage(Vector3D? shotDirection, float deltaTime, params PlayerDamage[] damages)
	{
		if (GodMode || pl.CurrentSpawnPoint is { Executor: not null, IsPlayerInSpawnPoint: true })
		{
			return;
		}
		float amount = damages.Where((PlayerDamage m) => m.Amount > 0f && m.HurtType is HurtType.Suffocate or HurtType.Pressure).Sum((PlayerDamage m) => m.Amount);
		float hurt = damages.Where((PlayerDamage m) => m.Amount > 0f && m.HurtType != HurtType.Suffocate && m.HurtType != HurtType.Pressure).Sum((PlayerDamage m) => m.Amount);
		amount = pl.PlayerInventory.CurrOutfit == null ? amount + hurt : amount + MathHelper.Clamp(hurt - pl.PlayerInventory.CurrOutfit.Armor * deltaTime, 0f, float.MaxValue);
		if (amount <= float.Epsilon)
		{
			return;
		}
		HurtType causeOfDeath = damages.OrderBy((PlayerDamage m) => m.Amount).Reverse().FirstOrDefault()?.HurtType ?? HurtType.None;
		HealthPoints = MathHelper.Clamp(HealthPoints - amount, 0f, MaxHealthPoints);
		if (HealthPoints <= float.Epsilon)
		{
			await pl.KillPlayer(causeOfDeath);
			return;
		}
		acummulatedDamage += amount;
		foreach (PlayerDamage dmg in damages.Where((PlayerDamage m) => m.Amount > 0f))
		{
			PlayerDamage pd = psm.DamageList.FirstOrDefault((PlayerDamage m) => m.HurtType == dmg.HurtType);
			if (pd == null)
			{
				psm.DamageList.Add(dmg);
			}
			else
			{
				pd.Amount += dmg.Amount;
			}
		}
		psm.ShotDirection = shotDirection.HasValue ? shotDirection.Value.ToFloatArray() : null;
		if (acummulatedDamage > 1f)
		{
			psm.GUID = pl.FakeGuid;
			psm.Health = (int)HealthPoints;
			await NetworkController.Send(pl.Guid, psm);
			psm = new PlayerStatsMessage();
			acummulatedDamage = 0f;
		}
	}

	public async Task Heal(float amount)
	{
		amount = amount > 0f ? amount : 0f;
		if (!(amount <= float.Epsilon) && HealthPoints != MaxHealthPoints)
		{
			HealthPoints = MathHelper.Clamp(HealthPoints + amount, 0f, MaxHealthPoints);
			PlayerStatsMessage psm = new PlayerStatsMessage
			{
				GUID = pl.FakeGuid,
				Health = (int)HealthPoints
			};
			await NetworkController.Send(pl.Guid, psm);
		}
	}

	private async Task HealOverTimeStep()
	{
		amountToHeal -= amountToHealStep;
		float healAmount = amountToHealStep;
		if (amountToHeal <= 0f)
		{
			healAmount += amountToHeal;
			await Heal(amountToHealStep);
			healTimer.Enabled = false;
		}
		else
		{
			await Heal(amountToHealStep);
		}
	}

	public void HealOverTime(float amountOverSec, float duration)
	{
		if (healTimer.Enabled)
		{
			amountToHeal += amountOverSec * duration;
			amountToHealStep = (amountToHealStep + amountOverSec * 0.1f) * 0.5f;
		}
		else
		{
			amountToHeal = amountOverSec * duration;
			amountToHealStep = amountOverSec * 0.1f;
			healTimer.Enabled = true;
		}
	}

	public async Task DoCollisionDamage(float speed)
	{
		double threshold = 6.5;
		float hp = 0f;
		if (speed >= threshold)
		{
			hp = (float)((speed - threshold) * (speed - threshold) / 10.0 + speed) * (pl.PlayerInventory.CurrOutfit != null ? pl.PlayerInventory.CurrOutfit.CollisionResistance : 1f);
		}
		await TakeDamage(HurtType.Impact, hp);
	}

	public async Task<float> TakeHitDamage(int id)
	{
		if (GodMode || pl.CurrentSpawnPoint is { Executor: not null, IsPlayerInSpawnPoint: true })
		{
			return 0f;
		}
		if (hitQueue.TryGetValue(id, out var hitInfo))
		{
			float damage = await TakeHitDamage(hitInfo.damage, hitInfo.hitType, hitInfo.isMelee, hitInfo.direction);
			UnqueueHit(id);
			return damage;
		}
		return 0f;
	}

	public async Task<float> TakeHitDamage(float damage, HitBoxType hitType, bool isMelee, Vector3D? direction = null, float duration = 1f)
	{
		Outfit outfit = pl.PlayerInventory.CurrOutfit;
		Helmet helmet = pl.CurrentHelmet;
		float resistanceMulti = 1f;
		float reductionValue = 0f;
		float bodyDmgMulti = 1f;
		switch (hitType)
		{
		case HitBoxType.None:
			resistanceMulti = 0f;
			Debug.LogError("UNKNOWN HITBOX TYPE", pl.Guid);
			break;
		case HitBoxType.Head:
			bodyDmgMulti = 10f;
			resistanceMulti = helmet?.DamageResistance ?? 1f;
			reductionValue = helmet?.DamageReduction ?? 0f;
			break;
		case HitBoxType.Torso:
			bodyDmgMulti = 5f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceTorso;
				reductionValue = outfit.DamageReductionTorso;
			}
			break;
		case HitBoxType.Arms:
			bodyDmgMulti = 1f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceArms;
				reductionValue = outfit.DamageReductionArms;
			}
			break;
		case HitBoxType.Legs:
			bodyDmgMulti = 1f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceLegs;
				reductionValue = outfit.DamageReductionLegs;
			}
			break;
		case HitBoxType.Abdomen:
			bodyDmgMulti = 2f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceAbdomen;
				reductionValue = outfit.DamageReductionAbdomen;
			}
			break;
		default:
			Debug.LogError("UNKNOWN HITBOX TYPE DEFAULT", pl.Guid);
			break;
		}
		if (isMelee)
		{
			bodyDmgMulti = 1f;
		}
		float amount = (damage - reductionValue * duration) * resistanceMulti * bodyDmgMulti;
		await TakeDamage(direction, duration, new PlayerDamage
		{
			HurtType = HurtType.Shot,
			Amount = amount
		});
		return amount;
	}
}
