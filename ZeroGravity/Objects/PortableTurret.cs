using System.Collections.Generic;
using System.Threading.Tasks;
using OpenHellion.Net;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class PortableTurret : Item
{
	public bool IsActive;

	public float Damage;

	private Player targetPlayer;

	private PortableTurretStats _stats = new();

	public bool isStunned;

	public override DynamicObjectStats StatsNew => _stats;

	private PortableTurret()
	{
		EventSystem.AddListener<PortableTurretShootingMessage>(PortableTurretShootingMessageListener);
	}

	public static async Task<PortableTurret> CreateAsync(DynamicObjectAuxData data)
	{
		PortableTurret portableTurret = new();
		if (data != null)
		{
			await portableTurret.SetData(data);
		}

		return portableTurret;
	}

	public override async Task<bool> ChangeStats(DynamicObjectStats stats)
	{
		PortableTurretStats ts = stats as PortableTurretStats;
		if (ts.IsActive.HasValue)
		{
			IsActive = ts.IsActive.Value;
		}
		await DynamicObj.SendStatsToClient();
		return false;
	}

	public override PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataPortableTurret data = new PersistenceObjectDataPortableTurret();
		FillPersistenceData(data);
		data.PortableTurretData = new PortableTurretData();
		FillBaseAuxData(data.PortableTurretData);
		data.PortableTurretData.IsActive = IsActive;
		data.PortableTurretData.Damage = Damage;
		return data;
	}

	public override async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		await base.LoadPersistenceData(persistenceData);
		if (persistenceData is not PersistenceObjectDataPortableTurret data)
		{
			Debug.LogWarning("PersistenceObjectDataPortableTurret data is null", GUID);
		}
		else
		{
			await SetData(data.PortableTurretData);
			ApplyTierMultiplier();
		}
	}

	public override async Task SetData(DynamicObjectAuxData data)
	{
		await base.SetData(data);
		PortableTurretData ptd = data as PortableTurretData;
		IsActive = ptd.IsActive;
		Damage = ptd.Damage;
		ApplyTierMultiplier();
	}

	private async void PortableTurretShootingMessageListener(NetworkData data)
	{
		var message = data as PortableTurretShootingMessage;
		if (message.TurretGUID == GUID && !isStunned)
		{
			targetPlayer = Server.Instance.GetPlayer(message.Sender);
			await NetworkController.SendToClientsSubscribedTo(message, -1L, targetPlayer.Parent);
			if (message.IsShooting)
			{
				Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_0_1_sec, DamagePlayer);
			}
			else
			{
				Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_0_1_sec, DamagePlayer);
			}
		}
	}

	public async Task UnStun()
	{
		if (DynamicObj.Parent != null)
		{
			isStunned = false;
			(StatsNew as PortableTurretStats).IsStunned = false;
			await DynamicObj.SendStatsToClient();
		}
	}

	public override async Task TakeDamage(Dictionary<TypeOfDamage, float> damages, bool forceTakeDamage = false)
	{
		await base.TakeDamage(damages, forceTakeDamage);
		if (damages.ContainsKey(TypeOfDamage.EMP))
		{
			isStunned = true;
			Extensions.Invoke(async delegate
			{
				await UnStun();
			}, 10.0);
			(StatsNew as PortableTurretStats).IsStunned = isStunned;
			await DynamicObj.SendStatsToClient();
		}
	}

	private async void DamagePlayer(double deltaTime)
	{
		if (targetPlayer is { IsAlive: true })
		{
			await targetPlayer.TakeHitDamage(Damage * TierMultiplier * (float)deltaTime, Player.HitBoxType.Torso, isMelee: false, null, (float)deltaTime);
		}
		else
		{
			Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_0_1_sec, DamagePlayer);
		}
	}

	public override void ApplyTierMultiplier()
	{
		if (!TierMultiplierApplied)
		{
			Armor = AuxValue;
		}
		base.ApplyTierMultiplier();
	}

	~PortableTurret()
	{
		EventSystem.RemoveListener<PortableTurretShootingMessage>(PortableTurretShootingMessageListener);
	}
}
