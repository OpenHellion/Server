using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using OpenHellion.Net.Message;
using ZeroGravity.Math;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Corpse : SpaceObjectTransferable
{
	public double DestroyTime = 120000.0;

	public static readonly double ArenaTimer = TimeSpan.FromMinutes(30.0).TotalMilliseconds;

	public static readonly double EmptyCorpseTimer = TimeSpan.FromMinutes(5.0).TotalMilliseconds;

	public static readonly double OutsideTimer = TimeSpan.FromHours(3.0).TotalMilliseconds;

	public static readonly double InsideModuleTimer = TimeSpan.FromHours(24.0).TotalMilliseconds;

	public Vector3D AngularVelocity;

	public double LastChangeTime;

	private Timer _destroyTimer;

	public Gender Gender;

	private SpaceObject _parent;

	public override SpaceObjectType ObjectType => SpaceObjectType.Corpse;

	public Inventory CorpseInventory { get; private set; }

	public override SpaceObject Parent
	{
		get
		{
			return _parent;
		}
		set
		{
			if (_parent != null)
			{
				Parent.Corpses.Remove(Guid);
			}
			_parent = value;
			if (_parent != null)
			{
				Parent.Corpses.Add(Guid);
			}
		}
	}

	public Corpse(Player player)
		: base(GUIDFactory.NextObjectGUID(), player.LocalPosition, player.LocalRotation)
	{
		if (player.Parent is Pivot parent)
		{
			Parent = new Pivot(this, parent);
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
		LastChangeTime = Server.SolarSystemTime;
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
			_destroyTimer = new Timer(DestroyTime);
			_destroyTimer.Elapsed += async delegate
			{
				await DestoyCorpseTimerElapsed();
			};
			_destroyTimer.Enabled = true;
		}
		Gender = player.Gender;
	}

	private Task DestoyCorpseTimerElapsed()
	{
		return Destroy();
	}

	internal void CheckInventoryDestroy()
	{
		if (CorpseInventory.HandsSlot.Item == null && (CorpseInventory.CurrOutfit == null || CorpseInventory.CurrOutfit.InventorySlots.Where((KeyValuePair<short, InventorySlot> m) => m.Value.Item != null) != null))
		{
			_destroyTimer?.Dispose();
			_destroyTimer = new Timer(TimeSpan.FromMinutes(5.0).TotalMilliseconds);
			_destroyTimer.Elapsed += async delegate
			{
				await DestoyCorpseTimerElapsed();
			};
			_destroyTimer.Enabled = true;
		}
	}

	public ObjectsInfoResponse.CorpseData GetCorpseData(Player pl)
	{
		return new ObjectsInfoResponse.CorpseData
		{
			Guid = Guid,
			Position = LocalPosition.ToFloatArray(),
			Rotation = LocalRotation.ToFloatArray(),
			ParentGUID = Parent == null ? -1 : Parent.Guid,
			Gender = Gender,
			DynamicObjects = DynamicObject.GetCarriedDetails(this)
		};
	}

	public override async Task Destroy()
	{
		_destroyTimer?.Dispose();
		await base.Destroy();
	}
}
