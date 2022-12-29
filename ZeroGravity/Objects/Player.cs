using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public class Player : SpaceObjectTransferable, IPersistantObject, IAirConsumer
{
	public double LastMovementMessageSolarSystemTime = -1.0;

	public List<long> UpdateArtificialBodyMovement = new List<long>();

	public bool IsAlive = false;

	private bool _EnvironmentReady = false;

	private bool _PlayerReady = false;

	public string Name;

	public string SteamId;

	public Gender Gender;

	public byte HeadType;

	public byte HairType;

	public float MouseLook;

	public float FreeLookX;

	public float FreeLookY;

	public float CameraY;

	public Vector3D ZeroGOrientation;

	public long FakeGuid;

	public CharacterAnimationData AnimationData;

	public int AnimationStatsMask;

	private HashSet<long> subscribedToSpaceObjects = new HashSet<long>();

	private float[] gravity;

	public Vector3D LocalVelocity = Vector3D.Zero;

	private float collisionImpactVelocity;

	private Helmet currentHelmet;

	private Jetpack currentJetpack;

	public Dictionary<byte, RagdollItemData> RagdollData;

	private sbyte[] jetpackDirection;

	public CharacterTransformData TransformData = null;

	public const float NoAirMaxDamage = 1f;

	public const float NoPressureMaxDamage = 2f;

	public const float TemperatureMaxDamage = 0.5f;

	public double updateItemTimer;

	public const float timeToUpdateItems = 0.3f;

	private Vector3D pivotPositionCorrection = Vector3D.Zero;

	private Vector3D pivotVelocityCorrection = Vector3D.Zero;

	private Vector3D? dockUndockPositionCorrection = null;

	private QuaternionD? dockUndockRotationCorrection = null;

	private bool dockUndockWaitForMsg = false;

	public bool IsAdmin = false;

	private SpaceObject _Parent = null;

	public Room CurrentRoom;

	private bool isOutsideRoom;

	public Inventory PlayerInventory;

	public float CoreTemperature = 37f;

	private bool outfitTempRegulationActive;

	private double lastPivotResetTime;

	private double lateDisconnectWait;

	public bool IsInsideSpawnPoint;

	public ConcurrentQueue<ShipStatsMessage> MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();

	private PlayerStatsMessage lastPlayerStatsMessage = null;

	public VesselObjectID LockedToTriggerID;

	public List<Quest> Quests;

	public List<ItemCompoundType> Blueprints = ObjectCopier.DeepCopy(StaticData.DefaultBlueprints);

	public NavigationMapDetails NavMapDetails = null;

	public bool Initialize = true;

	public override SpaceObjectType ObjectType => SpaceObjectType.Player;

	public bool EnvironmentReady
	{
		get
		{
			return _EnvironmentReady;
		}
		set
		{
			if (_EnvironmentReady = value)
			{
				_EnvironmentReady = value;
				if (PlayerReady && EnvironmentReady)
				{
					Initialize = false;
				}
			}
		}
	}

	public bool PlayerReady
	{
		get
		{
			return _PlayerReady;
		}
		set
		{
			if (_PlayerReady = value)
			{
				_PlayerReady = value;
				if (PlayerReady && EnvironmentReady)
				{
					Initialize = false;
				}
			}
		}
	}

	public PlayerStats Stats { get; private set; }

	public ShipSpawnPoint CurrentSpawnPoint { get; private set; }

	public ShipSpawnPoint AuthorizedSpawnPoint { get; private set; }

	public Helmet CurrentHelmet
	{
		get
		{
			return currentHelmet;
		}
		set
		{
			currentHelmet = value;
			if (value == null && CurrentJetpack != null)
			{
				CurrentJetpack.Helmet = null;
			}
		}
	}

	public Jetpack CurrentJetpack
	{
		get
		{
			return currentJetpack;
		}
		set
		{
			currentJetpack = value;
			if (value == null && CurrentHelmet != null)
			{
				CurrentHelmet.Jetpack = value;
			}
		}
	}

	public Item ItemInHands => PlayerInventory.HandsSlot.Item;

	public int Health
	{
		get
		{
			return (int)Stats.HealthPoints;
		}
		set
		{
			Stats.HealthPoints = MathHelper.Clamp(value, 0, 100);
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
			if (_Parent != null && _Parent is SpaceObjectVessel)
			{
				((SpaceObjectVessel)_Parent).RemovePlayerFromCrew(this);
			}
			_Parent = value;
			if (_Parent != null && _Parent is SpaceObjectVessel)
			{
				((SpaceObjectVessel)_Parent).AddPlayerToCrew(this);
			}
		}
	}

	public bool GodMode => Stats.GodMode;

	public bool IsPilotingVessel { get; private set; }

	public float? AmbientTemperature
	{
		get
		{
			if (PlayerInventory.CurrOutfit != null)
			{
				return PlayerInventory.CurrOutfit.InternalTemperature;
			}
			if (Parent is SpaceObjectVessel)
			{
				return (Parent as SpaceObjectVessel).Temperature;
			}
			return null;
		}
	}

	public float AirQualityDegradationRate
	{
		get
		{
			if (IsInsideSpawnPoint || !IsAlive || (CurrentHelmet != null && (!CurrentHelmet.IsVisorToggleable || CurrentHelmet.IsVisorActive)))
			{
				return 0f;
			}
			return 0.05f;
		}
	}

	public float AirQuantityDecreaseRate => 0f;

	public bool AffectsQuality => AirQualityDegradationRate > 0f;

	public bool AffectsQuantity => false;

	public Player(long guid, Vector3D localPosition, QuaternionD localRotation, string name, string steamId, Gender gender, byte headType, byte hairType, bool addToServerList = true, Player clone = null)
		: base(guid, localPosition, localRotation)
	{
		FakeGuid = GUIDFactory.NextPlayerFakeGUID();
		Name = name;
		SteamId = steamId;
		Gender = gender;
		HeadType = headType;
		HairType = hairType;
		Stats = new PlayerStats();
		Stats.pl = this;
		PlayerInventory = new Inventory(this);
		Quests = StaticData.QuestsData.Select((QuestData m) => new Quest(m, this)).ToList();
		if (addToServerList)
		{
			Server.Instance.Add(this);
		}
		if (clone == null)
		{
			return;
		}
		if (clone.PlayerInventory.OutfitSlot.Item != null)
		{
			PlayerInventory.AddItemToInventory(clone.PlayerInventory.OutfitSlot.Item.GetCopy(), clone.PlayerInventory.OutfitSlot.SlotID);
			foreach (InventorySlot sl in clone.PlayerInventory.CurrOutfit.InventorySlots.Values.Where((InventorySlot m) => m.Item != null))
			{
				PlayerInventory.AddItemToInventory(clone.PlayerInventory.CurrOutfit.InventorySlots[sl.SlotID].Item.GetCopy(), sl.SlotID);
			}
		}
		if (clone.PlayerInventory.HandsSlot.Item != null)
		{
			PlayerInventory.AddItemToInventory(clone.PlayerInventory.HandsSlot.Item.GetCopy(), clone.PlayerInventory.HandsSlot.SlotID);
		}
	}

	public void ConnectToNetworkController()
	{
		EnvironmentReady = false;
		PlayerReady = false;
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(CharacterMovementMessage), UpdateMovementListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(EnvironmentReadyMessage), EnvironmentReadyListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(PlayerShootingMessage), PlayerShootingListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(PlayerHitMessage), PlayerHitListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(PlayerStatsMessage), PlayerStatsMessageListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(PlayerDrillingMessage), PlayerDrillingListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(PlayerRoomMessage), PlayerRoomMessageListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(SuicideRequest), SuicideListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(AuthorizedVesselsRequest), AuthorizedVesselsRequestListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(LockToTriggerMessage), LockToTriggerMessageListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(QuestTriggerMessage), QuestTriggerMessageListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(SkipQuestMessage), SkipQuestMessageListener);
		Server.Instance.NetworkController.EventSystem.AddListener(typeof(NavigationMapDetailsMessage), NavigationMapDetailsMessageListener);
	}

	public void DiconnectFromNetworkContoller()
	{
		EnvironmentReady = false;
		PlayerReady = false;
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(CharacterMovementMessage), UpdateMovementListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(EnvironmentReadyMessage), EnvironmentReadyListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(PlayerShootingMessage), PlayerShootingListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(PlayerHitMessage), PlayerHitListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(PlayerStatsMessage), PlayerStatsMessageListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(PlayerDrillingMessage), PlayerDrillingListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(PlayerRoomMessage), PlayerRoomMessageListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(SuicideRequest), SuicideListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(AuthorizedVesselsRequest), AuthorizedVesselsRequestListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(LockToTriggerMessage), LockToTriggerMessageListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(SkipQuestMessage), SkipQuestMessageListener);
		Server.Instance.NetworkController.EventSystem.RemoveListener(typeof(NavigationMapDetailsMessage), NavigationMapDetailsMessageListener);
	}

	public void RemovePlayerFromTrigger()
	{
		if (lastPlayerStatsMessage != null)
		{
			lastPlayerStatsMessage.LockedToTriggerID = null;
			Server.Instance.NetworkController.SendToClientsSubscribedTo(lastPlayerStatsMessage, GUID, Parent);
		}
	}

	private void PlayerStatsMessageListener(NetworkData data)
	{
		PlayerStatsMessage psm = data as PlayerStatsMessage;
		if (FakeGuid == psm.GUID)
		{
			lastPlayerStatsMessage = psm;
			if (psm.AnimationMaskChanged.HasValue && psm.AnimationMaskChanged.Value)
			{
				AnimationStatsMask = psm.AnimationStatesMask;
			}
			else
			{
				psm.AnimationStatesMask = AnimationStatsMask;
			}
			LockedToTriggerID = psm.LockedToTriggerID;
			IsPilotingVessel = psm.IsPilotingVessel;
			Server.Instance.NetworkController.SendToClientsSubscribedTo(psm, data.Sender, Parent);
		}
	}

	protected void PlayerHitListener(NetworkData data)
	{
		PlayerHitMessage phm = data as PlayerHitMessage;
		if (phm.Sender != GUID)
		{
			return;
		}
		if (phm.HitSuccessfull || MathHelper.RandomNextDouble() > 0.699999988079071)
		{
			if (Stats.TakeHitDamage(phm.HitIndentifier) > 0f)
			{
				PlayerStatsMessage psm = new PlayerStatsMessage();
				psm.Health = (int)Stats.HealthPoints;
				psm.GUID = FakeGuid;
				Server.Instance.NetworkController.SendToGameClient(GUID, psm);
			}
		}
		else
		{
			Stats.UnqueueHit(phm.HitIndentifier);
		}
	}

	private static void PassTroughtShootMessage(PlayerShootingMessage psm)
	{
		PlayerShootingMessage sending = new PlayerShootingMessage();
		sending.HitIndentifier = -1;
		sending.ShotData = psm.ShotData;
		sending.HitGUID = psm.HitGUID;
		sending.GUID = psm.GUID;
		Server.Instance.NetworkController.SendToAllClients(sending, psm.Sender);
	}

	protected void PlayerShootingListener(NetworkData data)
	{
		if (data.Sender != GUID)
		{
			return;
		}
		PlayerShootingMessage psm = data as PlayerShootingMessage;
		Weapon wep = PlayerInventory.GetHandsItemIfType<Weapon>() as Weapon;
		if (wep == null && !psm.ShotData.IsMeleeAttack)
		{
			return;
		}
		bool rateValid = false;
		if (psm.ShotData.IsMeleeAttack)
		{
			if (Server.Instance.SolarSystem.CurrentTime - Stats.lastMeleeTime > 1.0)
			{
				rateValid = true;
				Stats.lastMeleeTime = Server.Instance.SolarSystem.CurrentTime;
			}
		}
		else if (wep != null && wep.CanShoot())
		{
			rateValid = true;
		}
		if (!rateValid)
		{
			return;
		}
		if (psm.HitGUID == -1)
		{
			psm.HitGUID = -2L;
			PassTroughtShootMessage(psm);
			return;
		}
		SpaceObject sp = Server.Instance.GetObject(psm.HitGUID);
		float damage = 0f;
		damage = ((wep == null) ? (psm.ShotData.IsMeleeAttack ? 30f : 0f) : (psm.ShotData.IsMeleeAttack ? wep.MeleeDamage : wep.Damage));
		if (sp is DynamicObject)
		{
			(sp as DynamicObject).Item.TakeDamage(new Dictionary<TypeOfDamage, float> { 
			{
				TypeOfDamage.Hit,
				damage
			} });
		}
		if (Server.Instance.GetObject(psm.HitGUID) is Player hitPlayer)
		{
			Server.Instance.NetworkController.SendToClientsSubscribedTo(psm, GUID, Parent, hitPlayer.Parent);
			float realDamage = hitPlayer.Stats.TakeHitDamage(hitPlayer.Stats.QueueHit((PlayerStats.HitBoxType)psm.ShotData.colliderType, damage, psm.ShotData.Orientation.ToVector3D(), psm.ShotData.IsMeleeAttack));
		}
	}

	protected void PlayerDrillingListener(NetworkData data)
	{
		PlayerDrillingMessage pdm = data as PlayerDrillingMessage;
		HandDrill drill = ItemInHands as HandDrill;
		if (data.Sender == GUID && drill != null)
		{
			PlayerDrillingMessage pdmForOtherChar = new PlayerDrillingMessage();
			pdmForOtherChar.DrillersGUID = FakeGuid;
			pdmForOtherChar.dontPlayEffect = pdm.dontPlayEffect;
			pdmForOtherChar.isDrilling = pdm.isDrilling;
			Server.Instance.NetworkController.SendToClientsSubscribedTo(pdmForOtherChar, GUID, Parent);
			if (drill.CanDrill && pdm.MiningPointID != null && Server.Instance.GetVessel(pdm.MiningPointID.VesselGUID) is Asteroid asteroid && asteroid.MiningPoints.TryGetValue(pdm.MiningPointID.InSceneID, out var miningPoint))
			{
				drill.Battery.ChangeQuantity((0f - drill.BatteryUsage) * pdm.MiningTime * drill.TierMultiplier);
				drill.Canister.ChangeQuantityBy(0, miningPoint.ResourceType, drill.DrillingStrength * drill.TierMultiplier * pdm.MiningTime);
				miningPoint.Quantity = MathHelper.Clamp(miningPoint.Quantity - drill.DrillingStrength * drill.TierMultiplier * pdm.MiningTime, 0f, miningPoint.MaxQuantity);
				drill.DrillBit.TakeDamage(TypeOfDamage.Degradation, drill.DrillBit.UsageWear * pdm.MiningTime * drill.DrillBit.TierMultiplier, forceTakeDamage: true);
			}
		}
	}

	protected void EnvironmentReadyListener(NetworkData data)
	{
		EnvironmentReadyMessage erd = data as EnvironmentReadyMessage;
		if (erd.Sender != GUID)
		{
			return;
		}
		EnvironmentReady = true;
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		Server.Instance.NetworkController.AddCharacterSpawnsToResponse(this, ref res);
		if (Parent is SpaceObjectVessel)
		{
			SpaceObjectVessel vessel = Parent as SpaceObjectVessel;
			if (vessel.IsDocked)
			{
				vessel = vessel.DockedToMainVessel;
			}
			foreach (DynamicObject obj4 in vessel.DynamicObjects.Values)
			{
				res.Data.Add(obj4.GetSpawnResponseData(this));
			}
			foreach (Corpse obj3 in vessel.Corpses.Values)
			{
				res.Data.Add(obj3.GetSpawnResponseData(this));
			}
			if (vessel.AllDockedVessels.Count > 0)
			{
				foreach (SpaceObjectVessel child in vessel.AllDockedVessels)
				{
					foreach (DynamicObject obj2 in child.DynamicObjects.Values)
					{
						res.Data.Add(obj2.GetSpawnResponseData(this));
					}
					foreach (Corpse obj in child.Corpses.Values)
					{
						res.Data.Add(obj.GetSpawnResponseData(this));
					}
				}
			}
		}
		Server.Instance.NetworkController.SendToGameClient(GUID, res);
		Server.Instance.NetworkController.SendCharacterSpawnToOtherPlayers(this);
		MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		foreach (SpaceObjectVessel ves in from m in subscribedToSpaceObjects
			select Server.Instance.GetObject(m) into m
			where m != null && m is SpaceObjectVessel
			select m as SpaceObjectVessel)
		{
			VesselObjects vesselObjects = ves.GetVesselObjects();
			if (vesselObjects.SceneTriggerExecuters != null)
			{
				foreach (SceneTriggerExecuterDetails sted in vesselObjects.SceneTriggerExecuters)
				{
					sted.IsImmediate = true;
				}
			}
			Server.Instance.NetworkController.SendToGameClient(GUID, new ShipStatsMessage
			{
				GUID = ves.GUID,
				VesselObjects = vesselObjects
			});
		}
		IsAlive = true;
	}

	private void SuicideListener(NetworkData data)
	{
		if (data.Sender == GUID)
		{
			KillYourself(HurtType.Suicide);
		}
	}

	private void AuthorizedVesselsRequestListener(NetworkData data)
	{
		if (data.Sender == GUID)
		{
			SendAuthorizedVesselsResponse();
		}
	}

	public void SendAuthorizedVesselsResponse()
	{
		AuthorizedVesselsResponse avr = new AuthorizedVesselsResponse
		{
			GUIDs = (from m in Server.Instance.AllVessels
				where m.AuthorizedPersonel.FirstOrDefault((AuthorizedPerson n) => n.PlayerGUID == GUID) != null
				select m.GUID).ToArray()
		};
		Server.Instance.NetworkController.SendToGameClient(GUID, avr);
	}

	private void QuestTriggerMessageListener(NetworkData data)
	{
		QuestTriggerMessage qtm = data as QuestTriggerMessage;
		if (qtm.Sender != GUID)
		{
			return;
		}
		Quest quest = Quests.FirstOrDefault((Quest m) => m.ID == qtm.QuestID);
		if (quest == null || (quest.DependencyQuests != null && Quests.FirstOrDefault((Quest m) => quest.DependencyQuests.Contains(m.ID) && m.Status != QuestStatus.Completed) != null))
		{
			return;
		}
		QuestTrigger qt = quest.QuestTriggers.FirstOrDefault((QuestTrigger m) => m.ID == qtm.TriggerID);
		if (qt == null || qt.Status != QuestStatus.Active)
		{
			return;
		}
		qt.Status = QuestStatus.Completed;
		qt.UpdateDependentTriggers(quest);
		List<Task> tasks = new List<Task>();
		if (qt.Type == QuestTriggerType.Activate && quest.Status == QuestStatus.Inactive)
		{
			quest.UpdateActivation();
			if (quest.ActivationDependencyTpe == QuestTriggerDependencyTpe.Any)
			{
				foreach (QuestTrigger aqt in quest.QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Activate))
				{
					aqt.Status = QuestStatus.Completed;
				}
			}
		}
		else if (qt.Type == QuestTriggerType.Complete && !quest.IsFineshed)
		{
			quest.UpdateCompletion();
			if (quest.Status == QuestStatus.Completed)
			{
				foreach (Quest aaq in Quests.Where((Quest m) => m.AutoActivate && m.DependencyQuests.Contains(quest.ID) && m.Status == QuestStatus.Inactive))
				{
					List<Quest> depQuests = aaq.DependencyQuests.Select((uint m) => Quests.First((Quest n) => n.ID == m)).ToList();
					if (depQuests.Count() == depQuests.Count((Quest m) => m.Status == QuestStatus.Completed))
					{
						aaq.Status = QuestStatus.Active;
						foreach (QuestTrigger aaqt in aaq.QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Activate && m.Status != QuestStatus.Completed))
						{
							aaqt.Status = QuestStatus.Completed;
							aaqt.UpdateDependentTriggers(aaq);
						}
					}
					tasks.Add(new Task(delegate
					{
						Server.Instance.NetworkController.SendToGameClient(qtm.Sender, new QuestStatsMessage
						{
							QuestDetails = aaq.GetDetails()
						});
					}));
				}
			}
		}
		Server.Instance.NetworkController.SendToGameClient(qtm.Sender, new QuestStatsMessage
		{
			QuestDetails = quest.GetDetails()
		});
		foreach (Task t in tasks)
		{
			t.RunSynchronously();
		}
	}

	private void SkipQuestMessageListener(NetworkData data)
	{
		SkipQuestMessage sqm = data as SkipQuestMessage;
		if (sqm.Sender != GUID)
		{
			return;
		}
		Quest quest = Quests.FirstOrDefault((Quest m) => m.ID == sqm.QuestID);
		if (quest == null)
		{
			return;
		}
		quest.Status = QuestStatus.Completed;
		foreach (QuestTrigger qt in quest.QuestTriggers)
		{
			qt.Status = QuestStatus.Completed;
		}
		List<Task> tasks = new List<Task>();
		foreach (Quest aaq in Quests.Where((Quest m) => m.AutoActivate && m.DependencyQuests.Contains(quest.ID) && m.Status == QuestStatus.Inactive))
		{
			List<Quest> depQuests = aaq.DependencyQuests.Select((uint m) => Quests.First((Quest n) => n.ID == m)).ToList();
			if (depQuests.Count() == depQuests.Count((Quest m) => m.Status == QuestStatus.Completed))
			{
				aaq.Status = QuestStatus.Active;
				foreach (QuestTrigger aaqt in aaq.QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Activate && m.Status != QuestStatus.Completed))
				{
					aaqt.Status = QuestStatus.Completed;
					aaqt.UpdateDependentTriggers(aaq);
				}
			}
			tasks.Add(new Task(delegate
			{
				Server.Instance.NetworkController.SendToGameClient(sqm.Sender, new QuestStatsMessage
				{
					QuestDetails = aaq.GetDetails()
				});
			}));
		}
		Server.Instance.NetworkController.SendToGameClient(sqm.Sender, new QuestStatsMessage
		{
			QuestDetails = quest.GetDetails()
		});
		foreach (Task t in tasks)
		{
			t.RunSynchronously();
		}
	}

	private void LockToTriggerMessageListener(NetworkData data)
	{
		LockToTriggerMessage ltm = data as LockToTriggerMessage;
		if (ltm.Sender == GUID)
		{
			if (ltm.TriggerID == null)
			{
				LockedToTriggerID = null;
				IsPilotingVessel = ltm.IsPilotingVessel;
			}
			else if (Server.Instance.AllPlayers.FirstOrDefault((Player m) => m.GUID != GUID && m.LockedToTriggerID != null && m.LockedToTriggerID.Equals(ltm.TriggerID)) == null)
			{
				LockedToTriggerID = ltm.TriggerID;
				IsPilotingVessel = ltm.IsPilotingVessel;
				Server.Instance.NetworkController.SendToGameClient(data.Sender, ltm);
			}
		}
	}

	public void SetDockUndockCorrection(Vector3D? posCorrection, QuaternionD? rotCorrection)
	{
		dockUndockPositionCorrection = posCorrection;
		dockUndockRotationCorrection = rotCorrection;
		dockUndockWaitForMsg = posCorrection.HasValue && rotCorrection.HasValue;
	}

	public void ModifyLocalPositionAndRotation(Vector3D locPos, QuaternionD locRot)
	{
		LocalPosition += locPos;
		LocalRotation *= locRot;
		if (TransformData != null)
		{
			TransformData.LocalPosition = LocalPosition.ToFloatArray();
			TransformData.LocalRotation = LocalRotation.ToFloatArray();
		}
	}

	private void UpdateMovementListener(NetworkData data)
	{
		CharacterMovementMessage mm = data as CharacterMovementMessage;
		if (mm.Sender != GUID || !IsAlive || (Parent is Pivot && mm.ParentType == SpaceObjectType.None))
		{
			return;
		}
		MouseLook = mm.TransformData.MouseLook;
		FreeLookX = mm.TransformData.FreeLookX;
		FreeLookY = mm.TransformData.FreeLookY;
		CharacterAnimationData tmp = new CharacterAnimationData();
		tmp.VelocityForward = mm.AnimationData.VelocityForward;
		tmp.VelocityRight = mm.AnimationData.VelocityRight;
		tmp.ZeroGForward = mm.AnimationData.ZeroGForward;
		tmp.ZeroGRight = mm.AnimationData.ZeroGRight;
		tmp.PlayerStance = mm.AnimationData.PlayerStance;
		tmp.InteractType = mm.AnimationData.InteractType;
		tmp.TurningDirection = mm.AnimationData.TurningDirection;
		tmp.EquipOrDeEquip = mm.AnimationData.EquipOrDeEquip;
		tmp.EquipItemId = mm.AnimationData.EquipItemId;
		tmp.EmoteType = mm.AnimationData.EmoteType;
		tmp.ReloadItemType = mm.AnimationData.ReloadItemType;
		tmp.MeleeAttackType = mm.AnimationData.MeleeAttackType;
		tmp.LadderDirection = mm.AnimationData.LadderDirection;
		tmp.PlayerStanceFloat = mm.AnimationData.PlayerStanceFloat;
		tmp.GetUpType = mm.AnimationData.GetUpType;
		tmp.FireMode = mm.AnimationData.FireMode;
		tmp.AirTime = mm.AnimationData.AirTime;
		AnimationData = tmp;
		if (pivotPositionCorrection.IsNotEpsilonZero() && Parent is Pivot && mm.ParentType == SpaceObjectType.PlayerPivot && !mm.PivotReset)
		{
			return;
		}
		if (pivotPositionCorrection.IsNotEpsilonZero() && mm.ParentType == SpaceObjectType.PlayerPivot && mm.PivotReset)
		{
			pivotPositionCorrection = Vector3D.Zero;
			return;
		}
		TransformData = mm.TransformData;
		LocalPosition = mm.TransformData.LocalPosition.ToVector3D();
		if (pivotPositionCorrection.IsNotEpsilonZero() && Parent is Pivot && !mm.PivotReset)
		{
			LocalPosition -= pivotPositionCorrection;
			TransformData.LocalPosition = LocalPosition.ToFloatArray();
		}
		LocalRotation = mm.TransformData.LocalRotation.ToQuaternionD();
		if (mm.DockUndockMsg.HasValue && dockUndockWaitForMsg)
		{
			SetDockUndockCorrection(null, null);
		}
		if (dockUndockPositionCorrection.HasValue && dockUndockRotationCorrection.HasValue)
		{
			LocalPosition += dockUndockPositionCorrection.Value;
			LocalRotation *= dockUndockRotationCorrection.Value;
			TransformData.LocalPosition = LocalPosition.ToFloatArray();
			TransformData.LocalRotation = LocalRotation.ToFloatArray();
		}
		LocalVelocity = mm.TransformData.LocalVelocity.ToVector3D();
		gravity = mm.Gravity;
		if (mm.ImpactVelocity.HasValue)
		{
			Stats.DoCollisionDamage(mm.ImpactVelocity.Value);
			collisionImpactVelocity = mm.ImpactVelocity.Value;
		}
		else
		{
			collisionImpactVelocity = 0f;
		}
		if (mm.RagdollData != null)
		{
			RagdollData = new Dictionary<byte, RagdollItemData>(mm.RagdollData);
		}
		else if (RagdollData != null)
		{
			RagdollData.Clear();
			RagdollData = null;
		}
		if (mm.JetpackDirection != null)
		{
			jetpackDirection = new sbyte[4]
			{
				mm.JetpackDirection[0],
				mm.JetpackDirection[1],
				mm.JetpackDirection[2],
				mm.JetpackDirection[3]
			};
		}
		else if (jetpackDirection != null)
		{
			jetpackDirection = null;
		}
		if (Parent is SpaceObjectVessel && mm.ParentType == SpaceObjectType.PlayerPivot)
		{
			pivotPositionCorrection = Vector3D.Zero;
			pivotVelocityCorrection = Vector3D.Zero;
			LocalPosition = Vector3D.Zero;
			SpaceObjectVessel refVessel2 = (Parent as SpaceObjectVessel).MainVessel;
			Pivot pivot2 = new Pivot(this, refVessel2);
			pivot2.Orbit.CopyDataFrom(refVessel2.Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
			pivot2.Orbit.SetLastChangeTime(Server.SolarSystemTime);
			SubscribeTo(Parent);
			foreach (Player pl in Server.Instance.AllPlayers)
			{
				if (pl.IsSubscribedTo(Parent.GUID))
				{
					pl.SubscribeTo(pivot2);
				}
			}
			Parent = pivot2;
		}
		else if (Parent is SpaceObjectVessel && Parent.GUID != mm.ParentGUID)
		{
			SpaceObjectVessel parVessel = Parent as SpaceObjectVessel;
			if ((mm.ParentType == SpaceObjectType.Ship || mm.ParentType == SpaceObjectType.Asteroid || mm.ParentType == SpaceObjectType.Station) && Server.Instance.DoesObjectExist(mm.ParentGUID))
			{
				Parent = Server.Instance.GetVessel(mm.ParentGUID);
			}
			else
			{
				Dbg.Error("Unable to find new parent", GUID, Name, "new parent", mm.ParentType, mm.ParentGUID);
			}
		}
		else if (Parent is Pivot && mm.ParentType != SpaceObjectType.PlayerPivot)
		{
			Pivot pivot3 = Parent as Pivot;
			if ((mm.ParentType == SpaceObjectType.Ship || mm.ParentType == SpaceObjectType.Asteroid || mm.ParentType == SpaceObjectType.Station) && Server.Instance.DoesObjectExist(mm.ParentGUID))
			{
				Parent = Server.Instance.GetVessel(mm.ParentGUID);
				SubscribeTo(Parent);
				pivot3.Destroy();
			}
			else
			{
				Dbg.Error("Unable to find new parent", GUID, Name, "new parent", mm.ParentType, mm.ParentGUID);
			}
		}
		else if (Parent is Pivot && pivotPositionCorrection.IsEpsilonZero() && Server.Instance.RunTime.TotalSeconds - lastPivotResetTime > 1.0)
		{
			Pivot pivot = Parent as Pivot;
			SpaceObjectVessel nearestVessel = ((mm.NearestVesselGUID > 0) ? Server.Instance.GetVessel(mm.NearestVesselGUID) : null);
			SpaceObjectVessel refVessel = nearestVessel.MainVessel;
			if (refVessel.StabilizeToTargetObj != null)
			{
				refVessel = refVessel.StabilizeToTargetObj;
			}
			Vector3D playerGlobalPos = pivot.Position + LocalPosition;
			if (nearestVessel != null && !nearestVessel.IsDebrisFragment && (pivot.Position - refVessel.Position).IsNotEpsilonZero() && (mm.StickToVessel || mm.NearestVesselDistance <= 50f))
			{
				Vector3D oldPivotPos = pivot.Position;
				Vector3D oldPivotVel = pivot.Velocity;
				pivot.Orbit.CopyDataFrom(refVessel.Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
				pivot.Orbit.SetLastChangeTime(Server.SolarSystemTime);
				pivotPositionCorrection += pivot.Position - oldPivotPos;
				pivotVelocityCorrection = pivot.Velocity - oldPivotVel;
				UpdateArtificialBodyMovement.Add(pivot.GUID);
				UpdateArtificialBodyMovement.Add(refVessel.GUID);
			}
			else if ((nearestVessel == null || (mm.NearestVesselDistance > 50f && (nearestVessel.FTL == null || nearestVessel.FTL.Status != SystemStatus.OnLine || (nearestVessel.Velocity - Parent.Velocity).SqrMagnitude < 900.0))) && LocalPosition.SqrMagnitude > 25000000.0 && !mm.StickToVessel)
			{
				pivotPositionCorrection = LocalPosition;
				pivot.AdjustPositionAndVelocity(pivotPositionCorrection, pivotVelocityCorrection);
				UpdateArtificialBodyMovement.Add(pivot.GUID);
			}
			if (pivotPositionCorrection.IsNotEpsilonZero())
			{
				lastPivotResetTime = Server.Instance.RunTime.TotalSeconds;
				LocalPosition -= pivotPositionCorrection;
				TransformData.LocalPosition = (TransformData.LocalPosition.ToVector3D() - pivotPositionCorrection).ToFloatArray();
			}
		}
		PlayerReady = true;
	}

	public override void UpdateTimers(double deltaTime)
	{
		if (!IsAlive)
		{
			return;
		}
		updateTemperature(deltaTime);
		if (CoreTemperature < 20f || CoreTemperature > 45f)
		{
		}
		float suffocateDamage = 0f;
		float pressureDamage = 0f;
		float exposureDamage = ((!(Parent is Ship)) ? (StaticData.GetPlayerExposureDamage(Parent.Position.Magnitude) * (float)deltaTime) : 0f);
		if (!IsInsideSpawnPoint)
		{
			if (CurrentHelmet != null && (!CurrentHelmet.IsVisorToggleable || CurrentHelmet.IsVisorActive))
			{
				if (CurrentJetpack != null && CurrentJetpack.HasOxygen)
				{
					CurrentJetpack.ConsumeResources(null, CurrentJetpack.OxygenConsumption * (float)deltaTime);
				}
				else
				{
					suffocateDamage = 1f * (float)deltaTime;
				}
			}
			else if (CurrentRoom == null)
			{
				suffocateDamage = 1f * (float)deltaTime;
			}
			else if (CurrentRoom != null && CurrentRoom.Breathability < 1f)
			{
				suffocateDamage = 1f * (1f - CurrentRoom.Breathability) * (float)deltaTime;
			}
		}
		if ((CurrentRoom == null || CurrentRoom.AirPressure < 0.3f) && (PlayerInventory.CurrOutfit == null || CurrentHelmet == null || (CurrentHelmet.IsVisorToggleable && !CurrentHelmet.IsVisorActive)))
		{
			pressureDamage = 2f * (float)deltaTime;
		}
		if (!Initialize && (suffocateDamage > float.Epsilon || pressureDamage > float.Epsilon || exposureDamage > float.Epsilon))
		{
			Stats.TakeDamage((float)deltaTime, new PlayerDamage
			{
				HurtType = HurtType.Suffocate,
				Amount = suffocateDamage
			}, new PlayerDamage
			{
				HurtType = HurtType.Suffocate,
				Amount = pressureDamage
			}, new PlayerDamage
			{
				HurtType = HurtType.SpaceExposure,
				Amount = exposureDamage
			});
		}
		if (CurrentJetpack != null && jetpackDirection != null && (jetpackDirection[0] != 0 || jetpackDirection[1] != 0 || jetpackDirection[2] != 0 || jetpackDirection[3] != 0))
		{
			CurrentJetpack.ConsumeResources(CurrentJetpack.PropellantConsumption * (float)deltaTime);
		}
		if (CurrentHelmet == null && CurrentJetpack == null && ItemInHands == null)
		{
			return;
		}
		updateItemTimer += deltaTime;
		if (!(updateItemTimer >= 0.30000001192092896))
		{
			return;
		}
		DynamicObjectsInfoMessage doim = new DynamicObjectsInfoMessage();
		doim.Infos = new List<DynamicObjectInfo>();
		if (CurrentHelmet != null && CurrentHelmet.DynamicObj.StatsChanged)
		{
			doim.Infos.Add(new DynamicObjectInfo
			{
				GUID = CurrentHelmet.GUID,
				Stats = CurrentHelmet.StatsNew
			});
			CurrentHelmet.DynamicObj.StatsChanged = false;
		}
		if (CurrentJetpack != null && CurrentJetpack.DynamicObj.StatsChanged)
		{
			doim.Infos.Add(new DynamicObjectInfo
			{
				GUID = CurrentJetpack.GUID,
				Stats = CurrentJetpack.StatsNew
			});
			CurrentJetpack.DynamicObj.StatsChanged = false;
		}
		if (ItemInHands != null)
		{
			ItemInHands.SendAllStats();
		}
		if (!(ItemInHands is HandDrill) && ItemInHands is Weapon)
		{
			Weapon wep = ItemInHands as Weapon;
			if (wep.DynamicObj.StatsChanged)
			{
				doim.Infos.Add(new DynamicObjectInfo
				{
					GUID = ItemInHands.GUID,
					Stats = wep.StatsNew
				});
				wep.DynamicObj.StatsChanged = false;
			}
			if (wep.Magazine != null && wep.Magazine.DynamicObj.StatsChanged)
			{
				doim.Infos.Add(new DynamicObjectInfo
				{
					GUID = wep.Magazine.GUID,
					Stats = wep.Magazine.StatsNew
				});
				wep.Magazine.DynamicObj.StatsChanged = false;
			}
		}
		if (doim.Infos.Count > 0)
		{
			Server.Instance.NetworkController.SendToClientsSubscribedTo(doim, -1L, Parent);
		}
		updateItemTimer = 0.0;
	}

	public void SubscribeTo(SpaceObject spaceObject)
	{
		subscribedToSpaceObjects.Add(spaceObject.GUID);
		if (!(spaceObject is SpaceObjectVessel))
		{
			return;
		}
		SpaceObjectVessel ves = spaceObject as SpaceObjectVessel;
		if (ves.IsDocked)
		{
			subscribedToSpaceObjects.Add(ves.DockedToMainVessel.GUID);
			{
				foreach (SpaceObjectVessel obj2 in ves.DockedToMainVessel.AllDockedVessels)
				{
					subscribedToSpaceObjects.Add(obj2.GUID);
				}
				return;
			}
		}
		if (ves.AllDockedVessels == null || ves.AllDockedVessels.Count <= 0)
		{
			return;
		}
		foreach (SpaceObjectVessel obj in ves.AllDockedVessels)
		{
			subscribedToSpaceObjects.Add(obj.GUID);
		}
	}

	public void UnsubscribeFrom(SpaceObject spaceObject)
	{
		subscribedToSpaceObjects.Remove(spaceObject.GUID);
	}

	public void UnsubscribeFromAll()
	{
		subscribedToSpaceObjects.Clear();
	}

	public bool IsSubscribedTo(SpaceObject spaceObject, bool checkParent)
	{
		if (!checkParent)
		{
			return subscribedToSpaceObjects.Contains(spaceObject.GUID);
		}
		return subscribedToSpaceObjects.Contains(spaceObject.GUID) || (spaceObject.Parent != null && subscribedToSpaceObjects.Contains(spaceObject.Parent.GUID));
	}

	public bool IsSubscribedTo(long guid)
	{
		return subscribedToSpaceObjects.Contains(guid);
	}

	public void PlayerRoomMessageListener(NetworkData data)
	{
		if (data.Sender != GUID)
		{
			return;
		}
		PlayerRoomMessage prm = (PlayerRoomMessage)data;
		isOutsideRoom = prm.IsOutsideRoom.HasValue && prm.IsOutsideRoom.Value;
		Room newRoom = null;
		if (prm.ID != null)
		{
			SpaceObjectVessel newRoomVessel = Server.Instance.GetVessel(prm.ID.VesselGUID);
			if (newRoomVessel != null)
			{
				newRoom = newRoomVessel.Rooms.FirstOrDefault((Room m) => m.ID.Equals(prm.ID));
			}
		}
		if (CurrentRoom != null)
		{
			CurrentRoom.RemoveAirConsumer(this);
		}
		newRoom?.AddAirConsumer(this);
		CurrentRoom = newRoom;
	}

	public CharacterMovementMessage GetCharacterMovementMessage()
	{
		if (TransformData == null)
		{
			return null;
		}
		CharacterMovementMessage mm = new CharacterMovementMessage();
		mm.GUID = FakeGuid;
		if (Parent != null)
		{
			mm.ParentGUID = Parent.GUID;
			mm.ParentType = Parent.ObjectType;
		}
		else
		{
			mm.ParentGUID = -1L;
			mm.ParentType = SpaceObjectType.None;
		}
		mm.TransformData = TransformData;
		mm.Gravity = gravity;
		mm.AnimationData = new CharacterAnimationData();
		mm.AnimationData.VelocityForward = AnimationData.VelocityForward;
		mm.AnimationData.VelocityRight = AnimationData.VelocityRight;
		mm.AnimationData.ZeroGForward = AnimationData.ZeroGForward;
		mm.AnimationData.ZeroGRight = AnimationData.ZeroGRight;
		mm.AnimationData.PlayerStance = AnimationData.PlayerStance;
		mm.AnimationData.InteractType = AnimationData.InteractType;
		mm.AnimationData.TurningDirection = AnimationData.TurningDirection;
		mm.AnimationData.EquipOrDeEquip = AnimationData.EquipOrDeEquip;
		mm.AnimationData.EquipItemId = AnimationData.EquipItemId;
		mm.AnimationData.EmoteType = AnimationData.EmoteType;
		mm.AnimationData.ReloadItemType = AnimationData.ReloadItemType;
		mm.AnimationData.MeleeAttackType = AnimationData.MeleeAttackType;
		mm.AnimationData.LadderDirection = AnimationData.LadderDirection;
		mm.AnimationData.PlayerStanceFloat = AnimationData.PlayerStanceFloat;
		mm.AnimationData.GetUpType = AnimationData.GetUpType;
		mm.AnimationData.FireMode = AnimationData.FireMode;
		mm.AnimationData.AirTime = AnimationData.AirTime;
		if (RagdollData != null && RagdollData.Count > 0)
		{
			mm.RagdollData = new Dictionary<byte, RagdollItemData>(RagdollData);
		}
		if (jetpackDirection != null)
		{
			mm.JetpackDirection = new sbyte[4]
			{
				jetpackDirection[0],
				jetpackDirection[1],
				jetpackDirection[2],
				jetpackDirection[3]
			};
		}
		mm.PivotReset = pivotPositionCorrection.IsNotEpsilonZero();
		mm.PivotPositionCorrection = pivotPositionCorrection.ToFloatArray();
		mm.PivotVelocityCorrection = pivotVelocityCorrection.ToFloatArray();
		if (collisionImpactVelocity > 0f)
		{
			mm.ImpactVelocity = collisionImpactVelocity;
		}
		return mm;
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		return new SpawnCharacterResponseData
		{
			GUID = GUID,
			Details = GetDetails(checkAlive: true)
		};
	}

	public CharacterDetails GetDetails(bool checkAlive = false)
	{
		List<DynamicObjectDetails> dods = new List<DynamicObjectDetails>();
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			dods.Add(dobj.GetDetails());
		}
		CharacterDetails details = new CharacterDetails
		{
			GUID = FakeGuid,
			Name = Name,
			Gender = Gender,
			HeadType = HeadType,
			HairType = HairType,
			SteamId = SteamId,
			ParentID = ((Parent != null) ? Parent.GUID : (-1)),
			ParentType = ((Parent != null) ? Parent.ObjectType : SpaceObjectType.None),
			DynamicObjects = dods,
			AnimationStatsMask = AnimationStatsMask,
			LockedToTriggerID = LockedToTriggerID
		};
		if (IsAlive || !checkAlive || CurrentSpawnPoint == null)
		{
			details.TransformData = new CharacterTransformData();
			details.TransformData.LocalPosition = LocalPosition.ToFloatArray();
			details.TransformData.LocalRotation = LocalRotation.ToFloatArray();
			details.TransformData.MouseLook = MouseLook;
			details.TransformData.FreeLookX = FreeLookX;
			details.TransformData.FreeLookY = FreeLookY;
		}
		else
		{
			details.SpawnPointID = CurrentSpawnPoint.SpawnPointID;
		}
		return details;
	}

	public override void Destroy()
	{
		DiconnectFromNetworkContoller();
		while (DynamicObjects.Count > 0)
		{
			DynamicObjects.First().Value.DestroyDynamicObject();
		}
		foreach (SpaceObjectVessel ves in Server.Instance.AllVessels)
		{
			if (ves is Ship)
			{
				(ves as Ship).ResetSpawnPointsForPlayer(this, sendStatsMessage: true);
			}
		}
		Server.Instance.Remove(this);
		base.Destroy();
	}

	private void updateTemperature(double deltaTime)
	{
		updateOutfitTemperature(deltaTime);
		if (AmbientTemperature.HasValue)
		{
			CoreTemperature += (float)((double?)(AmbientTemperature - CoreTemperature) * 0.01 * deltaTime).Value;
		}
		else
		{
			CoreTemperature = SpaceExposureTemperature(CoreTemperature, 10000f, 20f, 80f, deltaTime);
		}
	}

	private void updateOutfitTemperature(double deltaTime)
	{
		Outfit outfit = PlayerInventory.CurrOutfit;
		if (outfit != null)
		{
			if (Parent is Pivot)
			{
				outfit.ExternalTemperature = SpaceExposureTemperature(outfit.ExternalTemperature, 10000f, 20f, 80f, deltaTime);
			}
			else if (Parent is SpaceObjectVessel)
			{
				outfit.ExternalTemperature += (float)((double)((Parent as SpaceObjectVessel).Temperature - outfit.ExternalTemperature) * 0.001 * deltaTime);
			}
			float outfitInsulationFactor = 0.1f;
			outfit.InternalTemperature += (float)((double)(outfit.ExternalTemperature - outfit.InternalTemperature) * 0.1 * deltaTime * (double)outfitInsulationFactor);
			if (CurrentHelmet != null && CurrentHelmet.IsVisorActive && CurrentJetpack != null)
			{
				float outfitTempRegulation = 5f;
				float tempCorr = (float)MathHelper.Clamp(37f - outfit.InternalTemperature, (double)(0f - outfitTempRegulation) * deltaTime, (double)outfitTempRegulation * deltaTime);
				outfit.InternalTemperature += tempCorr;
				outfitTempRegulationActive = System.Math.Abs((double)tempCorr / deltaTime) > 0.5;
			}
		}
	}

	public void KillYourself(HurtType causeOfdeath, bool createCorpse = true)
	{
		IsAlive = false;
		Corpse corpse = null;
		if (createCorpse)
		{
			corpse = new Corpse(this);
		}
		else
		{
			while (DynamicObjects.Count > 0)
			{
				DynamicObjects.First().Value.DestroyDynamicObject();
			}
		}
		PlayerInventory = new Inventory(this);
		CurrentJetpack = null;
		CurrentHelmet = null;
		if (DynamicObjects.Count > 0)
		{
			string error = "Player had some dynamic objects that are not moved to corpse:";
			foreach (DynamicObject dobj in DynamicObjects.Values)
			{
				error = error + " " + dobj.GUID + ",";
			}
			DynamicObjects.Clear();
		}
		UnsubscribeFromAll();
		Health = 100;
		VesselDamageType vesselDamageType = VesselDamageType.None;
		if (Parent is Ship)
		{
			Ship ship = Parent as Ship;
			if (ship.Health <= 0f)
			{
				vesselDamageType = ship.LastVesselDamageType;
			}
			ship.RemovePlayerFromRoom(this);
			ship.RemovePlayerFromCrew(this, checkDetails: true);
		}
		else if (Parent is Pivot)
		{
			(Parent as Pivot).Destroy();
		}
		if (CurrentSpawnPoint != null && CurrentSpawnPoint.Type == SpawnPointType.SimpleSpawn)
		{
			CurrentSpawnPoint.Player = null;
			CurrentSpawnPoint.IsPlayerInSpawnPoint = false;
			CurrentSpawnPoint = null;
		}
		if (RagdollData != null)
		{
			RagdollData.Clear();
			RagdollData = null;
		}
		Server.Instance.NetworkController.SendToClientsSubscribedToParents(new KillPlayerMessage
		{
			GUID = FakeGuid,
			CauseOfDeath = causeOfdeath,
			VesselDamageType = vesselDamageType,
			CorpseDetails = corpse?.GetDetails()
		}, this, GUID);
		Parent = null;
		CurrentRoom = null;
		isOutsideRoom = false;
		if (Server.Instance.NetworkController.ClientList.ContainsKey(GUID))
		{
			Server.Instance.NetworkController.LogOutPlayer(GUID);
			Server.Instance.NetworkController.SendToGameClient(GUID, new KillPlayerMessage
			{
				GUID = FakeGuid,
				CauseOfDeath = causeOfdeath,
				VesselDamageType = vesselDamageType
			});
			lateDisconnectWait = 0.0;
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_0_1_sec, LateDisconnect);
		}
		foreach (Quest q in Quests)
		{
			if (q.Status != QuestStatus.Active)
			{
				continue;
			}
			q.Status = QuestStatus.Inactive;
			foreach (QuestTrigger qt in q.QuestTriggers)
			{
				if (qt.Type == QuestTriggerType.Activate)
				{
					qt.Status = QuestStatus.Active;
					continue;
				}
				qt.Status = QuestStatus.Inactive;
				if (qt.SpawnRuleName == null || !(qt.SpawnRuleName != ""))
				{
					continue;
				}
				QuestTrigger.QuestTriggerID qtid = qt.GetQuestTriggerID();
				foreach (SpaceObjectVessel vessel in Server.Instance.AllVessels)
				{
					if (vessel.QuestTriggerID == qtid)
					{
						vessel.SelfDestructTimer = new SelfDestructTimer(vessel, 1f)
						{
							CheckPlayersDistance = 1000.0
						};
						vessel.AuthorizedPersonel.RemoveAll((AuthorizedPerson m) => m.PlayerGUID == GUID);
					}
				}
			}
		}
	}

	public void LateDisconnect(double dbl)
	{
		lateDisconnectWait += dbl;
		if (lateDisconnectWait > 1.0)
		{
			Server.Instance.NetworkController.DisconnectClient(GUID);
			Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_0_1_sec, LateDisconnect);
			lateDisconnectWait = 0.0;
		}
	}

	public void LogoutDisconnectReset()
	{
		if (Parent is Ship)
		{
			(Parent as Ship).RemovePlayerFromExecuters(this);
		}
		UnsubscribeFromAll();
		EnvironmentReady = false;
		PlayerReady = false;
		LastMovementMessageSolarSystemTime = -1.0;
		MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		try
		{
			if (Parent != null && Parent is Ship && isOutsideRoom)
			{
				Pivot pivot = new Pivot(this, Parent as SpaceObjectVessel);
				pivot.Orbit.CopyDataFrom((Parent as Ship).Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
				pivot.Orbit.RelativePosition += LocalPosition;
				pivot.Orbit.InitFromCurrentStateVectors(Server.Instance.SolarSystem.CurrentTime);
				pivot.StabilizeToTarget(Parent as Ship, forceStabilize: true);
				LocalPosition = Vector3D.Zero;
				Parent = pivot;
			}
		}
		catch (Exception)
		{
		}
	}

	public PersistenceObjectData GetPersistenceData()
	{
		PersistenceObjectDataPlayer data = new PersistenceObjectDataPlayer();
		data.GUID = GUID;
		data.FakeGUID = FakeGuid;
		if (Parent != null)
		{
			data.ParentGUID = Parent.GUID;
			data.ParentType = Parent.ObjectType;
			if (Parent.ObjectType == SpaceObjectType.PlayerPivot)
			{
				data.ParentPosition = Parent.Position.ToArray();
				data.ParentVelocity = Parent.Velocity.ToArray();
			}
		}
		else
		{
			data.ParentGUID = -1L;
			data.ParentType = SpaceObjectType.None;
		}
		data.LocalPosition = LocalPosition.ToArray();
		data.LocalRotation = LocalRotation.ToArray();
		data.IsAlive = IsAlive;
		data.Name = Name;
		data.SteamId = SteamId;
		data.Gender = Gender;
		data.HeadType = HeadType;
		data.HairType = HairType;
		data.HealthPoints = Stats.HealthPoints;
		data.MaxHealthPoints = Stats.MaxHealthPoints;
		data.AnimationData = ObjectCopier.DeepCopy(AnimationData);
		data.AnimationStatsMask = AnimationStatsMask;
		data.Gravity = gravity;
		data.Velocity = LocalVelocity.ToArray();
		if (CurrentRoom != null)
		{
			data.CurrentRoomID = CurrentRoom.ID.InSceneID;
		}
		data.CoreTemperature = CoreTemperature;
		data.ChildObjects = new List<PersistenceObjectData>();
		DynamicObject outfitItem = DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item != null && m.Item.Slot != null && m.Item.Slot.SlotID == -2);
		if (outfitItem != null)
		{
			data.ChildObjects.Add((outfitItem.Item != null) ? outfitItem.Item.GetPersistenceData() : outfitItem.GetPersistenceData());
		}
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			if (dobj != outfitItem)
			{
				data.ChildObjects.Add((dobj.Item != null) ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
			}
		}
		data.Quests = Quests.Select((Quest m) => m.GetDetails()).ToList();
		data.Blueprints = Blueprints;
		data.NavMapDetails = NavMapDetails;
		return data;
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			PersistenceObjectDataPlayer data = persistenceData as PersistenceObjectDataPlayer;
			if (data == null)
			{
				Dbg.Warning("PersistenceObjectDataPlayer data is null", GUID);
				return;
			}
			GUID = data.GUID;
			FakeGuid = data.FakeGUID;
			LocalPosition = data.LocalPosition.ToVector3D();
			LocalRotation = data.LocalRotation.ToQuaternionD();
			IsAlive = data.IsAlive;
			Name = data.Name;
			SteamId = data.SteamId;
			Gender = data.Gender;
			HeadType = data.HeadType;
			HairType = data.HairType;
			Stats.MaxHealthPoints = data.MaxHealthPoints;
			Stats.HealthPoints = data.HealthPoints;
			AnimationData = ObjectCopier.DeepCopy(data.AnimationData);
			AnimationStatsMask = data.AnimationStatsMask;
			gravity = data.Gravity;
			LocalVelocity = data.Velocity.ToVector3D();
			CoreTemperature = data.CoreTemperature;
			SpaceObject papa = null;
			if (data.ParentType == SpaceObjectType.PlayerPivot)
			{
				papa = new Pivot(this, data.ParentPosition.ToVector3D(), data.ParentVelocity.ToVector3D());
			}
			else if (data.ParentGUID != -1)
			{
				papa = Server.Instance.GetObject(data.ParentGUID);
			}
			if (papa != null)
			{
				Parent = papa;
				if (data.CurrentRoomID.HasValue && Parent is SpaceObjectVessel)
				{
					CurrentRoom = (Parent as SpaceObjectVessel).Rooms.FirstOrDefault((Room m) => m.ID.InSceneID == data.CurrentRoomID.Value);
				}
			}
			else
			{
				if (data.ParentGUID != -1 && papa == null)
				{
					Dbg.Error("Player papa object not found, SAVE MIGHT BE CORRUPTED", GUID, data.ParentGUID, data.ParentType);
					return;
				}
				Parent = null;
				KillYourself(HurtType.None, createCorpse: false);
			}
			if (Parent != null)
			{
				foreach (PersistenceObjectDataDynamicObject dobjData in data.ChildObjects)
				{
					Persistence.CreateDynamicObject(dobjData, this);
				}
			}
			if (data.Quests != null)
			{
				foreach (QuestDetails det in data.Quests)
				{
					Quest quest = Quests.FirstOrDefault((Quest m) => m.ID == det.ID);
					if (quest == null)
					{
						continue;
					}
					quest.Status = det.Status;
					foreach (QuestTriggerDetails qtDet in det.QuestTriggers)
					{
						QuestTrigger questTrigger = quest.QuestTriggers.FirstOrDefault((QuestTrigger m) => m.ID == qtDet.ID);
						if (questTrigger != null)
						{
							questTrigger.Status = qtDet.Status;
						}
					}
				}
			}
			if (data.Blueprints != null)
			{
				Blueprints = data.Blueprints;
			}
			NavMapDetails = data.NavMapDetails;
			Server.Instance.Add(this);
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}

	public void SetSpawnPoint(ShipSpawnPoint spawnPoint)
	{
		if (spawnPoint != null && spawnPoint.Type == SpawnPointType.WithAuthorization)
		{
			AuthorizedSpawnPoint = spawnPoint;
		}
		CurrentSpawnPoint = spawnPoint;
	}

	public void ClearAuthorizedSpawnPoint()
	{
		AuthorizedSpawnPoint = null;
	}

	public CharacterData GetCharacterData()
	{
		return new CharacterData
		{
			Name = Name,
			Gender = Gender,
			HeadType = HeadType,
			HairType = HairType
		};
	}

	private void NavigationMapDetailsMessageListener(NetworkData data)
	{
		if (data.Sender == GUID)
		{
			NavigationMapDetailsMessage nmdl = data as NavigationMapDetailsMessage;
			NavMapDetails = nmdl.NavMapDetails;
		}
	}
}
