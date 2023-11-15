using System;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class VesselRepairPoint : IPersistantObject
{
	public enum Priority
	{
		None,
		External,
		Internal,
		Fire,
		Breach,
		System
	}

	public VesselObjectID ID;

	private float _MaxHealth;

	private float _Health;

	public SpaceObjectVessel ParentVessel;

	public bool StatusChanged = true;

	public RepairPointDamageType DamageType;

	public VesselComponent AffectedSystem;

	public IAirConsumer AirCousumer;

	public float MalfunctionThreshold;

	public float RepairThreshold;

	public bool External;

	private bool takingDamage;

	public Room Room { get; set; }

	public float MaxHealth
	{
		get
		{
			return _MaxHealth;
		}
		set
		{
			if (_MaxHealth != value)
			{
				StatusChanged = true;
			}
			_MaxHealth = value;
			Update();
		}
	}

	public float Health
	{
		get
		{
			return _Health;
		}
		set
		{
			float newHealth = MathHelper.Clamp(MaxHealth - value < 1f ? MaxHealth : value, 0f, MaxHealth);
			if (_Health != newHealth)
			{
				StatusChanged = true;
				takingDamage = newHealth < _Health;
			}
			_Health = newHealth;
			Update();
		}
	}

	public void Update()
	{
		if (DamageType == RepairPointDamageType.None)
		{
			return;
		}
		float hPerc = _MaxHealth > 0f ? _Health / _MaxHealth : 0f;
		if (DamageType == RepairPointDamageType.System && AffectedSystem != null)
		{
			if (hPerc <= MalfunctionThreshold && !AffectedSystem.Defective && AffectedSystem.Status == SystemStatus.OnLine && takingDamage)
			{
				AffectedSystem.Defective = true;
			}
			else if (hPerc >= RepairThreshold && AffectedSystem.Defective)
			{
				AffectedSystem.Defective = false;
			}
		}
		else if (DamageType == RepairPointDamageType.Gravity && Room != null)
		{
			if (hPerc <= MalfunctionThreshold && !Room.GravityMalfunction)
			{
				Room.GravityMalfunction = true;
			}
			else if (hPerc >= RepairThreshold && Room.GravityMalfunction)
			{
				Room.GravityMalfunction = false;
			}
		}
		else
		{
			if ((DamageType != RepairPointDamageType.Breach && DamageType != RepairPointDamageType.Fire) || Room == null)
			{
				return;
			}
			if (hPerc <= MalfunctionThreshold && AirCousumer == null)
			{
				if (DamageType == RepairPointDamageType.Breach)
				{
					AirCousumer = new AirConsumerBreach(BreachType.Small);
				}
				else
				{
					AirCousumer = new AirConsumerFire(FireType.Small)
					{
						Persistent = true
					};
				}
				if (AirCousumer != null)
				{
					Room.AddAirConsumer(AirCousumer);
					Room.StatusChanged = true;
				}
			}
			else if (hPerc >= RepairThreshold && AirCousumer != null)
			{
				Room.RemoveAirConsumer(AirCousumer);
				AirCousumer = null;
				Room.StatusChanged = true;
			}
		}
	}

	public VesselRepairPoint(SpaceObjectVessel vessel, VesselRepairPointData data, float maxHealth)
	{
		ID = new VesselObjectID(vessel.GUID, data.InSceneID);
		ParentVessel = vessel;
		Room = vessel.Rooms.Find((Room m) => m.ID.InSceneID == data.RoomID);
		_MaxHealth = maxHealth;
		_Health = maxHealth;
		DamageType = data.DamageType;
		External = data.External;
		AffectedSystem = vessel.Systems.Find((VesselComponent m) => m.ID.InSceneID == data.AffectedSystemID);
		MalfunctionThreshold = data.MalfunctionThreshold;
		RepairThreshold = data.RepairThreshold;
		Update();
	}

	public PersistenceObjectData GetPersistenceData()
	{
		return new PersistenceObjectDataRepairPoint
		{
			GUID = ParentVessel.GUID,
			InSceneID = ID.InSceneID,
			MaxHealth = MaxHealth,
			Health = Health
		};
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			if (!(persistenceData is PersistenceObjectDataRepairPoint data))
			{
				Dbg.Warning("PersistenceObjectDataRoom data is null");
				return;
			}
			_MaxHealth = data.MaxHealth;
			_Health = data.Health;
			Update();
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public VesselRepairPointDetails GetDetails()
	{
		bool dmgSpecActive = (DamageType == RepairPointDamageType.System && AffectedSystem != null && AffectedSystem.Defective) || ((DamageType == RepairPointDamageType.Fire || DamageType == RepairPointDamageType.Breach) && AirCousumer != null);
		return new VesselRepairPointDetails
		{
			InSceneID = ID.InSceneID,
			MaxHealth = MaxHealth,
			Health = Health,
			SecondaryDamageActive = dmgSpecActive
		};
	}
}
