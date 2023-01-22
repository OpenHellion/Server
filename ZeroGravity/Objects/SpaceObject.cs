using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenHellion.Networking;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public abstract class SpaceObject
{
	public long GUID;

	private double _ScanningRange = -1.0;

	public ConcurrentDictionary<long, DynamicObject> DynamicObjects = new ConcurrentDictionary<long, DynamicObject>();

	public ConcurrentDictionary<long, Corpse> Corpses = new ConcurrentDictionary<long, Corpse>();

	protected double baseSunHeatTransferPerSec = 9.4E+21;

	public bool IsExposedToSunlight;

	public double SqrDistanceFromSun;

	public bool IsPartOfSpawnSystem;

	public bool IsDestroyed;

	public double ScanningRange
	{
		get
		{
			return _ScanningRange;
		}
		set
		{
			_ScanningRange = ((value < 10000.0) ? 10000.0 : value);
		}
	}

	public virtual SpaceObjectType ObjectType => SpaceObjectType.None;

	public virtual SpaceObject Parent { get; set; }

	public virtual Vector3D Position => Vector3D.Zero;

	public virtual Vector3D Velocity => Vector3D.Zero;

	public SpaceObject(long guid)
	{
		GUID = guid;
	}

	public virtual InitializeSpaceObjectMessage GetInitializeMessage()
	{
		return null;
	}

	public virtual SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		return null;
	}

	public virtual void UpdateTimers(double deltaTime)
	{
		IsExposedToSunlight = isExposedToSunlight(out SqrDistanceFromSun);
	}

	public virtual void Destroy()
	{
		foreach (DynamicObject dobj in new List<DynamicObject>(DynamicObjects.Values))
		{
			dobj.Destroy();
		}
		DestroyObjectMessage dom = new DestroyObjectMessage
		{
			ID = GUID,
			ObjectType = ObjectType
		};
		if (this is SpaceObjectVessel)
		{
			NetworkController.Instance.SendToAllClients(dom, -1L);
		}
		else
		{
			NetworkController.Instance.SendToClientsSubscribedToParents(dom, this, -1L);
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
		IsDestroyed = true;
	}

	private bool isExposedToSunlight(out double sqrDistFromSun)
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
			if (!(this is SpaceObjectTransferable) || !(Parent is Pivot))
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
			hCol = baseSunHeatTransferPerSec * (double)heatCollectionFactor / (double)mass / SqrDistanceFromSun;
		}
		hDis = (double)(heatDissipationFactor / mass) * ((double)currentTemperature + 273.15);
		return (float)((double)currentTemperature + (hCol - hDis) * deltaTime);
	}

	public List<SpaceObject> GetParents(bool includeMe, int depth = 10)
	{
		List<SpaceObject> retVal = new List<SpaceObject>();
		SpaceObject tmpParent = (includeMe ? this : Parent);
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
		if (parent is T)
		{
			return parent as T;
		}
		return GetParent<T>(parent.Parent);
	}
}
