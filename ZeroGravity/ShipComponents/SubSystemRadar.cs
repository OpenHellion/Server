using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class SubSystemRadar : SubSystem
{
	public double ActiveScanSensitivity;

	public double ActiveScanFuzzySensitivity;

	public float ActiveScanDuration;

	public double PassiveScanSensitivity;

	public double WarpDetectionSensitivity;

	private double stopActiveScanTime;

	public override SubSystemType Type => SubSystemType.Radar;

	public override SystemStatus Status
	{
		get
		{
			return base.Status;
		}
		protected set
		{
			if (base.Status != SystemStatus.OnLine && value == SystemStatus.OnLine)
			{
				stopActiveScanTime = Server.SolarSystemTime + (double)ActiveScanDuration;
			}
			base.Status = value;
		}
	}

	public SubSystemRadar(SpaceObjectVessel vessel, VesselObjectID id, SubSystemData ssData)
		: base(vessel, id, ssData)
	{
		_AutoReactivate = false;
	}

	public override void SetAuxData(SystemAuxData auxData)
	{
		if (auxData is RadarAuxData rad)
		{
			ActiveScanSensitivity = rad.ActiveScanSensitivity;
			ActiveScanFuzzySensitivity = rad.ActiveScanFuzzySensitivity;
			PassiveScanSensitivity = rad.PassiveScanSensitivity;
			WarpDetectionSensitivity = rad.WarpDetectionSensitivity;
			ActiveScanDuration = rad.ActiveScanDuration;
		}
	}

	public override void Update(double duration)
	{
		base.Update(duration);
		if (stopActiveScanTime > 0.0 && stopActiveScanTime <= Server.SolarSystemTime && Status == SystemStatus.OnLine)
		{
			GoOffLine(autoRestart: false);
		}
	}

	public void PassiveScan()
	{
		List<long> list = new List<long>();
		List<SpaceObjectVessel> vessels = (from m in Server.Instance.SolarSystem.GetArtificialBodies()
			where m is SpaceObjectVessel
			select m as SpaceObjectVessel).ToList();
		Parallel.ForEach(vessels, delegate(SpaceObjectVessel vessel)
		{
			try
			{
				double magnitude = (ParentVessel.Position - vessel.Position).Magnitude;
				if (magnitude <= PassiveScanSensitivity * 1000.0 * (double)vessel.GetCompoundRadarSignature())
				{
					list.Add(vessel.GUID);
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}
		});
	}

	public void ActiveScan(Vector3D direction, float angle)
	{
		List<long> active = new List<long>();
		List<long> fuzzy = new List<long>();
		List<SpaceObjectVessel> vessels = (from m in Server.Instance.SolarSystem.GetArtificialBodies()
			where m is SpaceObjectVessel
			select m as SpaceObjectVessel).ToList();
		Parallel.ForEach(vessels, delegate(SpaceObjectVessel vessel)
		{
			try
			{
				Vector3D vector3D = vessel.Position - ParentVessel.Position;
				float num = (float)Vector3D.Angle(direction, vector3D.Normalized);
				if (num <= angle / 2f)
				{
					double magnitude = vector3D.Magnitude;
					double num2 = vessel.GetCompoundRadarSignature();
					if (magnitude > PassiveScanSensitivity * 1000.0 * num2)
					{
						if (magnitude <= ActiveScanSensitivity * 1000.0 * num2)
						{
							active.Add(vessel.GUID);
						}
						else if (magnitude <= ActiveScanFuzzySensitivity * 1000.0 * num2)
						{
							fuzzy.Add(vessel.GUID);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Dbg.Exception(ex);
			}
		});
	}
}
