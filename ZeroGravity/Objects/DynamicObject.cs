using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenHellion.Net;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public class DynamicObject : SpaceObjectTransferable, IPersistantObject
{
	public short ItemID;

	public ItemType ItemType;

	private Player MasterPlayer;

	private long _MasterClientID;

	private DateTime lastSenderTime;

	private DateTime takeoverTime;

	public double TimeToLive = -1.0;

	public double LastStatsSendTime;

	private Vector3D pivotPositionCorrection = Vector3D.Zero;

	private Vector3D pivotVelocityCorrection = Vector3D.Zero;

	private DateTime lastPivotResetTime = DateTime.UtcNow;

	private SpaceObject _Parent;

	private bool pickedUp;

	public DynamicObjectSceneData DynamicObjectSceneData;

	public AttachPointDetails APDetails;

	public Item Item;

	public float RespawnTime = -1f;

	public float SpawnMaxHealth = -1f;

	public float SpawnMinHealth = -1f;

	public float SpawnWearMultiplier = 1f;

	private Vector3D velocity;

	private Vector3D angularVelocity;

	private float collisionImpactVelocity;

	public override SpaceObjectType ObjectType => SpaceObjectType.DynamicObject;

	public long MasterClientID
	{
		get
		{
			return _MasterClientID;
		}
		private set
		{
			_MasterClientID = value;
			MasterPlayer = Server.Instance.GetPlayer(value);
		}
	}

	public bool IsAttached => Item != null && (Item.Slot != null || Item.AttachPointType != 0 || Parent is DynamicObject);

	public short InvSlotID => (short)(Item is { Slot: not null } ? Item.Slot.SlotID : -1111);

	public bool StatsChanged { get; set; }

	public double LastChangeTime { get; private set; }

	public DynamicObjectStats StatsNew
	{
		get
		{
			if (Item == null)
			{
				return null;
			}
			DynamicObjectStats dos = Item.StatsNew;
			if (dos == null)
			{
				dos = new DynamicObjectStats();
			}
			dos.Health = Item.Health;
			dos.MaxHealth = Item.MaxHealth;
			dos.Armor = Item.Armor;
			dos.Tier = Item.Tier;
			return dos;
		}
	}

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
				_Parent.DynamicObjects.TryRemove(Guid, out var _);
			}
			_Parent = value;
			if (_Parent != null && !_Parent.DynamicObjects.Values.Contains(this))
			{
				_Parent.DynamicObjects[Guid] = this;
			}
			if (_Parent is Pivot)
			{
				Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_1_0_min, SelfDestructCheck);
			}
			else
			{
				Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_1_0_min, SelfDestructCheck);
			}
		}
	}

	public async Task SendStatsToClient()
	{
		DynamicObjectStatsMessage dosm = new DynamicObjectStatsMessage();
		dosm.Info.GUID = Guid;
		dosm.Info.Stats = StatsNew;
		if (Parent != null)
		{
			await NetworkController.SendToClientsSubscribedToParents(dosm, Parent, -1L);
		}
		StatsChanged = false;
		LastStatsSendTime = Server.SolarSystemTime;
	}

	public void PickedUp()
	{
		if (!pickedUp)
		{
			pickedUp = true;
			if (RespawnTime > 0f)
			{
				Server.Instance.DynamicObjectsRespawnList.Add(new Server.DynamicObjectsRespawn
				{
					Data = DynamicObjectSceneData,
					Parent = Parent,
					Timer = RespawnTime,
					RespawnTime = RespawnTime,
					MaxHealth = SpawnMaxHealth,
					MinHealth = SpawnMinHealth,
					ApDetails = APDetails
				});
			}
			if (IsPartOfSpawnSystem)
			{
				SpawnManager.RemoveSpawnSystemObject(this, checkChildren: false);
			}
		}
	}

	private DynamicObject(DynamicObjectSceneData dosd, long guid = -1L)
		: base(guid == -1 ? GUIDFactory.NextObjectGUID() : guid, dosd.Position.ToVector3D(), QuaternionD.LookRotation(dosd.Forward.ToVector3D(), dosd.Up.ToVector3D()))
	{}

	public static async Task<DynamicObject> CreateDynamicObjectAsync(DynamicObjectSceneData dosd, SpaceObject parent, long guid = -1L, bool ignoreSpawnSettings = false)
	{
		var dynamicObject = new DynamicObject(dosd, guid)
		{
			DynamicObjectSceneData = ObjectCopier.DeepCopy(dosd)
		};
		if (ignoreSpawnSettings)
		{
			dynamicObject.DynamicObjectSceneData.SpawnSettings = null;
		}
		dynamicObject.ItemID = dynamicObject.DynamicObjectSceneData.ItemID;
		DynamicObjectData dod = StaticData.DynamicObjectsDataList[dynamicObject.ItemID];
		dynamicObject.ItemType = StaticData.DynamicObjectsDataList[dynamicObject.ItemID].ItemType;
		dynamicObject.Parent = parent;
		dynamicObject.Item = await Item.Create(dynamicObject, dynamicObject.ItemType, dynamicObject.DynamicObjectSceneData.AuxData);
		if (dynamicObject.Item is ICargo cargoItem)
		{
			if (cargoItem.Compartments != null && !ignoreSpawnSettings)
			{
				foreach (CargoCompartmentData ccd in cargoItem.Compartments.Where((CargoCompartmentData m) => m.Resources != null))
				{
					foreach (CargoResourceData resource in ccd.Resources.Where((CargoResourceData m) => m.SpawnSettings != null))
					{
						ResourcesSpawnSettings[] spawnSettings = resource.SpawnSettings;
						foreach (ResourcesSpawnSettings rss in spawnSettings)
						{
							if (dynamicObject.Parent is SpaceObjectVessel && (dynamicObject.Parent as SpaceObjectVessel).CheckTag(rss.Tag, rss.Case))
							{
								float qty = MathHelper.RandomRange(rss.MinQuantity, rss.MaxQuantity);
								resource.Quantity = 0f;
								float avail = ccd.Capacity - ccd.Resources.Sum((CargoResourceData m) => m.Quantity);
								resource.Quantity = MathHelper.Clamp(qty, 0f, avail);
								break;
							}
						}
					}
				}
			}
			if (cargoItem is Canister && cargoItem.Compartments != null)
			{
				cargoItem.GetCompartment().Resources.RemoveAll((CargoResourceData m) => m.Quantity <= float.Epsilon);
			}
		}
		Server.Instance.Add(dynamicObject);
		dynamicObject.ConnectToNetworkController();
		dynamicObject.LastChangeTime = Server.Instance.SolarSystem.CurrentTime;

		return dynamicObject;
	}

	private async void SelfDestructCheck(double dbl)
	{
		if (Parent is Pivot && (DateTime.UtcNow - lastSenderTime).TotalSeconds >= 300.0)
		{
			Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_1_0_min, SelfDestructCheck);
			await Destroy();
		}
	}

	public void ConnectToNetworkController()
	{
		EventSystem.AddListener<DynamicObjectMovementMessage>(DynamicObectMovementMessageListener);
		EventSystem.AddListener<DynamicObjectStatsMessage>(DynamicObjectStatsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EventSystem.RemoveListener<DynamicObjectMovementMessage>(DynamicObectMovementMessageListener);
		EventSystem.RemoveListener<DynamicObjectStatsMessage>(DynamicObjectStatsMessageListener);
	}

	private void DynamicObectMovementMessageListener(NetworkData data)
	{
		var message = data as DynamicObjectMovementMessage;
		if (message.GUID != Guid)
		{
			return;
		}
		if (MasterClientID != message.Sender)
		{
			if ((DateTime.UtcNow - takeoverTime).TotalSeconds < 0.8)
			{
				return;
			}
			takeoverTime = DateTime.UtcNow;
			MasterClientID = message.Sender;
		}
		if (MasterClientID == 0L || message.Sender == MasterClientID || MasterPlayer == null || (MasterPlayer.Parent != Parent && MasterClientID != message.Sender && Parent.ObjectType != SpaceObjectType.DynamicObjectPivot))
		{
			MasterClientID = message.Sender;
			lastSenderTime = DateTime.UtcNow;
			bool changed = false;
			if (!LocalPosition.IsEpsilonEqual(message.LocalPosition.ToVector3D(), 0.0001))
			{
				LocalPosition = message.LocalPosition.ToVector3D();
				changed = true;
			}
			if (!LocalRotation.IsEpsilonEqual(message.LocalRotation.ToQuaternionD(), 1E-05))
			{
				LocalRotation = message.LocalRotation.ToQuaternionD();
				changed = true;
			}
			velocity = message.Velocity.ToVector3D();
			angularVelocity = message.AngularVelocity.ToVector3D();
			collisionImpactVelocity = message.ImpactVelocity;
			if (changed)
			{
				LastChangeTime = Server.Instance.SolarSystem.CurrentTime;
			}
		}
	}

	public void SetStatsChanged()
	{
	}

	private string GetUnknownAttachMessage(DynamicObjectAttachData data)
	{
		DynamicObjectAttachData curr = GetCurrAttachData();
		return string.Format("Current: {0}, {1}, {10}, {2}, {3}, {4}\r\nNew: {5}, {6}, {7}, {8}, {9}", curr.ParentGUID, curr.ParentType, curr.IsAttached, curr.InventorySlotID, curr.APDetails != null ? curr.APDetails.InSceneID : 0, data.ParentGUID, data.ParentType, data.IsAttached, data.InventorySlotID, data.APDetails != null ? data.APDetails.InSceneID : 0, Parent is Ship ? (Parent as Ship).SceneID : GameScenes.SceneId.None);
	}

	private bool CanBePickedUp(Player player, DynamicObject parentDObj)
	{
		return parentDObj.Parent == player || (parentDObj.Item is Outfit && parentDObj.Parent is not Player);
	}

	private async void DynamicObjectStatsMessageListener(NetworkData data)
	{
		var message = data as DynamicObjectStatsMessage;
		if (message.Info.GUID != Guid)
		{
			return;
		}
		SpaceObject oldParent = Parent;

		if (message.Info.Stats != null && Item != null && Parent.Guid == message.Sender)
		{
			StatsChanged = await Item.ChangeStats(message.Info.Stats) || StatsChanged;
		}
		if (message.AttachData != null)
		{
			bool changeListener = false;
			SpaceObject newParent = null;
			Task removeFromOldParent = null;
			if (oldParent is Player && message.Sender == oldParent.Guid)
			{
				removeFromOldParent = new Task(delegate
				{
					(oldParent as Player).PlayerInventory.DropItem(InvSlotID);
				});
			}
			else if (oldParent is SpaceObjectVessel)
			{
				removeFromOldParent = new Task(delegate
				{
					if (Item.AttachPointType != 0 || Item.AttachPointID != null)
					{
						if (Item is MachineryPart)
						{
							(oldParent as SpaceObjectVessel).RemoveMachineryPart(Item.AttachPointID);
						}
						Item.SetAttachPoint(null);
					}
				});
			}
			else if (oldParent is Pivot && MasterClientID == message.Sender)
			{
				removeFromOldParent = new Task(delegate
				{
					Pivot pivot = oldParent as Pivot;
					if (message.AttachData.LocalPosition != null && message.AttachData.LocalRotation != null)
					{
						LocalPosition = message.AttachData.LocalPosition.ToVector3D();
						LocalRotation = message.AttachData.LocalRotation.ToQuaternionD();
					}
					foreach (Player current in Server.Instance.AllPlayers)
					{
						if (current.IsSubscribedTo(pivot.Guid))
						{
							current.UnsubscribeFrom(pivot);
						}
					}
					Server.Instance.SolarSystem.RemoveArtificialBody(pivot);
				});
			}
			else if (oldParent is DynamicObject)
			{
				removeFromOldParent = new Task(delegate
				{
					if ((oldParent as DynamicObject).Item.Slots != null && (oldParent as DynamicObject).Item.Slots.TryGetValue(Item.ItemSlotID, out var value))
					{
						if (Item != value.Item)
						{
							return;
						}
						value.Item = null;
					}
					Item.ItemSlotID = 0;
				});
			}
			else if (oldParent is Corpse)
			{
				removeFromOldParent = new Task(delegate
				{
				});
			}
			if (removeFromOldParent != null)
			{
				if (message.AttachData.ParentType == SpaceObjectType.Player)
				{
					newParent = Server.Instance.GetObject(message.AttachData.ParentGUID) as Player;
					if (await (newParent as Player).PlayerInventory.AddItemToInventory(Item, message.AttachData.InventorySlotID) && oldParent is not Player)
					{
						await removeFromOldParent;
					}
				}
				else if (message.AttachData.ParentType is SpaceObjectType.Ship or SpaceObjectType.Asteroid or SpaceObjectType.Station)
				{
					newParent = Server.Instance.GetObject(message.AttachData.ParentGUID) as SpaceObjectVessel;
					if (message.AttachData.IsAttached)
					{
						await removeFromOldParent;
						Parent = newParent;
						(newParent as SpaceObjectVessel).AttachPoints.TryGetValue(message.AttachData.APDetails.InSceneID, out var ap);
						if (ap == null || !ap.CanFitItem(Item))
						{
							return;
						}
						if (Item != null && message.AttachData.APDetails != null)
						{
							Item.SetAttachPoint(message.AttachData.APDetails);
						}
						if (Item != null && Item.AttachPointType != 0 && Item is MachineryPart
							{
								AttachPointType: AttachPointType.MachineryPartSlot
							} part)
						{
							(newParent as SpaceObjectVessel).FitMachineryPart(part.AttachPointID, part);
						}
					}
					else
					{
						await removeFromOldParent;
						LocalPosition = message.AttachData.LocalPosition.ToVector3D();
						LocalRotation = message.AttachData.LocalPosition.ToQuaternionD();
					}
				}
				else if (message.AttachData.ParentType is SpaceObjectType.PlayerPivot or SpaceObjectType.CorpsePivot or SpaceObjectType.DynamicObjectPivot)
				{
					ArtificialBody refObject = GetParent<ArtificialBody>(oldParent);
					if (refObject is SpaceObjectVessel vessel)
					{
						refObject = vessel.MainVessel;
					}
					newParent = new Pivot(this, refObject);
					await removeFromOldParent;
					LocalPosition = message.AttachData.LocalPosition.ToVector3D();
					LocalRotation = message.AttachData.LocalPosition.ToQuaternionD();
					pivotPositionCorrection = Vector3D.Zero;
					pivotVelocityCorrection = Vector3D.Zero;
					foreach (Player pl in Server.Instance.AllPlayers)
					{
						if (pl.IsSubscribedTo(GetParent<ArtificialBody>(oldParent).Guid))
						{
							pl.SubscribeTo(newParent);
						}
					}
				}
				else if (message.AttachData.ParentType == SpaceObjectType.DynamicObject)
				{
					newParent = Server.Instance.GetObject(message.AttachData.ParentGUID) as DynamicObject;
					ItemSlot slot = null;
					if ((newParent as DynamicObject).Item.Slots != null && (newParent as DynamicObject).Item.Slots.TryGetValue(message.AttachData.ItemSlotID, out slot) && slot != null && slot.CanFitItem(Item))
					{
						PickedUp();
						await removeFromOldParent;
						slot.FitItem(Item);
					}
				}
				else if (message.AttachData.ParentType != SpaceObjectType.Corpse)
				{
				}
				if (Parent != newParent)
				{
					Parent = newParent;
				}
				changeListener = true;
			}
			if (changeListener)
			{
				LastChangeTime = Server.Instance.SolarSystem.CurrentTime;
				if (Parent is SpaceObjectVessel)
				{
					Player senderPl = Server.Instance.GetPlayer(message.Sender);
					if (senderPl != null && Parent == senderPl.Parent)
					{
						MasterClientID = message.Sender;
						lastSenderTime = DateTime.UtcNow;
					}
				}
				else
				{
					MasterClientID = message.Sender;
					lastSenderTime = DateTime.UtcNow;
				}
			}
		}

		if (!StatsChanged && message.AttachData == null)
		{
			return;
		}
		if (StatsChanged && Item != null)
		{
			message.Info.Stats = Item.StatsNew;
		}
		else
		{
			message.Info.Stats = null;
		}
		if (message.AttachData != null)
		{
			float[] tmpVel = message.AttachData.Velocity;
			float[] tmpTorque = message.AttachData.Torque;
			float[] tmpThrowForce = message.AttachData.ThrowForce;
			message.AttachData = GetCurrAttachData();
			message.AttachData.Velocity = tmpVel;
			message.AttachData.Torque = tmpTorque;
			message.AttachData.ThrowForce = tmpThrowForce;
		}
		List<SpaceObject> parents = Parent.GetParents(includeMe: true);
		if (oldParent != null)
		{
			parents.AddRange(oldParent.GetParents(includeMe: true));
		}
		await NetworkController.SendToClientsSubscribedTo(message, -1L, parents.ToArray());
		if (DynamicObjects.Count > 0)
		{
			DynamicObjectsInfoMessage doim = new DynamicObjectsInfoMessage();
			doim.Infos = new List<DynamicObjectInfo>();
			foreach (DynamicObject child in DynamicObjects.Values)
			{
				if (child.StatsChanged)
				{
					doim.Infos.Add(new DynamicObjectInfo
					{
						GUID = child.Guid,
						Stats = child.StatsNew
					});
					child.StatsChanged = false;
				}
			}
			if (doim.Infos.Count > 0)
			{
				await NetworkController.SendToClientsSubscribedTo(doim, -1L, parents.ToArray());
			}
		}
		StatsChanged = false;
	}

	public DynamicObjectAttachData GetCurrAttachData()
	{
		DynamicObjectAttachData dynamicObjectAttachData = new DynamicObjectAttachData();
		dynamicObjectAttachData.ParentGUID = Parent is Player ? (Parent as Player).FakeGuid : Parent.Guid;
		dynamicObjectAttachData.ParentType = Parent.ObjectType;
		dynamicObjectAttachData.IsAttached = IsAttached;
		dynamicObjectAttachData.ItemSlotID = (short)(Item != null ? Item.ItemSlotID : 0);
		dynamicObjectAttachData.InventorySlotID = InvSlotID;
		dynamicObjectAttachData.APDetails = Item == null || Item.AttachPointID == null ? null : new AttachPointDetails
		{
			InSceneID = Item.AttachPointID.InSceneID
		};
		dynamicObjectAttachData.LocalPosition = IsAttached ? null : LocalPosition.ToFloatArray();
		dynamicObjectAttachData.LocalRotation = IsAttached ? null : LocalRotation.ToFloatArray();
		return dynamicObjectAttachData;
	}

	public DynamicObjectMovementMessage GetDynamicObectMovementMessage()
	{
		return new DynamicObjectMovementMessage
		{
			GUID = Guid,
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			Velocity = velocity.ToFloatArray(),
			AngularVelocity = angularVelocity.ToFloatArray(),
			ImpactVelocity = collisionImpactVelocity
		};
	}

	public bool PlayerReceivesMovementMessage(long playerGuid)
	{
		return playerGuid != MasterClientID && MasterClientID != 0;
	}

	public async Task DestroyDynamicObject()
	{
		if (Item != null)
		{
			Item.ItemSlotID = 0;
			Item.SetInventorySlot(null);
			Item.SetAttachPoint(null);
			if (Item.Slots != null)
			{
				foreach (ItemSlot slot in Item.Slots.Values.Where((ItemSlot m) => m.Item != null))
				{
					await slot.Item.DestroyItem();
				}
			}
		}
		DisconnectFromNetworkController();
		await base.Destroy();
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		DynamicObjectDetails det = GetDetails();
		if (Item != null && det?.StatsData != null)
		{
			det.StatsData.Tier = Item.Tier;
			det.StatsData.Armor = Item.Armor;
		}
		return new SpawnDynamicObjectResponseData
		{
			GUID = Guid,
			Details = det
		};
	}

	private List<DynamicObjectDetails> GetChildDynamicObjects()
	{
		if (DynamicObjects == null || DynamicObjects.Count == 0)
		{
			return null;
		}
		List<DynamicObjectDetails> retVal = new List<DynamicObjectDetails>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			retVal.Add(dobj.GetDetails());
		}
		return retVal;
	}

	public DynamicObjectDetails GetDetails()
	{
		return new DynamicObjectDetails
		{
			GUID = Guid,
			ItemID = ItemID,
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			Velocity = velocity.ToFloatArray(),
			AngularVelocity = angularVelocity.ToFloatArray(),
			StatsData = StatsNew,
			AttachData = GetCurrAttachData(),
			ChildObjects = GetChildDynamicObjects()
		};
	}

	public override async Task Destroy()
	{
		await DestroyDynamicObject();
		await base.Destroy();
	}

	public void FillPersistenceData(PersistenceObjectDataDynamicObject data)
	{
		data.GUID = Guid;
		data.ItemID = ItemID;
		data.LocalPosition = LocalPosition.ToFloatArray();
		data.LocalRotation = LocalRotation.ToFloatArray();
		if (!pickedUp && RespawnTime > 0f)
		{
			data.RespawnTime = RespawnTime;
			data.MaxHealth = SpawnMaxHealth;
			data.MinHealth = SpawnMinHealth;
			data.WearMultiplier = SpawnWearMultiplier;
			data.RespawnPosition = DynamicObjectSceneData.Position;
			data.RespawnForward = DynamicObjectSceneData.Forward;
			data.RespawnUp = DynamicObjectSceneData.Up;
			data.RespawnAuxData = DynamicObjectSceneData.AuxData;
		}
		data.ChildObjects = new List<PersistenceObjectData>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			data.ChildObjects.Add(dobj.Item != null ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
		}
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataDynamicObject data = new PersistenceObjectDataDynamicObject();
		FillPersistenceData(data);
		return data;
	}

	public Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		PersistenceObjectDataDynamicObject data = persistenceData as PersistenceObjectDataDynamicObject;
		ItemID = data.ItemID;
		LocalPosition = data.LocalPosition.ToVector3D();
		LocalRotation = data.LocalRotation.ToQuaternionD();
		pickedUp = false;
		RespawnTime = -1f;
		SpawnMaxHealth = -1f;
		SpawnMinHealth = -1f;
		SpawnWearMultiplier = 1f;
		if (data.RespawnTime.HasValue)
		{
			RespawnTime = data.RespawnTime.Value;
		}
		if (data.MaxHealth.HasValue)
		{
			SpawnMaxHealth = data.MaxHealth.Value;
		}
		if (data.MinHealth.HasValue)
		{
			SpawnMinHealth = data.MinHealth.Value;
		}
		if (data.WearMultiplier.HasValue)
		{
			SpawnWearMultiplier = data.WearMultiplier.Value;
		}

		return Task.CompletedTask;
	}

	public static async Task<bool> SpawnDynamicObject(ItemType itemType, GenericItemSubType subType, MachineryPartType mpType, SpaceObject parent, int apId = -1, Vector3D? position = null, Vector3D? forward = null, Vector3D? up = null, int tier = 1, InventorySlot inventorySlot = null, ItemSlot itemSlot = null, bool refill = false)
	{
		DynamicObjectData dod = null;
		dod = itemType == ItemType.GenericItem ? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType && m.DefaultAuxData is GenericItemData data && data.SubType == subType).First()) : itemType != ItemType.MachineryPart ? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType).First()) : ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType && m.DefaultAuxData is MachineryPartData data && data.PartType == mpType).First());
		if (dod == null)
		{
			return false;
		}
		return await SpawnDynamicObject(dod, parent, apId, position, forward, up, tier, inventorySlot, itemSlot, refill);
	}

	public static async Task<bool> SpawnDynamicObject(DynamicObjectData data, SpaceObject parent, int apId = -1, Vector3D? position = null, Vector3D? forward = null, Vector3D? up = null, int tier = 1, InventorySlot inventorySlot = null, ItemSlot itemSlot = null, bool refill = false)
	{
		DynamicObjectSceneData sceneData = new DynamicObjectSceneData
		{
			ItemID = data.ItemID,
			Position = position.HasValue ? position.Value.ToFloatArray() : Vector3D.Zero.ToFloatArray(),
			Forward = forward.HasValue ? forward.Value.ToFloatArray() : Vector3D.Forward.ToFloatArray(),
			Up = up.HasValue ? up.Value.ToFloatArray() : Vector3D.Up.ToFloatArray(),
			AttachPointInSceneId = apId,
			AuxData = ObjectCopier.DeepCopy(data.DefaultAuxData)
		};
		if (sceneData?.AuxData != null)
		{
			sceneData.AuxData.Tier = tier;
		}
		DynamicObject dobj = await CreateDynamicObjectAsync(sceneData, parent, -1L, ignoreSpawnSettings: true);
		if (dobj.Item == null)
		{
			return true;
		}
		if (dobj.Item.Tier != tier)
		{
			dobj.Item.Tier = tier;
		}
		if (apId > 0)
		{
			AttachPointDetails apd = new AttachPointDetails
			{
				InSceneID = apId
			};
			dobj.Item.SetAttachPoint(apd);
			dobj.APDetails = apd;
		}
		if (inventorySlot != null)
		{
			if (inventorySlot.Item == null)
			{
				await inventorySlot.Inventory.AddItemToInventory(dobj.Item, inventorySlot.SlotID);
			}
			else if (parent is Pivot pivot)
			{
				dobj.velocity = pivot.Child.Velocity;
				parent = new Pivot(dobj, pivot);
				dobj.Parent = parent;
			}
		}
		else
		{
			itemSlot?.FitItem(dobj.Item);
		}
		if (refill && dobj.Item is ICargo cargo)
		{
			foreach (CargoCompartmentData ccd in cargo.Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
			{
				using List<CargoResourceData>.Enumerator enumerator2 = ccd.Resources.GetEnumerator();
				if (enumerator2.MoveNext())
				{
					CargoResourceData r = enumerator2.Current;
					await cargo.ChangeQuantityByAsync(ccd.ID, r.ResourceType, ccd.Capacity);
				}
			}
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		res.Data.Add(dobj.GetSpawnResponseData(null));
		await NetworkController.SendToClientsSubscribedTo(res, -1L, dobj.Parent, dobj.Parent.Parent);
		await dobj.SendStatsToClient();
		return true;
	}

	public async Task<DynamicObject> GetCopy()
	{
		return await CreateDynamicObjectAsync(new DynamicObjectSceneData
		{
			ItemID = ItemID,
			Position = Vector3D.Zero.ToFloatArray(),
			Forward = Vector3D.Forward.ToFloatArray(),
			Up = Vector3D.Up.ToFloatArray(),
			AuxData = StaticData.DynamicObjectsDataList[ItemID].DefaultAuxData
		}, this, -1L);;
	}
}
