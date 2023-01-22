using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using OpenHellion.Networking;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Corpse : SpaceObjectTransferable
{
	public double DestroyTime = 120000.0;

	private Player _listenToPlayer = null;

	private long _listenToSenderID = 0L;

	private DateTime lastSenderTime;

	private double WaitForSenderTime = 1.0;

	public bool IsInsideSpaceObject;

	public Dictionary<byte, RagdollItemData> RagdollDataList;

	private Vector3D pivotPositionCorrection = Vector3D.Zero;

	private Vector3D pivotVelocityCorrection = Vector3D.Zero;

	private DateTime lastPivotResetTime = DateTime.UtcNow;

	public static double ArenaTimer = TimeSpan.FromMinutes(30.0).TotalMilliseconds;

	public static double EmptyCorpseTimer = TimeSpan.FromMinutes(5.0).TotalMilliseconds;

	public static double OutsideTimer = TimeSpan.FromHours(3.0).TotalMilliseconds;

	public static double InsideModuleTimer = TimeSpan.FromHours(24.0).TotalMilliseconds;

	private Vector3D velocity;

	private Vector3D angularVelocity;

	private DateTime takeoverTime;

	private Timer destroyTimer;

	public Gender Gender;

	private SpaceObject _Parent = null;

	public override SpaceObjectType ObjectType => SpaceObjectType.Corpse;

	public long ListenToSenderID
	{
		get
		{
			return _listenToSenderID;
		}
		private set
		{
			_listenToSenderID = value;
			_listenToPlayer = Server.Instance.GetPlayer(value);
		}
	}

	public Inventory CorpseInventory { get; private set; }

	public override SpaceObject Parent
	{
		get
		{
			return _Parent;
		}
		set
		{
			if (_Parent != null)
			{
				Parent.Corpses.TryRemove(GUID, out var _);
			}
			_Parent = value;
			if (_Parent != null && !_Parent.Corpses.Values.Contains(this))
			{
				Parent.Corpses[GUID] = this;
			}
		}
	}

	public Corpse(Player player)
		: base(GUIDFactory.NextObjectGUID(), player.LocalPosition, player.LocalRotation)
	{
		if (player.Parent is Pivot)
		{
			Pivot pivot = (Pivot)(Parent = new Pivot(this, player.Parent as ArtificialBody));
			pivotPositionCorrection = Vector3D.Zero;
			pivotVelocityCorrection = Vector3D.Zero;
			foreach (Player pl in Server.Instance.AllPlayers)
			{
				if (pl.IsSubscribedTo(player.Parent.GUID))
				{
					pl.SubscribeTo(pivot);
				}
			}
		}
		else
		{
			Parent = player.Parent;
		}
		LocalPosition = player.LocalPosition;
		LocalRotation = player.LocalRotation;
		CorpseInventory = player.PlayerInventory;
		CorpseInventory.ChangeParent(this);
		Server.Instance.Add(this);
		ConnectToNetworkController();
		if (Parent is SpaceObjectVessel)
		{
			DestroyTime = ((Parent as SpaceObjectVessel).IsPrefabStationVessel ? ArenaTimer : InsideModuleTimer);
		}
		else
		{
			DestroyTime = OutsideTimer;
		}
		bool isCorpseEmpty = CorpseInventory.HandsSlot.Item == null;
		if (CorpseInventory.CurrOutfit != null)
		{
			foreach (KeyValuePair<short, InventorySlot> inventorySlot in CorpseInventory.CurrOutfit.InventorySlots)
			{
				if (inventorySlot.Value.Item != null)
				{
					isCorpseEmpty = false;
				}
			}
		}
		if (isCorpseEmpty)
		{
			DestroyTime = EmptyCorpseTimer;
		}
		if (DestroyTime > -1.0)
		{
			destroyTimer = new Timer(DestroyTime);
			destroyTimer.Elapsed += delegate
			{
				DestoyCorpseTimerElapsed(this);
			};
			destroyTimer.Enabled = true;
		}
		Gender = player.Gender;
	}

	private static void DestoyCorpseTimerElapsed(object sender)
	{
		if (sender is Corpse obj)
		{
			obj.Destroy();
		}
	}

	public void ConnectToNetworkController()
	{
		EventSystem.AddListener(typeof(CorpseMovementMessage), CorpseMovementMessageListener);
		EventSystem.AddListener(typeof(CorpseStatsMessage), CorpseStatsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EventSystem.RemoveListener(typeof(CorpseMovementMessage), CorpseMovementMessageListener);
		EventSystem.RemoveListener(typeof(CorpseStatsMessage), CorpseStatsMessageListener);
	}

	private void CorpseMovementMessageListener(NetworkData data)
	{
		CorpseMovementMessage mdom = data as CorpseMovementMessage;
		if (mdom.GUID != GUID)
		{
			return;
		}
		if (ListenToSenderID != mdom.Sender)
		{
			if ((DateTime.UtcNow - takeoverTime).TotalSeconds < 0.8)
			{
				return;
			}
			takeoverTime = DateTime.UtcNow;
			ListenToSenderID = mdom.Sender;
		}
		if (ListenToSenderID == 0L || mdom.Sender == ListenToSenderID || _listenToPlayer == null || (_listenToPlayer.Parent != Parent && _listenToSenderID != mdom.Sender && Parent.ObjectType != SpaceObjectType.DynamicObjectPivot))
		{
			ListenToSenderID = mdom.Sender;
			lastSenderTime = DateTime.UtcNow;
			RagdollDataList = mdom.RagdollDataList;
			LocalPosition = mdom.LocalPosition.ToVector3D();
			LocalRotation = mdom.LocalRotation.ToQuaternionD();
			velocity = mdom.Velocity.ToVector3D();
			angularVelocity = mdom.AngularVelocity.ToVector3D();
			IsInsideSpaceObject = mdom.IsInsideSpaceObject;
		}
	}

	private void CorpseStatsMessageListener(NetworkData data)
	{
		CorpseStatsMessage dosm = data as CorpseStatsMessage;
		if (dosm.GUID != GUID)
		{
			return;
		}
		SpaceObject oldParent = Parent;
		if (Parent is SpaceObjectVessel && dosm.ParentType == SpaceObjectType.CorpsePivot)
		{
			SpaceObjectVessel parentVessel = Parent as SpaceObjectVessel;
			Pivot pivot = (Pivot)(Parent = new Pivot(this, parentVessel.MainVessel));
			pivotPositionCorrection = Vector3D.Zero;
			pivotVelocityCorrection = Vector3D.Zero;
			foreach (Player pl2 in Server.Instance.AllPlayers)
			{
				if (pl2.IsSubscribedTo(parentVessel.GUID))
				{
					pl2.SubscribeTo(pivot);
				}
			}
		}
		else if (Parent is Pivot && dosm.ParentType != SpaceObjectType.CorpsePivot)
		{
			Pivot pivot2 = Parent as Pivot;
			Parent = Server.Instance.GetObject(dosm.ParentGUID);
			if (Parent != null)
			{
				foreach (Player pl in Server.Instance.AllPlayers)
				{
					if (pl.IsSubscribedTo(pivot2.GUID))
					{
						pl.UnsubscribeFrom(pivot2);
					}
				}
				Server.Instance.SolarSystem.RemoveArtificialBody(pivot2);
			}
		}
		else if (dosm.ParentType == SpaceObjectType.Ship || dosm.ParentType == SpaceObjectType.Station || dosm.ParentType == SpaceObjectType.Asteroid)
		{
			Parent = Server.Instance.GetObject(dosm.ParentGUID);
		}
		else
		{
			Dbg.Error("Dont know what happened to corpse parent", oldParent.GUID, oldParent.ObjectType, dosm.ParentGUID, dosm.ParentType);
		}
		if (oldParent != Parent)
		{
		}
		ListenToSenderID = dosm.Sender;
		lastSenderTime = DateTime.UtcNow;
		NetworkController.Instance.SendToClientsSubscribedTo(dosm, -1L, oldParent, Parent, oldParent?.Parent, (Parent != null) ? Parent.Parent : null);
	}

	internal void CheckInventoryDestroy()
	{
		if (CorpseInventory.HandsSlot.Item == null && (CorpseInventory.CurrOutfit == null || CorpseInventory.CurrOutfit.InventorySlots.Where((KeyValuePair<short, InventorySlot> m) => m.Value.Item != null) != null))
		{
			if (destroyTimer != null)
			{
				destroyTimer.Dispose();
			}
			destroyTimer = new Timer(TimeSpan.FromMinutes(5.0).TotalMilliseconds);
			destroyTimer.Elapsed += delegate
			{
				DestoyCorpseTimerElapsed(this);
			};
			destroyTimer.Enabled = true;
		}
	}

	public CorpseMovementMessage GetMovementMessage()
	{
		return new CorpseMovementMessage
		{
			GUID = GUID,
			RagdollDataList = RagdollDataList,
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			Velocity = velocity.ToFloatArray(),
			AngularVelocity = angularVelocity.ToFloatArray(),
			IsInsideSpaceObject = IsInsideSpaceObject
		};
	}

	public bool PlayerReceivesMovementMessage(long playerGuid)
	{
		return playerGuid != ListenToSenderID && ListenToSenderID != 0;
	}

	public void DestroyCorpse()
	{
		DisconnectFromNetworkController();
		if (destroyTimer != null)
		{
			destroyTimer.Dispose();
		}
	}

	public CorpseDetails GetDetails()
	{
		List<DynamicObjectDetails> dynamicObjectDatas = new List<DynamicObjectDetails>();
		foreach (DynamicObject d in DynamicObjects.Values)
		{
			dynamicObjectDatas.Add(d.GetDetails());
		}
		return new CorpseDetails
		{
			GUID = GUID,
			ParentGUID = ((Parent == null) ? (-1) : Parent.GUID),
			ParentType = ((Parent != null) ? Parent.ObjectType : SpaceObjectType.None),
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			IsInsideSpaceObject = IsInsideSpaceObject,
			RagdollDataList = RagdollDataList,
			DynamicObjectData = dynamicObjectDatas,
			Gender = Gender
		};
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		return new SpawnCorpseResponseData
		{
			GUID = GUID,
			Details = GetDetails()
		};
	}

	public override void Destroy()
	{
		DestroyCorpse();
		base.Destroy();
	}
}
