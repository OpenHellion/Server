using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenHellion.Networking;
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

	private Player MasterPlayer = null;

	private long _MasterClientID = 0L;

	private DateTime lastSenderTime;

	private DateTime takeoverTime;

	public double TimeToLive = -1.0;

	public double LastStatsSendTime;

	private Vector3D pivotPositionCorrection = Vector3D.Zero;

	private Vector3D pivotVelocityCorrection = Vector3D.Zero;

	private DateTime lastPivotResetTime = DateTime.UtcNow;

	private SpaceObject _Parent = null;

	private bool pickedUp = false;

	public DynamicObjectSceneData DynamicObjectSceneData;

	public AttachPointDetails APDetails;

	public Item Item = null;

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

	public short InvSlotID => (short)((Item != null && Item.Slot != null) ? Item.Slot.SlotID : (-1111));

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
				_Parent.DynamicObjects.TryRemove(GUID, out var _);
			}
			_Parent = value;
			if (_Parent != null && !_Parent.DynamicObjects.Values.Contains(this))
			{
				_Parent.DynamicObjects[GUID] = this;
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

	public void SendStatsToClient()
	{
		DynamicObjectStatsMessage dosm = new DynamicObjectStatsMessage();
		dosm.Info.GUID = GUID;
		dosm.Info.Stats = StatsNew;
		if (Parent != null)
		{
			NetworkController.Instance.SendToClientsSubscribedToParents(dosm, Parent, -1L);
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
					WearMultiplier = SpawnWearMultiplier,
					APDetails = APDetails
				});
			}
			if (IsPartOfSpawnSystem)
			{
				SpawnManager.RemoveSpawnSystemObject(this, checkChildren: false);
			}
		}
	}

	public DynamicObject(DynamicObjectSceneData dosd, SpaceObject parent, long guid = -1L, bool ignoreSpawnSettings = false)
		: base((guid == -1) ? GUIDFactory.NextObjectGUID() : guid, dosd.Position.ToVector3D(), QuaternionD.LookRotation(dosd.Forward.ToVector3D(), dosd.Up.ToVector3D()))
	{
		DynamicObjectSceneData = ObjectCopier.DeepCopy(dosd);
		if (ignoreSpawnSettings)
		{
			DynamicObjectSceneData.SpawnSettings = null;
		}
		ItemID = DynamicObjectSceneData.ItemID;
		DynamicObjectData dod = StaticData.DynamicObjectsDataList[ItemID];
		ItemType = StaticData.DynamicObjectsDataList[ItemID].ItemType;
		Parent = parent;
		Item = Item.Create(this, ItemType, DynamicObjectSceneData.AuxData);
		if (Item is ICargo)
		{
			ICargo cargoItem = Item as ICargo;
			if (cargoItem.Compartments != null && !ignoreSpawnSettings)
			{
				foreach (CargoCompartmentData ccd in cargoItem.Compartments.Where((CargoCompartmentData m) => m.Resources != null))
				{
					foreach (CargoResourceData resource in ccd.Resources.Where((CargoResourceData m) => m.SpawnSettings != null))
					{
						ResourcesSpawnSettings[] spawnSettings = resource.SpawnSettings;
						foreach (ResourcesSpawnSettings rss in spawnSettings)
						{
							if (Parent is SpaceObjectVessel && (Parent as SpaceObjectVessel).CheckTag(rss.Tag, rss.Case))
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
		Server.Instance.Add(this);
		ConnectToNetworkController();
		LastChangeTime = Server.Instance.SolarSystem.CurrentTime;
	}

	private void SelfDestructCheck(double dbl)
	{
		if (Parent is Pivot && (DateTime.UtcNow - lastSenderTime).TotalSeconds >= 300.0)
		{
			Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_1_0_min, SelfDestructCheck);
			Destroy();
		}
	}

	public void ConnectToNetworkController()
	{
		EventSystem.AddListener(typeof(DynamicObectMovementMessage), DynamicObectMovementMessageListener);
		EventSystem.AddListener(typeof(DynamicObjectStatsMessage), DynamicObjectStatsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EventSystem.RemoveListener(typeof(DynamicObectMovementMessage), DynamicObectMovementMessageListener);
		EventSystem.RemoveListener(typeof(DynamicObjectStatsMessage), DynamicObjectStatsMessageListener);
	}

	private void DynamicObectMovementMessageListener(NetworkData data)
	{
		DynamicObectMovementMessage mdom = data as DynamicObectMovementMessage;
		if (mdom.GUID != GUID)
		{
			return;
		}
		if (MasterClientID != mdom.Sender)
		{
			if ((DateTime.UtcNow - takeoverTime).TotalSeconds < 0.8)
			{
				return;
			}
			takeoverTime = DateTime.UtcNow;
			MasterClientID = mdom.Sender;
		}
		if (MasterClientID == 0L || mdom.Sender == MasterClientID || MasterPlayer == null || (MasterPlayer.Parent != Parent && MasterClientID != mdom.Sender && Parent.ObjectType != SpaceObjectType.DynamicObjectPivot))
		{
			MasterClientID = mdom.Sender;
			lastSenderTime = DateTime.UtcNow;
			bool changed = false;
			if (!LocalPosition.IsEpsilonEqual(mdom.LocalPosition.ToVector3D(), 0.0001))
			{
				LocalPosition = mdom.LocalPosition.ToVector3D();
				changed = true;
			}
			if (!LocalRotation.IsEpsilonEqual(mdom.LocalRotation.ToQuaternionD(), 1E-05))
			{
				LocalRotation = mdom.LocalRotation.ToQuaternionD();
				changed = true;
			}
			velocity = mdom.Velocity.ToVector3D();
			angularVelocity = mdom.AngularVelocity.ToVector3D();
			collisionImpactVelocity = mdom.ImpactVelocity;
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
		return string.Format("Current: {0}, {1}, {10}, {2}, {3}, {4}\r\nNew: {5}, {6}, {7}, {8}, {9}", curr.ParentGUID, curr.ParentType, curr.IsAttached, curr.InventorySlotID, (curr.APDetails != null) ? curr.APDetails.InSceneID : 0, data.ParentGUID, data.ParentType, data.IsAttached, data.InventorySlotID, (data.APDetails != null) ? data.APDetails.InSceneID : 0, (Parent != null && Parent is Ship) ? (Parent as Ship).SceneID : GameScenes.SceneID.None);
	}

	private bool CanBePickedUp(Player player, DynamicObject parentDObj)
	{
		return parentDObj.Parent == player || (parentDObj.Item != null && parentDObj.Item is Outfit && !(parentDObj.Parent is Player));
	}

	private void DynamicObjectStatsMessageListener(NetworkData data)
	{
		DynamicObjectStatsMessage dosm = data as DynamicObjectStatsMessage;
		if (dosm.Info.GUID != GUID)
		{
			return;
		}
		SpaceObject oldParent = Parent;
		try
		{
			if (dosm.Info.Stats != null && Item != null && Parent.GUID == dosm.Sender)
			{
				StatsChanged = Item.ChangeStats(dosm.Info.Stats) || StatsChanged;
			}
			if (dosm.AttachData != null)
			{
				bool changeListener = false;
				SpaceObject newParent = null;
				Task removeFromOldParent = null;
				if (oldParent is Player && dosm.Sender == oldParent.GUID)
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
				else if (oldParent is Pivot && MasterClientID == dosm.Sender)
				{
					removeFromOldParent = new Task(delegate
					{
						Pivot pivot = oldParent as Pivot;
						if (dosm.AttachData.LocalPosition != null && dosm.AttachData.LocalRotation != null)
						{
							LocalPosition = dosm.AttachData.LocalPosition.ToVector3D();
							LocalRotation = dosm.AttachData.LocalRotation.ToQuaternionD();
						}
						foreach (Player current in Server.Instance.AllPlayers)
						{
							if (current.IsSubscribedTo(pivot.GUID))
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
					if (dosm.AttachData.ParentType == SpaceObjectType.Player)
					{
						newParent = Server.Instance.GetObject(dosm.AttachData.ParentGUID) as Player;
						if ((newParent as Player).PlayerInventory.AddItemToInventory(Item, dosm.AttachData.InventorySlotID) && !(oldParent is Player))
						{
							removeFromOldParent.RunSynchronously();
						}
					}
					else if (dosm.AttachData.ParentType == SpaceObjectType.Ship || dosm.AttachData.ParentType == SpaceObjectType.Asteroid || dosm.AttachData.ParentType == SpaceObjectType.Station)
					{
						newParent = Server.Instance.GetObject(dosm.AttachData.ParentGUID) as SpaceObjectVessel;
						if (dosm.AttachData.IsAttached)
						{
							removeFromOldParent.RunSynchronously();
							Parent = newParent;
							(newParent as SpaceObjectVessel).AttachPoints.TryGetValue(dosm.AttachData.APDetails.InSceneID, out var ap);
							if (ap == null || !ap.CanFitItem(Item))
							{
								return;
							}
							if (Item != null && dosm.AttachData.APDetails != null)
							{
								Item.SetAttachPoint(dosm.AttachData.APDetails);
							}
							if (Item != null && Item.AttachPointType != 0 && Item is MachineryPart && Item.AttachPointType == AttachPointType.MachineryPartSlot)
							{
								(newParent as SpaceObjectVessel).FitMachineryPart(Item.AttachPointID, Item as MachineryPart);
							}
						}
						else
						{
							removeFromOldParent.RunSynchronously();
							LocalPosition = dosm.AttachData.LocalPosition.ToVector3D();
							LocalRotation = dosm.AttachData.LocalPosition.ToQuaternionD();
						}
					}
					else if (dosm.AttachData.ParentType == SpaceObjectType.PlayerPivot || dosm.AttachData.ParentType == SpaceObjectType.CorpsePivot || dosm.AttachData.ParentType == SpaceObjectType.DynamicObjectPivot)
					{
						ArtificialBody refObject = SpaceObject.GetParent<ArtificialBody>(oldParent);
						if (refObject is SpaceObjectVessel)
						{
							refObject = (refObject as SpaceObjectVessel).MainVessel;
						}
						newParent = new Pivot(this, refObject);
						removeFromOldParent.RunSynchronously();
						LocalPosition = dosm.AttachData.LocalPosition.ToVector3D();
						LocalRotation = dosm.AttachData.LocalPosition.ToQuaternionD();
						pivotPositionCorrection = Vector3D.Zero;
						pivotVelocityCorrection = Vector3D.Zero;
						foreach (Player pl in Server.Instance.AllPlayers)
						{
							if (pl.IsSubscribedTo(SpaceObject.GetParent<ArtificialBody>(oldParent).GUID))
							{
								pl.SubscribeTo(newParent);
							}
						}
					}
					else if (dosm.AttachData.ParentType == SpaceObjectType.DynamicObject)
					{
						newParent = Server.Instance.GetObject(dosm.AttachData.ParentGUID) as DynamicObject;
						ItemSlot slot = null;
						if ((newParent as DynamicObject).Item.Slots != null && (newParent as DynamicObject).Item.Slots.TryGetValue(dosm.AttachData.ItemSlotID, out slot) && slot != null && slot.CanFitItem(Item))
						{
							PickedUp();
							removeFromOldParent.RunSynchronously();
							slot.FitItem(Item);
						}
					}
					else if (dosm.AttachData.ParentType != SpaceObjectType.Corpse)
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
						Player senderPl = Server.Instance.GetPlayer(dosm.Sender);
						if (senderPl != null && Parent == senderPl.Parent)
						{
							MasterClientID = dosm.Sender;
							lastSenderTime = DateTime.UtcNow;
						}
					}
					else
					{
						MasterClientID = dosm.Sender;
						lastSenderTime = DateTime.UtcNow;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Dbg.Exception(ex);
		}
		if (!StatsChanged && dosm.AttachData == null)
		{
			return;
		}
		if (StatsChanged && Item != null)
		{
			dosm.Info.Stats = Item.StatsNew;
		}
		else
		{
			dosm.Info.Stats = null;
		}
		if (dosm.AttachData != null)
		{
			float[] tmpVel = dosm.AttachData.Velocity;
			float[] tmpTorque = dosm.AttachData.Torque;
			float[] tmpThrowForce = dosm.AttachData.ThrowForce;
			dosm.AttachData = GetCurrAttachData();
			dosm.AttachData.Velocity = tmpVel;
			dosm.AttachData.Torque = tmpTorque;
			dosm.AttachData.ThrowForce = tmpThrowForce;
		}
		List<SpaceObject> parents = Parent.GetParents(includeMe: true);
		if (oldParent != null)
		{
			parents.AddRange(oldParent.GetParents(includeMe: true));
		}
		NetworkController.Instance.SendToClientsSubscribedTo(dosm, -1L, parents.ToArray());
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
						GUID = child.GUID,
						Stats = child.StatsNew
					});
					child.StatsChanged = false;
				}
			}
			if (doim.Infos.Count > 0)
			{
				NetworkController.Instance.SendToClientsSubscribedTo(doim, -1L, parents.ToArray());
			}
		}
		StatsChanged = false;
	}

	public DynamicObjectAttachData GetCurrAttachData()
	{
		DynamicObjectAttachData dynamicObjectAttachData = new DynamicObjectAttachData();
		dynamicObjectAttachData.ParentGUID = ((Parent is Player) ? (Parent as Player).FakeGuid : Parent.GUID);
		dynamicObjectAttachData.ParentType = Parent.ObjectType;
		dynamicObjectAttachData.IsAttached = IsAttached;
		dynamicObjectAttachData.ItemSlotID = (short)((Item != null) ? Item.ItemSlotID : 0);
		dynamicObjectAttachData.InventorySlotID = InvSlotID;
		dynamicObjectAttachData.APDetails = ((Item == null || Item.AttachPointID == null) ? null : new AttachPointDetails
		{
			InSceneID = Item.AttachPointID.InSceneID
		});
		dynamicObjectAttachData.LocalPosition = (IsAttached ? null : LocalPosition.ToFloatArray());
		dynamicObjectAttachData.LocalRotation = (IsAttached ? null : LocalRotation.ToFloatArray());
		return dynamicObjectAttachData;
	}

	public DynamicObectMovementMessage GetDynamicObectMovementMessage()
	{
		return new DynamicObectMovementMessage
		{
			GUID = GUID,
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

	public void DestroyDynamicObject()
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
					slot.Item.DestroyItem();
				}
			}
		}
		DisconnectFromNetworkController();
		base.Destroy();
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
			GUID = GUID,
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
			GUID = GUID,
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

	public override void Destroy()
	{
		DestroyDynamicObject();
		base.Destroy();
	}

	public void FillPersistenceData(PersistenceObjectDataDynamicObject data)
	{
		data.GUID = GUID;
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
			data.ChildObjects.Add((dobj.Item != null) ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
		}
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataDynamicObject data = new PersistenceObjectDataDynamicObject();
		FillPersistenceData(data);
		return data;
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
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
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public static bool SpawnDynamicObject(ItemType itemType, GenericItemSubType subType, MachineryPartType mpType, SpaceObject parent, int apId = -1, Vector3D? position = null, Vector3D? forward = null, Vector3D? up = null, int tier = 1, InventorySlot inventorySlot = null, ItemSlot itemSlot = null, bool refill = false)
	{
		DynamicObjectData dod = null;
		dod = ((itemType == ItemType.GenericItem) ? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType && m.DefaultAuxData != null && m.DefaultAuxData is GenericItemData && (m.DefaultAuxData as GenericItemData).SubType == subType).First()) : ((itemType != ItemType.MachineryPart) ? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType).First()) : ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.Where((DynamicObjectData m) => m.ItemType == itemType && m.DefaultAuxData != null && m.DefaultAuxData is MachineryPartData && (m.DefaultAuxData as MachineryPartData).PartType == mpType).First())));
		if (dod == null)
		{
			return false;
		}
		return SpawnDynamicObject(dod, parent, apId, position, forward, up, tier, inventorySlot, itemSlot, refill);
	}

	public static bool SpawnDynamicObject(DynamicObjectData data, SpaceObject parent, int apId = -1, Vector3D? position = null, Vector3D? forward = null, Vector3D? up = null, int tier = 1, InventorySlot inventorySlot = null, ItemSlot itemSlot = null, bool refill = false)
	{
		DynamicObjectSceneData sceneData = new DynamicObjectSceneData
		{
			ItemID = data.ItemID,
			Position = (position.HasValue ? position.Value.ToFloatArray() : Vector3D.Zero.ToFloatArray()),
			Forward = (forward.HasValue ? forward.Value.ToFloatArray() : Vector3D.Forward.ToFloatArray()),
			Up = (up.HasValue ? up.Value.ToFloatArray() : Vector3D.Up.ToFloatArray()),
			AttachPointInSceneId = apId,
			AuxData = ObjectCopier.DeepCopy(data.DefaultAuxData)
		};
		if (sceneData?.AuxData != null)
		{
			sceneData.AuxData.Tier = tier;
		}
		DynamicObject dobj = new DynamicObject(sceneData, parent, -1L, ignoreSpawnSettings: true);
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
				inventorySlot.Inventory.AddItemToInventory(dobj.Item, inventorySlot.SlotID);
			}
			else if (parent is Pivot)
			{
				dobj.velocity = (parent as Pivot).Child.Velocity;
				parent = new Pivot(dobj, parent as ArtificialBody);
				dobj.Parent = parent;
			}
		}
		else
		{
			itemSlot?.FitItem(dobj.Item);
		}
		if (refill && dobj.Item is ICargo)
		{
			foreach (CargoCompartmentData ccd in (dobj.Item as ICargo).Compartments.Where((CargoCompartmentData m) => m.AllowOnlyOneType))
			{
				using List<CargoResourceData>.Enumerator enumerator2 = ccd.Resources.GetEnumerator();
				if (enumerator2.MoveNext())
				{
					CargoResourceData r = enumerator2.Current;
					(dobj.Item as ICargo).ChangeQuantityBy(ccd.ID, r.ResourceType, ccd.Capacity);
				}
			}
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		res.Data.Add(dobj.GetSpawnResponseData(null));
		NetworkController.Instance.SendToClientsSubscribedTo(res, -1L, dobj.Parent, dobj.Parent.Parent);
		dobj.SendStatsToClient();
		return true;
	}

	public DynamicObject GetCopy()
	{
		return new DynamicObject(new DynamicObjectSceneData
		{
			ItemID = ItemID,
			Position = Vector3D.Zero.ToFloatArray(),
			Forward = Vector3D.Forward.ToFloatArray(),
			Up = Vector3D.Up.ToFloatArray(),
			AuxData = StaticData.DynamicObjectsDataList[ItemID].DefaultAuxData
		}, this, -1L);
	}
}
