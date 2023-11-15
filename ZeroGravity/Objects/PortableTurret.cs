using System;
using System.Collections.Generic;
using OpenHellion.Networking;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

internal class PortableTurret : Item
{
	public bool IsActive;

	public float Damage;

	private Player targetPlayer;

	private PortableTurretStats _stats;

	public bool isStunned;

	public override DynamicObjectStats StatsNew => _stats;

	public PortableTurret(DynamicObjectAuxData data)
	{
		_stats = new PortableTurretStats();
		EventSystem.AddListener(typeof(PortableTurretShootingMessage), PortableTurretShootingMessageListener);
		if (data != null)
		{
			SetData(data);
		}
	}

	public override bool ChangeStats(DynamicObjectStats stats)
	{
		PortableTurretStats ts = stats as PortableTurretStats;
		if (ts.IsActive.HasValue)
		{
			IsActive = ts.IsActive.Value;
		}
		base.DynamicObj.SendStatsToClient();
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

	public override void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			base.LoadPersistenceData(persistenceData);
			if (!(persistenceData is PersistenceObjectDataPortableTurret data))
			{
				Dbg.Warning("PersistenceObjectDataPortableTurret data is null", base.GUID);
			}
			else
			{
				SetData(data.PortableTurretData);
				ApplyTierMultiplier();
			}
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public override void SetData(DynamicObjectAuxData data)
	{
		base.SetData(data);
		PortableTurretData ptd = data as PortableTurretData;
		IsActive = ptd.IsActive;
		Damage = ptd.Damage;
		ApplyTierMultiplier();
	}

	private void PortableTurretShootingMessageListener(NetworkData data)
	{
		PortableTurretShootingMessage ptsm = data as PortableTurretShootingMessage;
		if (ptsm.TurretGUID == base.GUID && !isStunned)
		{
			targetPlayer = Server.Instance.GetPlayer(ptsm.Sender);
			NetworkController.Instance.SendToClientsSubscribedTo(ptsm, -1L, targetPlayer.Parent);
			if (ptsm.IsShooting)
			{
				Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_0_1_sec, DamagePlayer);
			}
			else
			{
				Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_0_1_sec, DamagePlayer);
			}
		}
	}

	public void UnStun()
	{
		if (base.DynamicObj.Parent != null)
		{
			isStunned = false;
			(StatsNew as PortableTurretStats).IsStunned = false;
			base.DynamicObj.SendStatsToClient();
		}
	}

	public override void TakeDamage(Dictionary<TypeOfDamage, float> damages, bool forceTakeDamage = false)
	{
		base.TakeDamage(damages, forceTakeDamage);
		if (damages.ContainsKey(TypeOfDamage.EMP))
		{
			isStunned = true;
			Extensions.Invoke(delegate
			{
				UnStun();
			}, 10.0);
			(StatsNew as PortableTurretStats).IsStunned = isStunned;
			base.DynamicObj.SendStatsToClient();
		}
	}

	private void DamagePlayer(double deltaTime)
	{
		if (targetPlayer != null && targetPlayer.IsAlive)
		{
			targetPlayer.Stats.TakeHitDamage(Damage * base.TierMultiplier * (float)deltaTime, PlayerStats.HitBoxType.Torso, isMelee: false, null, (float)deltaTime);
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
			base.Armor = base.AuxValue;
		}
		base.ApplyTierMultiplier();
	}
}
