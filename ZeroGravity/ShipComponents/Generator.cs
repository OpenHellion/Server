using System.Collections.Generic;
using Newtonsoft.Json;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public abstract class Generator : VesselComponent, IResourceProvider
{
	private float _NominalOutput;

	private float _Output;

	private float _Temperature;

	protected float _MaxOutput;

	protected float secondaryPowerOutputFactor = 1f;

	protected float secondaryOutputFactor = 1f;

	private HashSet<IResourceUser> _ConnectedConsumers = new HashSet<IResourceUser>();

	public abstract GeneratorType Type { get; }

	public abstract DistributionSystemType OutputType { get; }

	public override SystemStatus Status
	{
		get
		{
			return base.Status;
		}
		protected set
		{
			base.Status = value;
			if (Status != SystemStatus.OnLine)
			{
				_Output = -1f;
				Output = 0f;
			}
		}
	}

	public float NominalOutput
	{
		get
		{
			return _NominalOutput;
		}
		set
		{
			if (_NominalOutput != value)
			{
				StatusChanged = true;
			}
			_NominalOutput = value;
		}
	}

	public float MaxOutput
	{
		get
		{
			return _MaxOutput;
		}
		set
		{
			if (_MaxOutput != value)
			{
				StatusChanged = true;
			}
			_MaxOutput = value;
		}
	}

	public float Output
	{
		get
		{
			return _Output;
		}
		set
		{
			if (_Output != value)
			{
				_Output = value;
				base.OperationRate = MaxOutput > 0f ? Output / MaxOutput : 0f;
				StatusChanged = true;
			}
		}
	}

	public float Temperature
	{
		get
		{
			return _Temperature;
		}
		set
		{
			if (_Temperature != value)
			{
				StatusChanged = true;
			}
			_Temperature = value;
		}
	}

	[JsonIgnore]
	public HashSet<IResourceUser> ConnectedConsumers => _ConnectedConsumers;

	public abstract void SetAuxData(SystemAuxData auxData);

	public Generator(SpaceObjectVessel vessel, VesselObjectID id, GeneratorData genData)
		: base(vessel, id)
	{
		GeneratorData data = ObjectCopier.DeepCopy(genData);
		base.OperationRate = data.OutputRate;
		_NominalOutput = data.NominalOutput;
		ResourceRequirements = DistributionManager.ResourceRequirementsToDictionary(data.ResourceRequirements);
		if (data.SpawnSettings != null)
		{
			SystemSpawnSettings[] spawnSettings = data.SpawnSettings;
			foreach (SystemSpawnSettings sss in spawnSettings)
			{
				if (!vessel.CheckTag(sss.Tag, sss.Case))
				{
					continue;
				}
				float reqFactor = MathHelper.Clamp(sss.ResourceRequirementMultiplier, 0f, float.MaxValue);
				foreach (KeyValuePair<DistributionSystemType, ResourceRequirement> kv in ResourceRequirements)
				{
					ResourceRequirements[kv.Key].Nominal *= reqFactor;
					ResourceRequirements[kv.Key].Standby *= reqFactor;
				}
				break;
			}
		}
		_PowerUpTime = data.PowerUpTime;
		_CoolDownTime = data.CoolDownTime;
		Status = data.Status;
		_AutoReactivate = data.AutoReactivate;
		SetAuxData(data.AuxData);
		MaxOutput = _NominalOutput;
		ParentVessel = vessel;
		baseRadarSignature = data.RadarSignature;
	}

	public virtual IAuxDetails GetAuxDetails()
	{
		return null;
	}

	public virtual void SetDetails(GeneratorDetails details)
	{
		if (details.Status == SystemStatus.OnLine)
		{
			GoOnLine();
		}
		else if (details.Status == SystemStatus.OffLine)
		{
			GoOffLine(autoRestart: false);
		}
		base.OperationRate = details.OutputRate;
		SetAuxDetails(details.AuxDetails);
	}

	public virtual void SetAuxDetails(IAuxDetails auxDetails)
	{
	}

	public override void Update(double duration)
	{
		base.Update(duration);
		if (OutputType == DistributionSystemType.Power)
		{
			MaxOutput = NominalOutput * GetScopeMultiplier(MachineryPartSlotScope.PowerOutput) * secondaryPowerOutputFactor;
		}
		else
		{
			MaxOutput = NominalOutput * GetScopeMultiplier(MachineryPartSlotScope.Output) * secondaryOutputFactor;
		}
	}
}
