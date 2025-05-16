using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using OpenHellion.Net;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Corpse : SpaceObjectTransferable
{
	public double DestroyTime = 120000.0;

	private Player _listenToPlayer;

	private long _listenToSenderID;

	public bool IsInsideSpaceObject;

	public Dictionary<byte, RagdollItemData> RagdollDataList;

	public static readonly double ArenaTimer = TimeSpan.FromMinutes(30.0).TotalMilliseconds;

	public static readonly double EmptyCorpseTimer = TimeSpan.FromMinutes(5.0).TotalMilliseconds;

	public static readonly double OutsideTimer = TimeSpan.FromHours(3.0).TotalMilliseconds;

	public static readonly double InsideModuleTimer = TimeSpan.FromHours(24.0).TotalMilliseconds;

	private Vector3D velocity;

	private Vector3D angularVelocity;

	private DateTime takeoverTime;

	private Timer destroyTimer;

	public Gender Gender;

	private SpaceObject _Parent;

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
				Parent.Corpses.TryRemove(Guid, out var _);
			}
			_Parent = value;
			if (_Parent != null && !_Parent.Corpses.Values.Contains(this))
			{
				Parent.Corpses[Guid] = this;
			}
		}
	}

	public Corpse(Player player)
		: base(GUIDFactory.NextObjectGUID(), player.LocalPosition, player.LocalRotation)
	{
		if (player.Parent is Pivot parent)
		{
			Pivot pivot = (Pivot)(Parent = new Pivot(this, parent));
			foreach (Player pl in Server.Instance.AllPlayers)
			{
				if (pl.IsSubscribedTo(parent.Guid))
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
			DestroyTime = (Parent as SpaceObjectVessel).IsPrefabStationVessel ? ArenaTimer : InsideModuleTimer;
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
			destroyTimer.Elapsed += async delegate
			{
				await DestoyCorpseTimerElapsed(this);
			};
			destroyTimer.Enabled = true;
		}
		Gender = player.Gender;
	}

	private static async Task DestoyCorpseTimerElapsed(object sender)
	{
		if (sender is Corpse obj)
		{
			await obj.Destroy();
		}
	}

	public void ConnectToNetworkController()
	{
		EventSystem.AddListener<CorpseMovementMessage>(CorpseMovementMessageListener);
		EventSystem.AddListener<CorpseStatsMessage>(CorpseStatsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EventSystem.RemoveListener<CorpseMovementMessage>(CorpseMovementMessageListener);
		EventSystem.RemoveListener<CorpseStatsMessage>(CorpseStatsMessageListener);
	}

	private void CorpseMovementMessageListener(NetworkData data)
	{
		var message = data as CorpseMovementMessage;
		if (message.GUID != Guid)
		{
			return;
		}
		if (ListenToSenderID != message.Sender)
		{
			if ((DateTime.UtcNow - takeoverTime).TotalSeconds < 0.8)
			{
				return;
			}
			takeoverTime = DateTime.UtcNow;
			ListenToSenderID = message.Sender;
		}
		if (ListenToSenderID == 0L || message.Sender == ListenToSenderID || _listenToPlayer == null || (_listenToPlayer.Parent != Parent && _listenToSenderID != message.Sender && Parent.ObjectType != SpaceObjectType.DynamicObjectPivot))
		{
			ListenToSenderID = message.Sender;
			RagdollDataList = message.RagdollDataList;
			LocalPosition = message.LocalPosition.ToVector3D();
			LocalRotation = message.LocalRotation.ToQuaternionD();
			velocity = message.Velocity.ToVector3D();
			angularVelocity = message.AngularVelocity.ToVector3D();
			IsInsideSpaceObject = message.IsInsideSpaceObject;
		}
	}

	private async void CorpseStatsMessageListener(NetworkData data)
	{
		var message = data as CorpseStatsMessage;
		if (message.GUID != Guid)
		{
			return;
		}
		SpaceObject oldParent = Parent;
		if (Parent is SpaceObjectVessel && message.ParentType == SpaceObjectType.CorpsePivot)
		{
			SpaceObjectVessel parentVessel = Parent as SpaceObjectVessel;
			Pivot pivot = (Pivot)(Parent = new Pivot(this, parentVessel.MainVessel));
			foreach (Player pl2 in Server.Instance.AllPlayers)
			{
				if (pl2.IsSubscribedTo(parentVessel.Guid))
				{
					pl2.SubscribeTo(pivot);
				}
			}
		}
		else if (Parent is Pivot && message.ParentType != SpaceObjectType.CorpsePivot)
		{
			Pivot pivot2 = Parent as Pivot;
			Parent = Server.Instance.GetObject(message.ParentGUID);
			if (Parent != null)
			{
				foreach (Player pl in Server.Instance.AllPlayers)
				{
					if (pl.IsSubscribedTo(pivot2.Guid))
					{
						pl.UnsubscribeFrom(pivot2);
					}
				}
				Server.Instance.SolarSystem.RemoveArtificialBody(pivot2);
			}
		}
		else if (message.ParentType is SpaceObjectType.Ship or SpaceObjectType.Station or SpaceObjectType.Asteroid)
		{
			Parent = Server.Instance.GetObject(message.ParentGUID);
		}
		else
		{
			Debug.LogError("Dont know what happened to corpse parent", oldParent.Guid, oldParent.ObjectType, message.ParentGUID, message.ParentType);
		}
		if (oldParent != Parent)
		{
		}
		ListenToSenderID = message.Sender;
		await NetworkController.SendToClientsSubscribedTo(message, -1L, oldParent, Parent, oldParent?.Parent, Parent != null ? Parent.Parent : null);
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
			destroyTimer.Elapsed += async delegate
			{
				await DestoyCorpseTimerElapsed(this);
			};
			destroyTimer.Enabled = true;
		}
	}

	public CorpseMovementMessage GetMovementMessage()
	{
		return new CorpseMovementMessage
		{
			GUID = Guid,
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
			GUID = Guid,
			ParentGUID = Parent == null ? -1 : Parent.Guid,
			ParentType = Parent != null ? Parent.ObjectType : SpaceObjectType.None,
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
			GUID = Guid,
			Details = GetDetails()
		};
	}

	public override async Task Destroy()
	{
		DestroyCorpse();
		await base.Destroy();
	}
}
