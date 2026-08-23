using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenHellion.Net;
using OpenHellion.Net.Message;
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

	// Starts counting from when the object comes into existence. Left at DateTime.MinValue, an
	// object restored from persistence looks abandoned for two millennia and SelfDestructCheck
	// deletes it on the first tick, before any client ever gets the chance to touch it.
	private DateTime lastSenderTime = DateTime.UtcNow;

	private DateTime takeoverTime;

	public double LastStatsSendTime;

	private SpaceObject _Parent;

	private bool pickedUp;

	public DynamicObjectSceneData DynamicObjectSceneData;

	public AttachPointDetails APDetails;

	public Item Item;

	public float RespawnTime = -1f;

	public float SpawnMaxHealth = -1f;

	public float SpawnMinHealth = -1f;

	public float SpawnWearMultiplier = 1f;

	public Vector3D AngularVelocity;

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
			dos ??= new DynamicObjectStats();
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
			_Parent?.DynamicObjects.Remove(Guid);
			_Parent = value;
			if (_Parent != null && !_Parent.DynamicObjects.Contains(Guid))
			{
				_Parent.DynamicObjects.Add(Guid);
			}
			LastChangeTime = Server.SolarSystemTime;
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
		EventSystem.AddListener<MoveObjectRequest>(MoveObjectRequestListener);
		EventSystem.AddListener<DynamicObjectStatsMessage>(DynamicObjectStatsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EventSystem.RemoveListener<MoveObjectRequest>(MoveObjectRequestListener);
		EventSystem.RemoveListener<DynamicObjectStatsMessage>(DynamicObjectStatsMessageListener);
	}

	/// <summary>
	/// 	Applies a movement request that targets this dynamic object. The same message moves any
	/// 	object (see <see cref="MoveObjectRequest" />); players ignore guids that aren't their own,
	/// 	and we ignore guids that aren't ours.
	/// </summary>
	private void MoveObjectRequestListener(NetworkData data)
	{
		var message = data as MoveObjectRequest;
		if (message.Guid != Guid)
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
			if (!LocalPosition.IsEpsilonEqual(message.Position.ToVector3D(), 0.0001))
			{
				LocalPosition = message.Position.ToVector3D();
				changed = true;
			}
			if (!LocalRotation.IsEpsilonEqual(message.Rotation.ToQuaternionD(), 1E-05))
			{
				LocalRotation = message.Rotation.ToQuaternionD();
				changed = true;
			}
			AngularVelocity = message.AngularVelocity.ToVector3D();
			if (changed)
			{
				LastChangeTime = Server.Instance.SolarSystem.CurrentTime;
			}
		}
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
			Action removeFromOldParent = null;
			if (oldParent is Player && message.Sender == oldParent.Guid)
			{
				removeFromOldParent = delegate
				{
					(oldParent as Player).PlayerInventory.DropItem(InvSlotID);
				};
			}
			else if (oldParent is SpaceObjectVessel)
			{
				removeFromOldParent = delegate
				{
					if (Item.AttachPointType != 0 || Item.AttachPointID != null)
					{
						if (Item is MachineryPart)
						{
							(oldParent as SpaceObjectVessel).RemoveMachineryPart(Item.AttachPointID);
						}
						Item.SetAttachPoint(null);
					}
				};
			}
			else if (oldParent is Pivot && MasterClientID == message.Sender)
			{
				removeFromOldParent = delegate
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
				};
			}
			else if (oldParent is DynamicObject)
			{
				removeFromOldParent = delegate
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
				};
			}
			else if (oldParent is Corpse)
			{
				removeFromOldParent = delegate
				{
				};
			}
			if (removeFromOldParent != null)
			{
				if (message.AttachData.ParentType == SpaceObjectType.Player)
				{
					SpaceObject requestedParent = Server.Instance.GetSpaceObject(message.AttachData.ParentGUID);
					if ((requestedParent as Player ?? (requestedParent as Pivot)?.Child as Player) is not { } player)
					{
						Debug.LogWarning("Ignored an attach to a player nothing is registered under", message.AttachData.ParentGUID, "sender", message.Sender);
						return;
					}

					newParent = player;
					if (await player.PlayerInventory.AddItemToInventory(Item, message.AttachData.InventorySlotID) && oldParent is not Player)
					{
						removeFromOldParent();
					}

					LocalPosition = Vector3D.Zero;
					LocalRotation = QuaternionD.Identity;
				}
				else if (message.AttachData.ParentType is SpaceObjectType.Ship or SpaceObjectType.Asteroid or SpaceObjectType.Station)
				{
					newParent = Server.Instance.GetSpaceObject(message.AttachData.ParentGUID) as SpaceObjectVessel;
					if (message.AttachData.IsAttached)
					{
						(newParent as SpaceObjectVessel).AttachPoints.TryGetValue(message.AttachData.APDetails.InSceneID, out var ap);
						if (ap == null || !ap.CanFitItem(Item))
						{
							return;
						}

						removeFromOldParent();
						Parent = newParent;
						LocalPosition = Vector3D.Zero;
						LocalRotation = QuaternionD.Identity;

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
						removeFromOldParent();
						if (message.AttachData.LocalPosition != null && message.AttachData.LocalRotation != null)
						{
							LocalPosition = message.AttachData.LocalPosition.ToVector3D();
							LocalRotation = message.AttachData.LocalRotation.ToQuaternionD();
						}
					}
				}
				else if (message.AttachData.ParentType is SpaceObjectType.PlayerPivot or SpaceObjectType.CorpsePivot or SpaceObjectType.DynamicObjectPivot)
				{
					ArtificialBody refObject = GetParent<ArtificialBody>(oldParent);

					// The module the item was released in, before refObject is moved up to the station
					// it is docked into. Null means it was released in open space.
					SpaceObjectVessel releasedInside = refObject as SpaceObjectVessel;

					if (refObject is SpaceObjectVessel vessel)
					{
						refObject = vessel.MainVessel;
					}

					if (releasedInside != null)
					{
						// Let go of inside a vessel, so it stays the vessel's own, and the reply
						// below carries that back to the client.
						//
						// The client says otherwise, over and over: an item resting on the floor
						// leaves and re-enters its room trigger about once a second, because every
						// answer re-parents it and re-parenting fires the trigger again. Believing
						// it puts the item on a pivot of its own, and a pivot's contents are only
						// ever described once, in the message announcing the drop - the movement
						// message walks vessels, and a pivot is not a vessel. So the item becomes
						// invisible to anyone who arrives afterwards, including the player who
						// dropped it once they reconnect, and there is nothing under any vessel to
						// write down when the world is saved.
						//
						// The shipped game hides all of this: items let go of in a vessel are
						// cleaned up after five minutes and were never saved, so nobody could tell
						// they had already stopped existing for everyone else.
						newParent = releasedInside;
					}
					else
					{
					  newParent = new Pivot(this, refObject);
					  removeFromOldParent();
					  if (message.AttachData.LocalPosition != null && message.AttachData.LocalRotation != null)
					  {
              LocalPosition = message.AttachData.LocalPosition.ToVector3D();
					  	LocalRotation = message.AttachData.LocalRotation.ToQuaternionD();
            }
					}
				}
				else if (message.AttachData.ParentType == SpaceObjectType.DynamicObject)
				{
					newParent = Server.Instance.GetSpaceObject(message.AttachData.ParentGUID) as DynamicObject;
					ItemSlot slot = null;
					if ((newParent as DynamicObject).Item.Slots != null && (newParent as DynamicObject).Item.Slots.TryGetValue(message.AttachData.ItemSlotID, out slot) && slot != null && slot.CanFitItem(Item))
					{
						PickedUp();
						removeFromOldParent();
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
			DynamicObjectsInfoMessage doim = new DynamicObjectsInfoMessage
			{
				Infos = []
			};
			foreach (long childGuid in DynamicObjects)
			{
				if (Server.Instance.SpaceObjects.TryGet(childGuid, out SpaceObject obj) && obj is DynamicObject child && child.StatsChanged)
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
		return new DynamicObjectAttachData
		{
			ParentGUID = Parent is Player ? (Parent as Player).FakeGuid : Parent.Guid,
			ParentType = Parent.ObjectType,
			IsAttached = IsAttached,
			ItemSlotID = (short)(Item != null ? Item.ItemSlotID : 0),
			InventorySlotID = InvSlotID,
			APDetails = Item == null || Item.AttachPointID == null ? null : new AttachPointDetails
			{
				InSceneID = Item.AttachPointID.InSceneID
			},
			LocalPosition = IsAttached ? null : LocalPosition.ToFloatArray(),
			LocalRotation = IsAttached ? null : LocalRotation.ToFloatArray()
		};
	}

	private DynamicObjectDetails[] GetChildDynamicObjects()
	{
		if (DynamicObjects == null || DynamicObjects.Count == 0)
		{
			return null;
		}

		return [.. DynamicObjects.Select((m, _) =>
		{
			Server.Instance.SpaceObjects.TryGet(m, out var obj);
			return (obj as DynamicObject).GetDetails();
		})];
	}

	public DynamicObjectDetails GetDetails()
	{
		DynamicObjectStats stats = StatsNew;
		if (Item != null && stats != null)
		{
			stats.Tier = Item.Tier;
			stats.Armor = Item.Armor;
		}
		return new DynamicObjectDetails
		{
			GUID = Guid,
			ItemID = ItemID,
			StatsData = stats,
			AttachData = GetCurrAttachData(),
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			Velocity = Velocity.ToFloatArray(),
			AngularVelocity = AngularVelocity.ToFloatArray(),
			ChildObjects = GetChildDynamicObjects()
		};
	}

	/// <summary>
	/// 	Returns what a player or corpse (owner) carries.
	/// </summary>
	public static DynamicObjectDetails[] GetCarriedDetails(SpaceObject owner)
	{
		List<DynamicObject> carried = [.. owner.DynamicObjects.Select(Server.Instance.GetDynamicObject).Where(m => m != null)];
		DynamicObjectDetails outfit = carried.FirstOrDefault(m => m.InvSlotID == InventorySlot.OutfitSlotID)?.GetDetails();
		if (outfit == null)
		{
			return [.. carried.Select(m => m.GetDetails())];
		}

		outfit.ChildObjects = [.. outfit.ChildObjects ?? [], .. carried.Where(m => m.InvSlotID >= InventorySlot.StartSlotID).Select(m => m.GetDetails())];
		return [outfit, .. carried.Where(m => m.InvSlotID != InventorySlot.OutfitSlotID && m.InvSlotID < InventorySlot.StartSlotID).Select(m => m.GetDetails())];
	}

	public override async Task Destroy()
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
			data.RespawnRotation = QuaternionD.LookRotation(DynamicObjectSceneData.Forward.ToVector3D(), DynamicObjectSceneData.Up.ToVector3D()).ToFloatArray();
			data.RespawnAuxData = DynamicObjectSceneData.AuxData;
		}
		data.ChildObjects = [];
		foreach (long guid in DynamicObjects)
		{
			if (Server.Instance.TryGetSpaceObject(guid, out var obj) && obj is DynamicObject dobj)
			{
				data.ChildObjects.Add(dobj.Item != null ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
			}
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
		dod = itemType == ItemType.GenericItem
			? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.First((DynamicObjectData m) => m.ItemType == itemType && m.DefaultAuxData is GenericItemData data && data.SubType == subType))
			: itemType != ItemType.MachineryPart
				? ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.First((DynamicObjectData m) => m.ItemType == itemType))
				: ObjectCopier.DeepCopy(StaticData.DynamicObjectsDataList.Values.First((DynamicObjectData m) => m.ItemType == itemType
					&& m.DefaultAuxData is MachineryPartData data
					&& data.PartType == mpType));
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
