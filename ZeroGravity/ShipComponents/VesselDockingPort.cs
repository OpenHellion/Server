using System;
using System.Collections.Generic;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class VesselDockingPort : IPersistantObject
{
	public VesselObjectID ID;

	public VesselObjectID DockedToID;

	public SpaceObjectVessel DockedVessel;

	public bool DockingStatus;

	public Vector3D Position;

	public QuaternionD Rotation;

	public int[] DoorsIDs;

	public int OrderID;

	public float DoorPairingDistance;

	public bool Locked;

	public Dictionary<SceneTriggerExecutor, Vector3D> MergeExecutors;

	public double MergeExecutorsDistance;

	public SpaceObjectVessel ParentVessel = null;

	public List<ExecutorMergeDetails> GetMergedExecutors(VesselDockingPort parentPort)
	{
		if (!DockingStatus)
		{
			return null;
		}
		if (parentPort == null)
		{
			parentPort = DockedVessel.DockingPorts.Find((VesselDockingPort m) => m.ID.InSceneID == DockedToID.InSceneID);
		}
		List<ExecutorMergeDetails> retVal = new List<ExecutorMergeDetails>();
		foreach (SceneTriggerExecutor exec in parentPort.MergeExecutors.Keys)
		{
			SceneTriggerExecutor parent = exec.Child != null ? exec : exec.Parent;
			SceneTriggerExecutor child = exec.Parent != null ? exec : exec.Child;
			if (parent != null && child != null)
			{
				retVal.Add(new ExecutorMergeDetails
				{
					ParentTriggerID = new VesselObjectID(parent.ParentShip.GUID, parent.InSceneID),
					ChildTriggerID = new VesselObjectID(child.ParentShip.GUID, child.InSceneID)
				});
			}
		}
		return retVal;
	}

	public PersistenceObjectData GetPersistenceData()
	{
		return new PersistenceObjectDataDockingPort
		{
			InSceneID = ID.InSceneID,
			Locked = Locked
		};
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			if (persistenceData is not PersistenceObjectDataDockingPort data)
			{
				Debug.Warning("PersistenceObjectDataDoor data is null");
			}
			else
			{
				Locked = data.Locked;
			}
		}
		catch (Exception e)
		{
			Debug.Exception(e);
		}
	}

	public SceneDockingPortDetails GetDetails()
	{
		return new SceneDockingPortDetails
		{
			ID = ID,
			DockedToID = DockedToID,
			Locked = Locked,
			DockingStatus = DockingStatus,
			RelativePosition = ParentVessel.RelativePositionFromParent.ToFloatArray(),
			RelativeRotation = ParentVessel.RelativeRotationFromParent.ToFloatArray(),
			CollidersCenterOffset = ParentVessel.IsDocked ? ParentVessel.DockedToMainVessel.VesselData.CollidersCenterOffset : ParentVessel.VesselData.CollidersCenterOffset,
			ExecutorsMerge = GetMergedExecutors(null),
			PairedDoors = ParentVessel.GetPairedDoors(this)
		};
	}
}
