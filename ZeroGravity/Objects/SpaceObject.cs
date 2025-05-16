using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenHellion.Net;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public abstract class SpaceObject
{
	public long Guid;

	private double _scanningRange = -1.0;

	public readonly ConcurrentDictionary<long, DynamicObject> DynamicObjects = new ConcurrentDictionary<long, DynamicObject>();

	public readonly ConcurrentDictionary<long, Corpse> Corpses = new ConcurrentDictionary<long, Corpse>();

	private const double BaseSunHeatTransferPerSec = 9.4E+21;

	public bool IsExposedToSunlight;

	private double _sqrDistanceFromSun;

	public bool IsPartOfSpawnSystem;

	public double ScanningRange
	{
		get
		{
			return _scanningRange;
		}
		set
		{
			_scanningRange = value < 10000.0 ? 10000.0 : value;
		}
	}

	public virtual SpaceObjectType ObjectType => SpaceObjectType.None;

	public virtual SpaceObject Parent { get; set; }

	public virtual Vector3D Position => Vector3D.Zero;

	public virtual Vector3D Velocity => Vector3D.Zero;

	public SpaceObject(long guid)
	{
		Guid = guid;
	}

	public virtual InitializeSpaceObjectMessage GetInitializeMessage()
	{
		return null;
	}

	public virtual SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		return null;
	}

	public virtual Task UpdateTimers(double deltaTime)
	{
		IsExposedToSunlight = CalculateSunlightExposure(out _sqrDistanceFromSun);

		return Task.CompletedTask;
	}

	public virtual async Task Destroy()
	{
		foreach (DynamicObject dynamicObject in new List<DynamicObject>(DynamicObjects.Values))
		{
			await dynamicObject.Destroy();
		}

		DestroyObjectMessage message = new DestroyObjectMessage
		{
			ID = Guid,
			ObjectType = ObjectType
		};

		if (this is SpaceObjectVessel)
		{
			await NetworkController.SendToAll(message);
		}
		else
		{
			await NetworkController.SendToClientsSubscribedToParents(message, this, -1L);
		}

		if (this is Player)
		{
			Server.Instance.Remove(this as Player);
		}
		else if (this is SpaceObjectVessel)
		{
			Server.Instance.Remove(this as SpaceObjectVessel);
		}
		else if (this is DynamicObject)
		{
			Server.Instance.Remove(this as DynamicObject);
		}
		else if (this is Corpse)
		{
			Server.Instance.Remove(this as Corpse);
		}

		if (Parent is Pivot)
		{
			Server.Instance.SolarSystem.RemoveArtificialBody(Parent as Pivot);
		}

		Parent = null;
		if (IsPartOfSpawnSystem)
		{
			SpawnManager.RemoveSpawnSystemObject(this, checkChildren: false);
		}
	}

	/// <summary>
	/// 	Calculates if this vessel is exposed to sunlight.
	/// </summary>
	/// <param name="sqrDistFromSun">Square magnitude of our relative position to the sun.</param>
	/// <returns>If the vessel is exposed to sunlight.</returns>
	private bool CalculateSunlightExposure(out double sqrDistFromSun)
	{
		sqrDistFromSun = 0.0;
		CelestialBody sun = Server.Instance.SolarSystem.GetCelestialBodies()[0];
		while (sun.Parent != null)
		{
			sun = sun.Parent;
		}
		Vector3D position;
		if (this is SpaceObjectVessel)
		{
			position = Position;
		}
		else
		{
			if (this is not SpaceObjectTransferable || Parent is not Pivot)
			{
				return false;
			}
			position = Parent.Position + (this as SpaceObjectTransferable).LocalPosition;
		}
		Vector3D vSunPos = sun.Position - position;
		sqrDistFromSun = vSunPos.SqrMagnitude;
		foreach (CelestialBody cb in Server.Instance.SolarSystem.GetCelestialBodies())
		{
			if (cb.Parent != sun)
			{
				continue;
			}
			Vector3D vCBPos = cb.Position - position;
			if (vCBPos.SqrMagnitude < vSunPos.SqrMagnitude && Vector3D.Project(vCBPos, vSunPos).Normalized == vSunPos.Normalized)
			{
				double x = Vector3D.ProjectOnPlane(vCBPos, vSunPos).Magnitude;
				if (cb.Radius > x)
				{
					return false;
				}
			}
		}
		return true;
	}

	public float SpaceExposureTemperature(float currentTemperature, float heatCollectionFactor, float heatDissipationFactor, float mass, double deltaTime)
	{
		double hCol = 0.0;
		double hDis = 0.0;
		if (IsExposedToSunlight)
		{
			hCol = BaseSunHeatTransferPerSec * heatCollectionFactor / mass / _sqrDistanceFromSun;
		}
		hDis = heatDissipationFactor / mass * (currentTemperature + 273.15);
		return (float)(currentTemperature + (hCol - hDis) * deltaTime);
	}

	public List<SpaceObject> GetParents(bool includeMe, int depth = 10)
	{
		List<SpaceObject> retVal = new List<SpaceObject>();
		SpaceObject tmpParent = includeMe ? this : Parent;
		while (tmpParent != null && depth > 0)
		{
			retVal.Add(tmpParent);
			tmpParent = tmpParent.Parent;
			depth--;
		}
		return retVal;
	}

	public static T GetParent<T>(SpaceObject parent) where T : SpaceObject
	{
		if (parent is T spaceObject)
		{
			return spaceObject;
		}
		return GetParent<T>(parent.Parent);
	}
}
