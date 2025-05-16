using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class SceneTriggerExecutor : IPersistantObject
{
	public int InSceneID;

	public Ship ParentShip;

	public int DefaultStateID;

	private int _stateID;

	public Dictionary<int, SceneTriggerExecutorState> States = new Dictionary<int, SceneTriggerExecutorState>();

	public long PlayerThatActivated;

	public ShipSpawnPoint SpawnPoint;

	public SceneTriggerExecutor Child;

	public SceneTriggerExecutor Parent;

	public Dictionary<int, SceneTriggerProximity> ProximityTriggers;

	public int StateID
	{
		get
		{
			return _stateID;
		}
		set
		{
			if (States.ContainsKey(value))
			{
				_stateID = value;
				if (Child != null)
				{
					Child._stateID = value;
				}
				else if (Parent != null)
				{
					Parent._stateID = value;
				}
			}
		}
	}

	public bool IsMerged => Child != null || Parent != null;

	public void MergeWith(SceneTriggerExecutor child)
	{
		if (child == null)
		{
			if (Child != null)
			{
				Child.Parent = null;
				Child = null;
			}
			if (Parent != null)
			{
				Parent.Child = null;
				Parent = null;
			}
		}
		else
		{
			Child = child;
			Child.Parent = this;
			Child.StateID = StateID;
		}
	}

	public void SetStates(List<SceneTriggerExecutorStateData> states, int defaultStateID)
	{
		foreach (SceneTriggerExecutorStateData st in states)
		{
			States.Add(st.StateID, new SceneTriggerExecutorState
			{
				StateID = st.StateID,
				PlayerDisconnectToStateID = st.PlayerDisconnectToStateID,
				PlayerDisconnectToStateImmediate = st.PlayerDisconnectToStateImmediate
			});
		}
		if (States.ContainsKey(defaultStateID))
		{
			DefaultStateID = defaultStateID;
			StateID = defaultStateID;
		}
	}

	public SceneTriggerExecutorDetails ChangeState(long sender, SceneTriggerExecutorDetails details)
	{
		Player pl = null;
		try
		{
			pl = Server.Instance.GetPlayer(sender);
			details.PlayerThatActivated = pl.FakeGuid;
		}
		catch
		{
			details.PlayerThatActivated = 0L;
		}
		if (details.ProximityTriggerID.HasValue)
		{
			if (ProximityTriggers == null || !ProximityTriggers.ContainsKey(details.ProximityTriggerID.Value))
			{
				details.IsFail = true;
				return details;
			}
			bool containsGUID = ProximityTriggers[details.ProximityTriggerID.Value].ObjectsInTrigger.Contains(sender);
			if (details.ProximityIsEnter.Value)
			{
				if (!containsGUID)
				{
					ProximityTriggers[details.ProximityTriggerID.Value].ObjectsInTrigger.Add(sender);
				}
				if (ProximityTriggers[details.ProximityTriggerID.Value].ObjectsInTrigger.Count == 0)
				{
					details.IsFail = true;
				}
			}
			else
			{
				if (containsGUID)
				{
					ProximityTriggers[details.ProximityTriggerID.Value].ObjectsInTrigger.Remove(sender);
				}
				if (ProximityTriggers[details.ProximityTriggerID.Value].ObjectsInTrigger.Count > 0)
				{
					details.IsFail = true;
				}
			}
		}
		if (StateID == details.NewStateID && (!details.IsImmediate.HasValue || !details.IsImmediate.Value))
		{
			details.IsFail = true;
		}
		if (!States.ContainsKey(details.NewStateID))
		{
			details.IsFail = true;
		}
		if (SpawnPoint != null && ((SpawnPoint.Type == SpawnPointType.WithAuthorization && SpawnPoint.State == SpawnPointState.Locked) || (SpawnPoint.Player != null && SpawnPoint.Player != pl) || SpawnPoint.Player == null))
		{
			details.IsFail = true;
		}
		if (!details.IsFail)
		{
			StateID = details.NewStateID;
			if (SpawnPoint is { Player: not null })
			{
				if (StateID == SpawnPoint.ExecutorStateID || (SpawnPoint.ExecutorOccupiedStateIDs != null && SpawnPoint.ExecutorOccupiedStateIDs.Contains(StateID)))
				{
					SpawnPoint.Player.IsInsideSpawnPoint = true;
					SpawnPoint.IsPlayerInSpawnPoint = true;
				}
				else
				{
					SpawnPoint.Player.IsInsideSpawnPoint = false;
					SpawnPoint.IsPlayerInSpawnPoint = false;
					if (SpawnPoint.Type == SpawnPointType.SimpleSpawn)
					{
						SpawnPoint.Player.SetSpawnPoint(null);
						SpawnPoint.Player = null;
					}
				}
			}
			try
			{
				PlayerThatActivated = Server.Instance.GetPlayer(sender).FakeGuid;
			}
			catch
			{
				PlayerThatActivated = 0L;
			}
		}
		return details;
	}

	public SceneTriggerExecutorDetails RemovePlayerFromExecutor(Player pl)
	{
		if (PlayerThatActivated != pl.FakeGuid || !States.ContainsKey(_stateID) || States[_stateID].PlayerDisconnectToStateID == 0 || !States.ContainsKey(States[_stateID].PlayerDisconnectToStateID))
		{
			return null;
		}
		return new SceneTriggerExecutorDetails
		{
			InSceneID = InSceneID,
			IsFail = false,
			CurrentStateID = StateID,
			NewStateID = States[_stateID].PlayerDisconnectToStateID,
			IsImmediate = States[_stateID].PlayerDisconnectToStateImmediate,
			PlayerThatActivated = 0L
		};
	}

	public SceneTriggerExecutorDetails RemovePlayerFromProximity(Player pl)
	{
		if (ProximityTriggers == null || ProximityTriggers.Count == 0)
		{
			return null;
		}
		SceneTriggerProximity proxEmptied = null;
		bool hasProxWithPlayer = false;
		foreach (KeyValuePair<int, SceneTriggerProximity> prox in ProximityTriggers)
		{
			if (prox.Value.ObjectsInTrigger.Contains(pl.Guid))
			{
				prox.Value.ObjectsInTrigger.Remove(pl.Guid);
				if (prox.Value.ObjectsInTrigger.Count == 0)
				{
					proxEmptied = prox.Value;
				}
			}
			if (prox.Value.ObjectsInTrigger.Count > 0)
			{
				hasProxWithPlayer = true;
			}
		}
		if (hasProxWithPlayer || proxEmptied == null || StateID != proxEmptied.ActiveStateID)
		{
			return null;
		}
		return new SceneTriggerExecutorDetails
		{
			InSceneID = InSceneID,
			IsFail = false,
			CurrentStateID = StateID,
			NewStateID = proxEmptied.InactiveStateID,
			PlayerThatActivated = 0L,
			ProximityTriggerID = proxEmptied.TriggerID,
			ProximityIsEnter = false
		};
	}

	public bool AreStatesEqual(SceneTriggerExecutor other)
	{
		return States.Count == other.States.Count;
	}

	public PersistenceObjectData GetPersistenceData()
	{
		return new PersistenceObjectDataExecutor
		{
			InSceneID = InSceneID,
			StateID = StateID,
			PlayerThatActivated = PlayerThatActivated
		};
	}

	public Task LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		if (persistenceData is not PersistenceObjectDataExecutor data)
		{
			Debug.LogError("PersistenceObjectDataExecutor data is null");
			return Task.CompletedTask;
		}
		StateID = data.StateID;
		PlayerThatActivated = data.PlayerThatActivated;

		return Task.CompletedTask;
	}
}
