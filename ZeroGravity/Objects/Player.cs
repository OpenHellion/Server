using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using OpenHellion.Net;
using ZeroGravity.Data;
using ZeroGravity.Math;
using ZeroGravity.Network;
using ZeroGravity.ShipComponents;

namespace ZeroGravity.Objects;

public class Player : SpaceObjectTransferable, IPersistantObject, IAirConsumer
{
	public enum HitBoxType
	{
		None = -1,
		Head,
		Torso,
		Arms,
		Legs,
		Abdomen
	}

	public float MaxHealth = 100f;

	private double _lastMeleeTime;

	private float _acummulatedDamage;

	private readonly Timer _healTimer;

	private float _amountToHeal;

	private float _amountToHealStep;

	public double LastMovementMessageSolarSystemTime = -1.0;

	public List<long> UpdateArtificialBodyMovement = new List<long>();

	public bool IsAlive;

	private bool _environmentReady;

	private bool _playerReady;

	public string Name;

	public string PlayerId;

	public Gender Gender;

	public byte HeadType;

	public byte HairType;

	public float MouseLook;

	public float FreeLookX;

	public float FreeLookY;

	public long FakeGuid;

	public CharacterAnimationData AnimationData;

	public int AnimationStatsMask;

	private readonly HashSet<long> _subscribedToSpaceObjects = [];

	private float[] _gravity;

	public Vector3D LocalVelocity = Vector3D.Zero;

	private float _collisionImpactVelocity;

	private Helmet _currentHelmet;

	private Jetpack _currentJetpack;

	public Dictionary<byte, RagdollItemData> RagdollData;

	private sbyte[] _jetpackDirection;

	private Vector3D _pivotPositionCorrection = Vector3D.Zero;

	private Vector3D _pivotVelocityCorrection = Vector3D.Zero;

	private Vector3D? _dockUndockPositionCorrection;

	private QuaternionD? _dockUndockRotationCorrection;

	private bool _dockUndockWaitForMsg;

	public bool IsAdmin = false;

	private SpaceObject _parent;

	public Room CurrentRoom;

	private bool isOutsideRoom;

	public Inventory PlayerInventory;

	public float CoreTemperature = 37f;

	private double _lastPivotResetTime;

	private double _lateDisconnectWait;

	public bool IsInsideSpawnPoint;

	public ConcurrentQueue<ShipStatsMessage> MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();

	private PlayerStatsMessage lastPlayerStatsMessage;

	public VesselObjectID LockedToTriggerID;

	public List<Quest> Quests;

	public List<ItemCompoundType> Blueprints = ObjectCopier.DeepCopy(StaticData.DefaultBlueprints);

	public NavigationMapDetails NavMapDetails;

	public bool Initialize = true;

	public override SpaceObjectType ObjectType => SpaceObjectType.Player;

	public bool EnvironmentReady
	{
		get => _environmentReady;
		private set
		{
			if (_environmentReady != value)
			{
				_environmentReady = value;
				if (PlayerReady && _environmentReady)
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
			return _playerReady;
		}
		private set
		{
			if (_playerReady = value)
			{
				_playerReady = value;
				if (PlayerReady && EnvironmentReady)
				{
					Initialize = false;
				}
			}
		}
	}

	public ShipSpawnPoint CurrentSpawnPoint { get; private set; }

	public ShipSpawnPoint AuthorizedSpawnPoint { get; private set; }

	public Helmet CurrentHelmet
	{
		get
		{
			return _currentHelmet;
		}
		set
		{
			_currentHelmet = value;
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
			return _currentJetpack;
		}
		set
		{
			_currentJetpack = value;
			if (value == null && CurrentHelmet != null)
			{
				CurrentHelmet.Jetpack = value;
			}
		}
	}

	public Item ItemInHands => PlayerInventory.HandsSlot.Item;

	public float Health { get; private set; }

	public override SpaceObject Parent
	{
		get
		{
			return _parent;
		}
		set
		{
			if (_parent is SpaceObjectVessel vessel)
			{
				vessel.RemovePlayerFromCrew(this);
			}
			_parent = value;
			if (_parent is SpaceObjectVessel objectVessel)
			{
				objectVessel.AddPlayerToCrew(this);
			}
		}
	}

	public bool GodMode { get; set; }

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

	public Player(long guid, Vector3D localPosition, QuaternionD localRotation)
		: base(guid, localPosition, localRotation)
	{
		_healTimer = new Timer(100.0)
		{
			Enabled = false
		};
		_healTimer.Elapsed += async delegate
		{
			await HealOverTimeStep();
		};
	}

	public static async Task<Player> CreatePlayerAsync(long guid, Vector3D localPosition, QuaternionD localRotation, string name, string playerId, Gender gender, byte headType, byte hairType, bool addToServerList = true, Player clone = null)
	{
		var player = new Player(guid, localPosition, localRotation)
		{
			FakeGuid = GUIDFactory.NextPlayerFakeGUID(),
			Name = name,
			PlayerId = playerId,
			Gender = gender,
			HeadType = headType,
			HairType = hairType,
		};
		player.PlayerInventory = new Inventory(player);
		player.Quests = await Quest.CreateQuestsAsync(StaticData.QuestsData, player);
		if (addToServerList)
		{
			Server.Instance.Add(player);
		}
		if (clone == null)
		{
			return player;
		}
		if (clone.PlayerInventory.OutfitSlot.Item != null)
		{
			await player.PlayerInventory.AddItemToInventory(await clone.PlayerInventory.OutfitSlot.Item.GetCopy(), clone.PlayerInventory.OutfitSlot.SlotID);
			foreach (InventorySlot sl in clone.PlayerInventory.CurrOutfit.InventorySlots.Values.Where((InventorySlot m) => m.Item != null))
			{
				await player.PlayerInventory.AddItemToInventory(await clone.PlayerInventory.CurrOutfit.InventorySlots[sl.SlotID].Item.GetCopy(), sl.SlotID);
			}
		}
		if (clone.PlayerInventory.HandsSlot.Item != null)
		{
			await player.PlayerInventory.AddItemToInventory(await clone.PlayerInventory.HandsSlot.Item.GetCopy(), clone.PlayerInventory.HandsSlot.SlotID);
		}

		return player;
	}

	public void ConnectToNetworkController()
	{
		EnvironmentReady = false;
		PlayerReady = false;
		EventSystem.AddListener<CharacterMovementMessage>(UpdateMovementListener);
		EventSystem.AddListener<EnvironmentReadyMessage>(EnvironmentReadyListener);
		EventSystem.AddListener<PlayerShootingMessage>(PlayerShootingListener);
		EventSystem.AddListener<PlayerStatsMessage>(PlayerStatsMessageListener);
		EventSystem.AddListener<PlayerDrillingMessage>(PlayerDrillingListener);
		EventSystem.AddListener<PlayerRoomMessage>(PlayerRoomMessageListener);
		EventSystem.AddListener<SuicideRequest>(SuicideListener);
		EventSystem.AddListener<AuthorizedVesselsRequest>(AuthorizedVesselsRequestListener);
		EventSystem.AddListener<LockToTriggerMessage>(LockToTriggerMessageListener);
		EventSystem.AddListener<QuestTriggerMessage>(QuestTriggerMessageListener);
		EventSystem.AddListener<SkipQuestMessage>(SkipQuestMessageListener);
		EventSystem.AddListener<NavigationMapDetailsMessage>(NavigationMapDetailsMessageListener);
	}

	public void DisconnectFromNetworkController()
	{
		EnvironmentReady = false;
		PlayerReady = false;
		EventSystem.RemoveListener<CharacterMovementMessage>(UpdateMovementListener);
		EventSystem.RemoveListener<EnvironmentReadyMessage>(EnvironmentReadyListener);
		EventSystem.RemoveListener<PlayerShootingMessage>(PlayerShootingListener);
		EventSystem.RemoveListener<PlayerStatsMessage>(PlayerStatsMessageListener);
		EventSystem.RemoveListener<PlayerDrillingMessage>(PlayerDrillingListener);
		EventSystem.RemoveListener<PlayerRoomMessage>(PlayerRoomMessageListener);
		EventSystem.RemoveListener<SuicideRequest>(SuicideListener);
		EventSystem.RemoveListener<AuthorizedVesselsRequest>(AuthorizedVesselsRequestListener);
		EventSystem.RemoveListener<LockToTriggerMessage>(LockToTriggerMessageListener);
		EventSystem.RemoveListener<QuestTriggerMessage>(QuestTriggerMessageListener);
		EventSystem.RemoveListener<SkipQuestMessage>(SkipQuestMessageListener);
		EventSystem.RemoveListener<NavigationMapDetailsMessage>(NavigationMapDetailsMessageListener);
	}

	public async Task RemovePlayerFromTrigger()
	{
		if (lastPlayerStatsMessage != null)
		{
			lastPlayerStatsMessage.LockedToTriggerID = null;
			await NetworkController.SendToClientsSubscribedTo(lastPlayerStatsMessage, Guid, Parent);
		}
	}

	private async void PlayerStatsMessageListener(NetworkData data)
	{
		var message = data as PlayerStatsMessage;
		if (FakeGuid == message.GUID)
		{
			lastPlayerStatsMessage = message;
			if (message.AnimationMaskChanged.HasValue && message.AnimationMaskChanged.Value)
			{
				AnimationStatsMask = message.AnimationStatesMask;
			}
			else
			{
				message.AnimationStatesMask = AnimationStatsMask;
			}
			LockedToTriggerID = message.LockedToTriggerID;
			IsPilotingVessel = message.IsPilotingVessel;
			await NetworkController.SendToClientsSubscribedTo(message, message.Sender, Parent);
		}
	}

	private static async Task PassTroughtShootMessage(PlayerShootingMessage psm)
	{
		PlayerShootingMessage sending = new PlayerShootingMessage();
		sending.HitIndentifier = -1;
		sending.ShotData = psm.ShotData;
		sending.HitGUID = psm.HitGUID;
		sending.GUID = psm.GUID;
		await NetworkController.SendToAllAsync(sending, psm.Sender);
	}

	protected async void PlayerShootingListener(NetworkData data)
	{
		var message = data as PlayerShootingMessage;
		if (message.Sender != Guid)
		{
			return;
		}
		Weapon wep = PlayerInventory.GetHandsItemIfType<Weapon>() as Weapon;
		if (wep == null && !message.ShotData.IsMeleeAttack)
		{
			return;
		}
		bool rateValid = false;
		if (message.ShotData.IsMeleeAttack)
		{
			if (Server.Instance.SolarSystem.CurrentTime - _lastMeleeTime > 1.0)
			{
				rateValid = true;
				_lastMeleeTime = Server.Instance.SolarSystem.CurrentTime;
			}
		}
		else if (wep != null && await wep.CanShoot())
		{
			rateValid = true;
		}
		if (!rateValid)
		{
			return;
		}
		if (message.HitGUID == -1)
		{
			message.HitGUID = -2L;
			await PassTroughtShootMessage(message);
			return;
		}
		SpaceObject sp = Server.Instance.GetObject(message.HitGUID);
		float damage = 0f;
		damage = wep == null ? message.ShotData.IsMeleeAttack ? 30f : 0f : message.ShotData.IsMeleeAttack ? wep.MeleeDamage : wep.Damage;
		if (sp is DynamicObject dynamicObject)
		{
			await dynamicObject.Item.TakeDamage(new Dictionary<TypeOfDamage, float> {
			{
				TypeOfDamage.Hit,
				damage
			} });
		}
		if (Server.Instance.GetObject(message.HitGUID) is Player hitPlayer)
		{
			await NetworkController.SendToClientsSubscribedTo(message, Guid, Parent, hitPlayer.Parent);
			await hitPlayer.TakeHitDamage(damage, (HitBoxType)message.ShotData.colliderType, message.ShotData.IsMeleeAttack, message.ShotData.Orientation.ToVector3D());
		}
	}

	protected async void PlayerDrillingListener(NetworkData data)
	{
		var message = data as PlayerDrillingMessage;
		HandDrill drill = ItemInHands as HandDrill;
		if (message.Sender == Guid && drill != null)
		{
			PlayerDrillingMessage pdmForOtherChar = new PlayerDrillingMessage();
			pdmForOtherChar.DrillersGUID = FakeGuid;
			pdmForOtherChar.dontPlayEffect = message.dontPlayEffect;
			pdmForOtherChar.isDrilling = message.isDrilling;
			await NetworkController.SendToClientsSubscribedTo(pdmForOtherChar, Guid, Parent);
			if (drill.CanDrill && message.MiningPointID != null && Server.Instance.GetVessel(message.MiningPointID.VesselGUID) is Asteroid asteroid && asteroid.MiningPoints.TryGetValue(message.MiningPointID.InSceneID, out var miningPoint))
			{
				await drill.Battery.ChangeQuantity((0f - drill.BatteryUsage) * message.MiningTime * drill.TierMultiplier);
				await drill.Canister.ChangeQuantityByAsync(0, miningPoint.ResourceType, drill.DrillingStrength * drill.TierMultiplier * message.MiningTime);
				miningPoint.Quantity = MathHelper.Clamp(miningPoint.Quantity - drill.DrillingStrength * drill.TierMultiplier * message.MiningTime, 0f, miningPoint.MaxQuantity);
				await drill.DrillBit.TakeDamage(TypeOfDamage.Degradation, drill.DrillBit.UsageWear * message.MiningTime * drill.DrillBit.TierMultiplier, forceTakeDamage: true);
			}
		}
	}

	protected async void EnvironmentReadyListener(NetworkData data)
	{
		var message = data as EnvironmentReadyMessage;
		if (message.Sender != Guid)
		{
			return;
		}
		SpawnObjectsResponse res = new SpawnObjectsResponse();
		NetworkController.AddCharacterSpawnsToResponse(this, ref res);
		if (Parent is SpaceObjectVessel)
		{
			SpaceObjectVessel vessel = Parent as SpaceObjectVessel;
			if (vessel.IsDocked)
			{
				vessel = vessel.DockedToMainVessel;
			}
			foreach (DynamicObject dynamicObject in vessel.DynamicObjects.Values)
			{
				res.Data.Add(dynamicObject.GetSpawnResponseData(this));
			}
			foreach (Corpse corpse in vessel.Corpses.Values)
			{
				res.Data.Add(corpse.GetSpawnResponseData(this));
			}
			if (vessel.AllDockedVessels.Count > 0)
			{
				foreach (SpaceObjectVessel child in vessel.AllDockedVessels)
				{
					foreach (DynamicObject childDynamicObject in child.DynamicObjects.Values)
					{
						res.Data.Add(childDynamicObject.GetSpawnResponseData(this));
					}
					foreach (Corpse childCorpse in child.Corpses.Values)
					{
						res.Data.Add(childCorpse.GetSpawnResponseData(this));
					}
				}
			}
		}
		await NetworkController.SendAsync(Guid, res);
		await NetworkController.SendCharacterSpawnToOtherPlayersAsync(this);
		MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		foreach (SpaceObjectVessel ves in from m in _subscribedToSpaceObjects
			select Server.Instance.GetObject(m) into m
			where m is SpaceObjectVessel
			select m as SpaceObjectVessel)
		{
			VesselObjects vesselObjects = ves.GetVesselObjects();
			if (vesselObjects.SceneTriggerExecutors != null)
			{
				foreach (SceneTriggerExecutorDetails sted in vesselObjects.SceneTriggerExecutors)
				{
					sted.IsImmediate = true;
				}
			}
			await NetworkController.SendAsync(Guid, new ShipStatsMessage
			{
				GUID = ves.Guid,
				VesselObjects = vesselObjects
			});
		}
		IsAlive = true;
		EnvironmentReady = true;
	}

	private async void SuicideListener(NetworkData data)
	{
		if (data.Sender == Guid)
		{
			await KillPlayer(HurtType.Suicide);
		}
	}

	private async void AuthorizedVesselsRequestListener(NetworkData data)
	{
		if (data.Sender == Guid)
		{
			await SendAuthorizedVesselsResponse();
		}
	}

	public async Task SendAuthorizedVesselsResponse()
	{
		AuthorizedVesselsResponse avr = new AuthorizedVesselsResponse
		{
			GUIDs = (from m in Server.Instance.AllVessels
				where m.AuthorizedPersonel.FirstOrDefault((AuthorizedPerson n) => n.PlayerId == PlayerId) != null
				select m.Guid).ToArray()
		};
		await NetworkController.SendAsync(Guid, avr);
	}

	private async void QuestTriggerMessageListener(NetworkData data)
	{
		var message = data as QuestTriggerMessage;
		if (message.Sender != Guid)
		{
			return;
		}
		Quest quest = Quests.FirstOrDefault((Quest m) => m.ID == message.QuestID);
		if (quest == null || (quest.DependencyQuests != null && Quests.FirstOrDefault((Quest m) => quest.DependencyQuests.Contains(m.ID) && m.Status != QuestStatus.Completed) != null))
		{
			return;
		}
		QuestTrigger qt = quest.QuestTriggers.FirstOrDefault((QuestTrigger m) => m.ID == message.TriggerID);
		if (qt is not { Status: QuestStatus.Active })
		{
			return;
		}
		await qt.SetQuestStatusAsync(QuestStatus.Completed);
		await qt.UpdateDependentTriggers(quest);
		List<Task> tasks = new List<Task>();
		if (qt.Type == QuestTriggerType.Activate && quest.Status == QuestStatus.Inactive)
		{
			quest.UpdateActivation();
			if (quest.ActivationDependencyTpe == QuestTriggerDependencyTpe.Any)
			{
				foreach (QuestTrigger aqt in quest.QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Activate))
				{
					await aqt.SetQuestStatusAsync(QuestStatus.Completed);
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
							await aaqt.SetQuestStatusAsync(QuestStatus.Completed);
							await aaqt.UpdateDependentTriggers(aaq);
						}
					}
					tasks.Add(
						NetworkController.SendAsync(message.Sender, new QuestStatsMessage
						{
							QuestDetails = aaq.GetDetails()
						}
					));
				}
			}
		}
		await NetworkController.SendAsync(message.Sender, new QuestStatsMessage
		{
			QuestDetails = quest.GetDetails()
		});
		foreach (Task t in tasks)
		{
			await t;
		}
	}

	private async void SkipQuestMessageListener(NetworkData data)
	{
		var message = data as SkipQuestMessage;
		if (message.Sender != Guid)
		{
			return;
		}
		Quest quest = Quests.FirstOrDefault((Quest m) => m.ID == message.QuestID);
		if (quest == null)
		{
			return;
		}
		quest.Status = QuestStatus.Completed;
		foreach (QuestTrigger qt in quest.QuestTriggers)
		{
			await qt.SetQuestStatusAsync(QuestStatus.Completed);
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
					await aaqt.SetQuestStatusAsync(QuestStatus.Completed);
					await aaqt.UpdateDependentTriggers(aaq);
				}
			}
			tasks.Add(NetworkController.SendAsync(message.Sender, new QuestStatsMessage
				{
					QuestDetails = aaq.GetDetails()
				}));
		}
		await NetworkController.SendAsync(message.Sender, new QuestStatsMessage
		{
			QuestDetails = quest.GetDetails()
		});
		foreach (Task t in tasks)
		{
			await t;
		}
	}

	private async void LockToTriggerMessageListener(NetworkData data)
	{
		var message = data as LockToTriggerMessage;
		if (message.Sender == Guid)
		{
			if (message.TriggerID == null)
			{
				LockedToTriggerID = null;
				IsPilotingVessel = message.IsPilotingVessel;
			}
			else if (Server.Instance.AllPlayers.FirstOrDefault((Player m) => m.Guid != Guid && m.LockedToTriggerID != null && m.LockedToTriggerID.Equals(message.TriggerID)) == null)
			{
				LockedToTriggerID = message.TriggerID;
				IsPilotingVessel = message.IsPilotingVessel;
				await NetworkController.SendAsync(message.Sender, message);
			}
		}
	}

	public void SetDockUndockCorrection(Vector3D? posCorrection, QuaternionD? rotCorrection)
	{
		_dockUndockPositionCorrection = posCorrection;
		_dockUndockRotationCorrection = rotCorrection;
		_dockUndockWaitForMsg = posCorrection.HasValue && rotCorrection.HasValue;
	}

	public void ModifyLocalPositionAndRotation(Vector3D locPos, QuaternionD locRot)
	{
		LocalPosition += locPos;
		LocalRotation *= locRot;
	}

	private async void UpdateMovementListener(NetworkData data)
	{
		var message = data as CharacterMovementMessage;
		if (message.Sender != Guid || !IsAlive || (Parent is Pivot && message.ParentType == SpaceObjectType.None))
		{
			return;
		}
		MouseLook = message.TransformData.MouseLook;
		FreeLookX = message.TransformData.FreeLookX;
		FreeLookY = message.TransformData.FreeLookY;
		CharacterAnimationData tmp = new CharacterAnimationData
		{
			VelocityForward = message.AnimationData.VelocityForward,
			VelocityRight = message.AnimationData.VelocityRight,
			ZeroGForward = message.AnimationData.ZeroGForward,
			ZeroGRight = message.AnimationData.ZeroGRight,
			PlayerStance = message.AnimationData.PlayerStance,
			InteractType = message.AnimationData.InteractType,
			TurningDirection = message.AnimationData.TurningDirection,
			EquipOrDeEquip = message.AnimationData.EquipOrDeEquip,
			EquipItemId = message.AnimationData.EquipItemId,
			EmoteType = message.AnimationData.EmoteType,
			ReloadItemType = message.AnimationData.ReloadItemType,
			MeleeAttackType = message.AnimationData.MeleeAttackType,
			LadderDirection = message.AnimationData.LadderDirection,
			PlayerStanceFloat = message.AnimationData.PlayerStanceFloat,
			GetUpType = message.AnimationData.GetUpType,
			FireMode = message.AnimationData.FireMode,
			AirTime = message.AnimationData.AirTime
		};
		AnimationData = tmp;
		if (_pivotPositionCorrection.IsNotEpsilonZero() && Parent is Pivot && message.ParentType == SpaceObjectType.PlayerPivot && !message.PivotReset)
		{
			return;
		}
		if (_pivotPositionCorrection.IsNotEpsilonZero() && message.ParentType == SpaceObjectType.PlayerPivot && message.PivotReset)
		{
			_pivotPositionCorrection = Vector3D.Zero;
			return;
		}
		LocalPosition = message.TransformData.LocalPosition.ToVector3D();
		if (_pivotPositionCorrection.IsNotEpsilonZero() && Parent is Pivot && !message.PivotReset)
		{
			LocalPosition -= _pivotPositionCorrection;
		}
		LocalRotation = message.TransformData.LocalRotation.ToQuaternionD();
		if (message.DockUndockMsg.HasValue && _dockUndockWaitForMsg)
		{
			SetDockUndockCorrection(null, null);
		}
		if (_dockUndockPositionCorrection.HasValue && _dockUndockRotationCorrection.HasValue)
		{
			LocalPosition += _dockUndockPositionCorrection.Value;
			LocalRotation *= _dockUndockRotationCorrection.Value;
		}
		LocalVelocity = message.TransformData.LocalVelocity.ToVector3D();
		_gravity = message.Gravity;
		if (message.ImpactVelocity.HasValue)
		{
			await DoCollisionDamage(message.ImpactVelocity.Value);
			_collisionImpactVelocity = message.ImpactVelocity.Value;
		}
		else
		{
			_collisionImpactVelocity = 0f;
		}

		if (message.RagdollData != null)
		{
			RagdollData = new Dictionary<byte, RagdollItemData>(message.RagdollData);
		}
		else
		{
			RagdollData?.Clear();
			RagdollData = null;
		}

		if (message.JetpackDirection != null)
		{
			_jetpackDirection =
			[
				message.JetpackDirection[0],
				message.JetpackDirection[1],
				message.JetpackDirection[2],
				message.JetpackDirection[3]
			];
		}
		else if (_jetpackDirection != null)
		{
			_jetpackDirection = null;
		}

		if (Parent is SpaceObjectVessel && message.ParentType == SpaceObjectType.PlayerPivot)
		{
			_pivotPositionCorrection = Vector3D.Zero;
			_pivotVelocityCorrection = Vector3D.Zero;
			LocalPosition = Vector3D.Zero;
			SpaceObjectVessel refVessel2 = (Parent as SpaceObjectVessel).MainVessel;
			Pivot pivot2 = new Pivot(this, refVessel2);
			pivot2.Orbit.CopyDataFrom(refVessel2.Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
			pivot2.Orbit.SetLastChangeTime(Server.SolarSystemTime);
			SubscribeTo(Parent);
			foreach (Player pl in Server.Instance.AllPlayers)
			{
				if (pl.IsSubscribedTo(Parent.Guid))
				{
					pl.SubscribeTo(pivot2);
				}
			}
			Parent = pivot2;
		}
		else if (Parent is SpaceObjectVessel && Parent.Guid != message.ParentGUID)
		{

			if (message.ParentType is SpaceObjectType.Ship or SpaceObjectType.Asteroid or SpaceObjectType.Station && Server.Instance.DoesObjectExist(message.ParentGUID))
			{
				Parent = Server.Instance.GetVessel(message.ParentGUID);
			}
			else
			{
				Debug.LogError("Unable to find new parent", Guid, Name, "new parent", message.ParentType, message.ParentGUID);
			}
		}
		else if (Parent is Pivot && message.ParentType != SpaceObjectType.PlayerPivot)
		{
			Pivot pivot3 = Parent as Pivot;
			if (message.ParentType is SpaceObjectType.Ship or SpaceObjectType.Asteroid or SpaceObjectType.Station && Server.Instance.DoesObjectExist(message.ParentGUID))
			{
				Parent = Server.Instance.GetVessel(message.ParentGUID);
				SubscribeTo(Parent);
				await pivot3.Destroy();
			}
			else
			{
				Debug.LogError("Unable to find new parent", Guid, Name, "new parent", message.ParentType, message.ParentGUID);
			}
		}
		else if (Parent is Pivot && _pivotPositionCorrection.IsEpsilonZero() && Server.Instance.RunTime.TotalSeconds - _lastPivotResetTime > 1.0)
		{
			Pivot pivot = Parent as Pivot;
			SpaceObjectVessel nearestVessel = message.NearestVesselGUID > 0 ? Server.Instance.GetVessel(message.NearestVesselGUID) : null;
			SpaceObjectVessel refVessel = nearestVessel.MainVessel;

			if (refVessel.StabilizeToTargetObj != null)
			{
				refVessel = refVessel.StabilizeToTargetObj;
			}

			// Stick to vessel if requested or if within 50m. If not, and if far enough away, reset pivot to zero local position.
			if (nearestVessel is { IsDebrisFragment: false } && (pivot.Position - refVessel.Position).IsNotEpsilonZero() && (message.StickToVessel || message.NearestVesselDistance <= 50f))
			{
				Vector3D oldPivotPos = pivot.Position;
				Vector3D oldPivotVel = pivot.Velocity;
				pivot.Orbit.CopyDataFrom(refVessel.Orbit, Server.Instance.SolarSystem.CurrentTime, exactCopy: true);
				pivot.Orbit.SetLastChangeTime(Server.Instance.SolarSystem.CurrentTime);
				_pivotPositionCorrection += pivot.Position - oldPivotPos;
				_pivotVelocityCorrection = pivot.Velocity - oldPivotVel;
				UpdateArtificialBodyMovement.Add(pivot.Guid);
				UpdateArtificialBodyMovement.Add(refVessel.Guid);
			}
			else if ((nearestVessel == null || (message.NearestVesselDistance > 50f && (nearestVessel.FTL is not
					 {
						 Status: SystemStatus.OnLine
					 } || (nearestVessel.Velocity - Parent.Velocity).SqrMagnitude < 900.0))) && LocalPosition.SqrMagnitude > 25000000.0 && !message.StickToVessel)
			{
				_pivotPositionCorrection = LocalPosition;
				pivot.AdjustPositionAndVelocity(_pivotPositionCorrection, _pivotVelocityCorrection);
				UpdateArtificialBodyMovement.Add(pivot.Guid);
			}
			if (_pivotPositionCorrection.IsNotEpsilonZero())
			{
				_lastPivotResetTime = Server.Instance.RunTime.TotalSeconds;
				LocalPosition -= _pivotPositionCorrection;
			}
		}
		PlayerReady = true;
	}

	public override async Task UpdateTimers(double deltaTime)
	{
		if (!IsAlive)
		{
			return;
		}
		updateTemperature(deltaTime);
		if (CoreTemperature is < 20f or > 45f)
		{
		}
		float suffocateDamage = 0f;
		float pressureDamage = 0f;
		float exposureDamage = Parent is not Ship ? StaticData.GetPlayerExposureDamage(Parent.Position.Magnitude) * (float)deltaTime : 0f;
		if (!IsInsideSpawnPoint)
		{
			if (CurrentHelmet != null && (!CurrentHelmet.IsVisorToggleable || CurrentHelmet.IsVisorActive))
			{
				if (CurrentJetpack is { HasOxygen: true })
				{
					await CurrentJetpack.ConsumeResources(null, CurrentJetpack.OxygenConsumption * (float)deltaTime);
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
			else if (CurrentRoom is { Breathability: < 1f })
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
			await TakeDamage((float)deltaTime, new PlayerDamage
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
		if (CurrentJetpack != null && _jetpackDirection != null && (_jetpackDirection[0] != 0 || _jetpackDirection[1] != 0 || _jetpackDirection[2] != 0 || _jetpackDirection[3] != 0))
		{
			await CurrentJetpack.ConsumeResources(CurrentJetpack.PropellantConsumption * (float)deltaTime);
		}
		if (CurrentHelmet == null && CurrentJetpack == null && ItemInHands == null)
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
			await ItemInHands.SendAllStats();
		}
		if (ItemInHands is not HandDrill && ItemInHands is Weapon)
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
			await NetworkController.SendToClientsSubscribedTo(doim, -1L, Parent);
		}
	}

	public void SubscribeTo(SpaceObject spaceObject)
	{
		lock (_subscribedToSpaceObjects)
		{
			_subscribedToSpaceObjects.Add(spaceObject.Guid);
			if (spaceObject is not SpaceObjectVessel ves)
			{
				return;
			}

			if (ves.IsDocked)
			{
				_subscribedToSpaceObjects.Add(ves.DockedToMainVessel.Guid);
				{
					foreach (SpaceObjectVessel obj2 in ves.DockedToMainVessel.AllDockedVessels)
					{
						_subscribedToSpaceObjects.Add(obj2.Guid);
					}
					return;
				}
			}
			if (ves.AllDockedVessels is not { Count: > 0 })
			{
				return;
			}
			foreach (SpaceObjectVessel obj in ves.AllDockedVessels)
			{
				_subscribedToSpaceObjects.Add(obj.Guid);
			}
		}
	}

	public void UnsubscribeFrom(SpaceObject spaceObject)
	{
		lock (_subscribedToSpaceObjects)
		{
			_subscribedToSpaceObjects.Remove(spaceObject.Guid);
		}
	}

	public void UnsubscribeFromAll()
	{
		lock (_subscribedToSpaceObjects)
		{
			_subscribedToSpaceObjects.Clear();
		}
	}

	public bool IsSubscribedTo(SpaceObject spaceObject, bool checkParent)
	{
		lock (_subscribedToSpaceObjects)
		{
			if (!checkParent)
			{
				return _subscribedToSpaceObjects.Contains(spaceObject.Guid);
			}
			return _subscribedToSpaceObjects.Contains(spaceObject.Guid) || (spaceObject.Parent != null && _subscribedToSpaceObjects.Contains(spaceObject.Parent.Guid));
		}
	}

	public bool IsSubscribedTo(long guid)
	{
		lock (_subscribedToSpaceObjects)
		{
			return _subscribedToSpaceObjects.Contains(guid);
		}
	}

	public void PlayerRoomMessageListener(NetworkData data)
	{
		var message = data as PlayerRoomMessage;
		if (message.Sender != Guid)
		{
			return;
		}
		isOutsideRoom = message.IsOutsideRoom.HasValue && message.IsOutsideRoom.Value;
		Room newRoom = null;
		if (message.ID != null)
		{
			SpaceObjectVessel newRoomVessel = Server.Instance.GetVessel(message.ID.VesselGUID);
			if (newRoomVessel != null)
			{
				newRoom = newRoomVessel.Rooms.FirstOrDefault((Room m) => m.ID.Equals(message.ID));
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
		CharacterMovementMessage message = new CharacterMovementMessage();
		message.GUID = FakeGuid;
		if (Parent != null)
		{
			message.ParentGUID = Parent.Guid;
			message.ParentType = Parent.ObjectType;
		}
		else
		{
			message.ParentGUID = -1L;
			message.ParentType = SpaceObjectType.None;
		}
		message.TransformData = new CharacterTransformData
		{
			LocalPosition = LocalPosition.ToFloatArray(),
			LocalRotation = LocalRotation.ToFloatArray(),
			MouseLook = MouseLook,
			FreeLookX = FreeLookX,
			FreeLookY = FreeLookY
		};
		message.Gravity = _gravity;
		message.AnimationData = new CharacterAnimationData();
		message.AnimationData.VelocityForward = AnimationData.VelocityForward;
		message.AnimationData.VelocityRight = AnimationData.VelocityRight;
		message.AnimationData.ZeroGForward = AnimationData.ZeroGForward;
		message.AnimationData.ZeroGRight = AnimationData.ZeroGRight;
		message.AnimationData.PlayerStance = AnimationData.PlayerStance;
		message.AnimationData.InteractType = AnimationData.InteractType;
		message.AnimationData.TurningDirection = AnimationData.TurningDirection;
		message.AnimationData.EquipOrDeEquip = AnimationData.EquipOrDeEquip;
		message.AnimationData.EquipItemId = AnimationData.EquipItemId;
		message.AnimationData.EmoteType = AnimationData.EmoteType;
		message.AnimationData.ReloadItemType = AnimationData.ReloadItemType;
		message.AnimationData.MeleeAttackType = AnimationData.MeleeAttackType;
		message.AnimationData.LadderDirection = AnimationData.LadderDirection;
		message.AnimationData.PlayerStanceFloat = AnimationData.PlayerStanceFloat;
		message.AnimationData.GetUpType = AnimationData.GetUpType;
		message.AnimationData.FireMode = AnimationData.FireMode;
		message.AnimationData.AirTime = AnimationData.AirTime;
		if (RagdollData is { Count: > 0 })
		{
			message.RagdollData = new Dictionary<byte, RagdollItemData>(RagdollData);
		}
		if (_jetpackDirection != null)
		{
			message.JetpackDirection = new sbyte[4]
			{
				_jetpackDirection[0],
				_jetpackDirection[1],
				_jetpackDirection[2],
				_jetpackDirection[3]
			};
		}
		message.PivotReset = _pivotPositionCorrection.IsNotEpsilonZero();
		message.PivotPositionCorrection = _pivotPositionCorrection.ToFloatArray();
		message.PivotVelocityCorrection = _pivotVelocityCorrection.ToFloatArray();
		if (_collisionImpactVelocity > 0f)
		{
			message.ImpactVelocity = _collisionImpactVelocity;
		}
		return message;
	}

	public override SpawnObjectResponseData GetSpawnResponseData(Player pl)
	{
		return new SpawnCharacterResponseData
		{
			GUID = Guid,
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
			PlayerId = PlayerId,
			ParentID = Parent != null ? Parent.Guid : -1,
			ParentType = Parent != null ? Parent.ObjectType : SpaceObjectType.None,
			DynamicObjects = dods,
			AnimationStatsMask = AnimationStatsMask,
			LockedToTriggerID = LockedToTriggerID
		};
		if (IsAlive || !checkAlive || CurrentSpawnPoint == null)
		{
			details.TransformData = new CharacterTransformData
			{
				LocalPosition = LocalPosition.ToFloatArray(),
				LocalRotation = LocalRotation.ToFloatArray(),
				MouseLook = MouseLook,
				FreeLookX = FreeLookX,
				FreeLookY = FreeLookY
			};
		}
		else
		{
			details.SpawnPointID = CurrentSpawnPoint.SpawnPointID;
		}
		return details;
	}

	public override async Task Destroy()
	{
		DisconnectFromNetworkController();
		while (DynamicObjects.Count > 0)
		{
			await DynamicObjects.First().Value.DestroyDynamicObject();
		}
		foreach (SpaceObjectVessel ves in Server.Instance.AllVessels)
		{
			if (ves is Ship ship)
			{
				await ship.ResetSpawnPointsForPlayer(this, sendStatsMessage: true);
			}
		}
		Server.Instance.Remove(this);
		await base.Destroy();
	}

	private void updateTemperature(double deltaTime)
	{
		updateOutfitTemperature(deltaTime);
		if (AmbientTemperature.HasValue)
		{
			CoreTemperature += (float)((AmbientTemperature - CoreTemperature) * 0.01 * deltaTime).Value;
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
				outfit.ExternalTemperature += (float)(((Parent as SpaceObjectVessel).Temperature - outfit.ExternalTemperature) * 0.001 * deltaTime);
			}
			float outfitInsulationFactor = 0.1f;
			outfit.InternalTemperature += (float)((outfit.ExternalTemperature - outfit.InternalTemperature) * 0.1 * deltaTime * outfitInsulationFactor);
			if (CurrentHelmet is { IsVisorActive: true } && CurrentJetpack != null)
			{
				float outfitTempRegulation = 5f;
				float tempCorr = (float)MathHelper.Clamp(37f - outfit.InternalTemperature, (0f - outfitTempRegulation) * deltaTime, outfitTempRegulation * deltaTime);
				outfit.InternalTemperature += tempCorr;
			}
		}
	}

	public async Task KillPlayer(HurtType causeOfDeath, bool createCorpse = true)
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
				await DynamicObjects.First().Value.DestroyDynamicObject();
			}
		}
		PlayerInventory = new Inventory(this);
		CurrentJetpack = null;
		CurrentHelmet = null;
		if (!DynamicObjects.IsEmpty)
		{
			string error = "Player had some dynamic objects that are not moved to corpse:";
			foreach (DynamicObject dobj in DynamicObjects.Values)
			{
				error = error + " " + dobj.Guid + ",";
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
			await (Parent as Pivot).Destroy();
		}
		if (CurrentSpawnPoint is { Type: SpawnPointType.SimpleSpawn })
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
		await NetworkController.SendToClientsSubscribedToParents(new KillPlayerMessage
		{
			GUID = FakeGuid,
			CauseOfDeath = causeOfDeath,
			VesselDamageType = vesselDamageType,
			CorpseDetails = corpse?.GetDetails()
		}, this, Guid);
		Parent = null;
		CurrentRoom = null;
		isOutsideRoom = false;
		if (NetworkController.IsPlayerConnected(Guid))
		{
			await NetworkController.SendAsync(Guid, new KillPlayerMessage
			{
				GUID = FakeGuid,
				CauseOfDeath = causeOfDeath,
				VesselDamageType = vesselDamageType
			});
			_lateDisconnectWait = 0.0;
			Server.Instance.SubscribeToTimer(UpdateTimer.TimerStep.Step_0_1_sec, LateDisconnect);
			NetworkController.DisconnectClient(Guid);
		}
		foreach (var q in Quests.Where(q => q.Status == QuestStatus.Active))
		{
			q.Status = QuestStatus.Inactive;
			foreach (QuestTrigger qt in q.QuestTriggers)
			{
				if (qt.Type == QuestTriggerType.Activate)
				{
					await qt.SetQuestStatusAsync(QuestStatus.Active);
					continue;
				}
				await qt.SetQuestStatusAsync(QuestStatus.Inactive);
				if (qt.SpawnRuleName is null or "")
				{
					continue;
				}
				QuestTrigger.QuestTriggerID qtid = qt.GetQuestTriggerID();
				foreach (var vessel in Server.Instance.AllVessels.Where(vessel => vessel.QuestTriggerID == qtid))
				{
					vessel.SelfDestructTimer = new SelfDestructTimer(vessel, 1f)
					{
						CheckPlayersDistance = 1000.0
					};
					vessel.AuthorizedPersonel.RemoveAll((AuthorizedPerson m) => m.PlayerId == PlayerId);
				}
			}
		}
	}

	private void LateDisconnect(double dbl)
	{
		_lateDisconnectWait += dbl;
		if (_lateDisconnectWait > 1.0)
		{
			NetworkController.DisconnectClient(Guid);
			Server.Instance.UnsubscribeFromTimer(UpdateTimer.TimerStep.Step_0_1_sec, LateDisconnect);
			_lateDisconnectWait = 0.0;
		}
	}

	public void LogoutDisconnectReset()
	{
		if (Parent is Ship)
		{
			(Parent as Ship).RemovePlayerFromExecutors(this);
		}
		UnsubscribeFromAll();
		EnvironmentReady = false;
		PlayerReady = false;
		LastMovementMessageSolarSystemTime = -1.0;
		MessagesReceivedWhileLoading = new ConcurrentQueue<ShipStatsMessage>();
		try
		{
			if (Parent is Ship && isOutsideRoom)
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
		data.GUID = Guid;
		data.FakeGUID = FakeGuid;
		if (Parent != null)
		{
			data.ParentGUID = Parent.Guid;
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
		data.PlayerId = PlayerId;
		data.Gender = Gender;
		data.HeadType = HeadType;
		data.HairType = HairType;
		data.Health = Health;
		data.MaxHealth = MaxHealth;
		data.AnimationData = ObjectCopier.DeepCopy(AnimationData);
		data.AnimationStatsMask = AnimationStatsMask;
		data.Gravity = _gravity;
		data.Velocity = LocalVelocity.ToArray();
		if (CurrentRoom != null)
		{
			data.CurrentRoomID = CurrentRoom.ID.InSceneID;
		}
		data.CoreTemperature = CoreTemperature;
		data.ChildObjects = new List<PersistenceObjectData>();
		DynamicObject outfitItem = DynamicObjects.Values.FirstOrDefault((DynamicObject m) => m.Item is { Slot.SlotID: -2 });
		if (outfitItem != null)
		{
			data.ChildObjects.Add(outfitItem.Item != null ? outfitItem.Item.GetPersistenceData() : outfitItem.GetPersistenceData());
		}
		foreach (DynamicObject dobj in DynamicObjects.Values)
		{
			if (dobj != outfitItem)
			{
				data.ChildObjects.Add(dobj.Item != null ? dobj.Item.GetPersistenceData() : dobj.GetPersistenceData());
			}
		}
		data.Quests = Quests.Select((Quest m) => m.GetDetails()).ToList();
		data.Blueprints = Blueprints;
		data.NavMapDetails = NavMapDetails;
		return data;
	}

	public async Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		if (persistenceData is not PersistenceObjectDataPlayer data)
		{
			Debug.LogError("PersistenceObjectDataPlayer data is null", Guid);
			return;
		}

		Guid = data.GUID;
		FakeGuid = data.FakeGUID;
		LocalPosition = data.LocalPosition.ToVector3D();
		LocalRotation = data.LocalRotation.ToQuaternionD();
		IsAlive = data.IsAlive;
		Name = data.Name;
		PlayerId = data.PlayerId;
		Gender = data.Gender;
		HeadType = data.HeadType;
		HairType = data.HairType;
		MaxHealth = data.MaxHealth;
		Health = data.Health;
		AnimationData = ObjectCopier.DeepCopy(data.AnimationData);
		AnimationStatsMask = data.AnimationStatsMask;
		_gravity = data.Gravity;
		LocalVelocity = data.Velocity.ToVector3D();
		CoreTemperature = data.CoreTemperature;
		SpaceObject parent = null;
		if (data.ParentType == SpaceObjectType.PlayerPivot)
		{
			parent = new Pivot(this, data.ParentPosition.ToVector3D(), data.ParentVelocity.ToVector3D());
		}
		else if (data.ParentGUID != -1)
		{
			parent = Server.Instance.GetObject(data.ParentGUID);
		}
		if (parent != null)
		{
			Parent = parent;
			if (data.CurrentRoomID.HasValue && Parent is SpaceObjectVessel)
			{
				CurrentRoom = (Parent as SpaceObjectVessel).Rooms.FirstOrDefault((Room m) => m.ID.InSceneID == data.CurrentRoomID.Value);
			}
		}
		else
		{
			if (data.ParentGUID != -1 && parent == null)
			{
				Debug.LogError("Player parent object not found, SAVE MIGHT BE CORRUPTED", Guid, data.ParentGUID, data.ParentType);
				return;
			}
			Parent = null;
			await KillPlayer(HurtType.None, createCorpse: false);
		}
		if (Parent != null)
		{
			foreach (PersistenceObjectDataDynamicObject dobjData in data.ChildObjects.Cast<PersistenceObjectDataDynamicObject>())
			{
				await Persistence.CreateDynamicObject(dobjData, this);
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
						await questTrigger.SetQuestStatusAsync(qtDet.Status);
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

	public void SetSpawnPoint(ShipSpawnPoint spawnPoint)
	{
		if (spawnPoint is { Type: SpawnPointType.WithAuthorization })
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
		var message = data as NavigationMapDetailsMessage;
		if (message.Sender == Guid)
		{
			NavMapDetails = message.NavMapDetails;
		}
	}

	public Task TakeDamage(HurtType hurtType, float amount)
	{
		return TakeDamage(1f, new PlayerDamage
		{
			HurtType = hurtType,
			Amount = amount
		});
	}

	public Task TakeDamage(float deltaTime, params PlayerDamage[] damages)
	{
		return TakeDamage(null, deltaTime, damages);
	}

	public async Task TakeDamage(Vector3D? shotDirection, float deltaTime, params PlayerDamage[] damages)
	{
		if (GodMode || CurrentSpawnPoint is { Executor: not null, IsPlayerInSpawnPoint: true })
		{
			return;
		}
		float amount = damages.Where((PlayerDamage m) => m.Amount > 0f && m.HurtType is HurtType.Suffocate or HurtType.Pressure).Sum((PlayerDamage m) => m.Amount);
		float hurt = damages.Where((PlayerDamage m) => m.Amount > 0f && m.HurtType != HurtType.Suffocate && m.HurtType != HurtType.Pressure).Sum((PlayerDamage m) => m.Amount);
		amount = PlayerInventory.CurrOutfit == null ? amount + hurt : amount + MathHelper.Clamp(hurt - PlayerInventory.CurrOutfit.Armor * deltaTime, 0f, float.MaxValue);
		if (amount <= float.Epsilon)
		{
			return;
		}
		HurtType causeOfDeath = damages.OrderBy((PlayerDamage m) => m.Amount).Reverse().FirstOrDefault()?.HurtType ?? HurtType.None;
		Health = MathHelper.Clamp(Health - amount, 0f, MaxHealth);
		if (Health <= float.Epsilon)
		{
			await KillPlayer(causeOfDeath);
			return;
		}
		_acummulatedDamage += amount;
		PlayerStatsMessage message = new PlayerStatsMessage();
		foreach (PlayerDamage dmg in damages.Where((PlayerDamage m) => m.Amount > 0f))
		{
			PlayerDamage pd = message.DamageList.FirstOrDefault((PlayerDamage m) => m.HurtType == dmg.HurtType);
			if (pd == null)
			{
				message.DamageList.Add(dmg);
			}
			else
			{
				pd.Amount += dmg.Amount;
			}
		}
		message.ShotDirection = shotDirection.HasValue ? shotDirection.Value.ToFloatArray() : null;
		if (_acummulatedDamage > 1f)
		{
			message.GUID = FakeGuid;
			message.Health = (int)Health;
			await NetworkController.SendAsync(Guid, message);
			_acummulatedDamage = 0f;
		}
	}

	public async Task Heal(float amount)
	{
		amount = amount > 0f ? amount : 0f;
		if (amount <= float.Epsilon || Health == MaxHealth)
		{
			return;
		}
		Health = MathHelper.Clamp(Health + amount, 0f, MaxHealth);
		PlayerStatsMessage message = new PlayerStatsMessage
		{
			GUID = FakeGuid,
			Health = (int)Health
		};
		await NetworkController.SendAsync(Guid, message);
	}

	private async Task HealOverTimeStep()
	{
		_amountToHeal -= _amountToHealStep;
		if (_amountToHeal <= 0f)
		{
			await Heal(_amountToHealStep);
			_healTimer.Enabled = false;
		}
		else
		{
			await Heal(_amountToHealStep);
		}
	}

	public void HealOverTime(float amountOverSec, float duration)
	{
		if (_healTimer.Enabled)
		{
			_amountToHeal += amountOverSec * duration;
			_amountToHealStep = (_amountToHealStep + amountOverSec * 0.1f) * 0.5f;
		}
		else
		{
			_amountToHeal = amountOverSec * duration;
			_amountToHealStep = amountOverSec * 0.1f;
			_healTimer.Enabled = true;
		}
	}

	public Task DoCollisionDamage(float speed)
	{
		double threshold = 6.5;
		float hp = 0f;
		if (speed >= threshold)
		{
			hp = (float)((speed - threshold) * (speed - threshold) / 10.0 + speed) * (PlayerInventory.CurrOutfit != null ? PlayerInventory.CurrOutfit.CollisionResistance : 1f);
		}
		return TakeDamage(HurtType.Impact, hp);
	}

	public async Task<float> TakeHitDamage(float damage, HitBoxType hitType, bool isMelee, Vector3D? direction = null, float duration = 1f)
	{
		Outfit outfit = PlayerInventory.CurrOutfit;
		Helmet helmet = CurrentHelmet;
		float resistanceMulti = 1f;
		float reductionValue = 0f;
		float bodyDmgMulti = 1f;
		switch (hitType)
		{
		case HitBoxType.None:
			resistanceMulti = 0f;
			Debug.LogError("UNKNOWN HITBOX TYPE", Guid);
			break;
		case HitBoxType.Head:
			bodyDmgMulti = 10f;
			resistanceMulti = helmet?.DamageResistance ?? 1f;
			reductionValue = helmet?.DamageReduction ?? 0f;
			break;
		case HitBoxType.Torso:
			bodyDmgMulti = 5f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceTorso;
				reductionValue = outfit.DamageReductionTorso;
			}
			break;
		case HitBoxType.Arms:
			bodyDmgMulti = 1f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceArms;
				reductionValue = outfit.DamageReductionArms;
			}
			break;
		case HitBoxType.Legs:
			bodyDmgMulti = 1f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceLegs;
				reductionValue = outfit.DamageReductionLegs;
			}
			break;
		case HitBoxType.Abdomen:
			bodyDmgMulti = 2f;
			if (outfit != null)
			{
				resistanceMulti = outfit.DamageResistanceAbdomen;
				reductionValue = outfit.DamageReductionAbdomen;
			}
			break;
		default:
			Debug.LogError($"UNKNOWN HITBOX TYPE: {hitType}. damage={damage:F2}, isMelee={isMelee}, duration={duration:F2}, player={Guid}, health={Health}/{MaxHealth}, outfit={(outfit != null ? outfit.GetType().Name : "null")}, helmet={(helmet != null ? helmet.GetType().Name : "null")}");
			break;
		}
		if (isMelee)
		{
			bodyDmgMulti = 1f;
		}
		float amount = (damage - reductionValue * duration) * resistanceMulti * bodyDmgMulti;
		await TakeDamage(direction, duration, new PlayerDamage
		{
			HurtType = HurtType.Shot,
			Amount = amount
		});
		return amount;
	}
}
