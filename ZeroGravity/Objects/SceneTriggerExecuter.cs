using System;
using System.Collections.Generic;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class SceneTriggerExecuter : IPersistantObject
{
	public int InSceneID;

	public Ship ParentShip;

	public int DefaultStateID = 0;

	private int _stateID = 0;

	public Dictionary<int, SceneTriggerExceuterState> States = new Dictionary<int, SceneTriggerExceuterState>();

	public long PlayerThatActivated = 0L;

	public ShipSpawnPoint SpawnPoint;

	public SceneTriggerExecuter Child = null;

	public SceneTriggerExecuter Parent = null;

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

	public void MergeWith(SceneTriggerExecuter child)
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

	public void SetStates(List<SceneTriggerExecuterStateData> states, int defaultStateID)
	{
		foreach (SceneTriggerExecuterStateData st in states)
		{
			States.Add(st.StateID, new SceneTriggerExceuterState
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

	public SceneTriggerExecuterDetails ChangeState(long sender, SceneTriggerExecuterDetails details)
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
			if (SpawnPoint != null && SpawnPoint.Player != null)
			{
				if (StateID == SpawnPoint.ExecuterStateID || (SpawnPoint.ExecuterOccupiedStateIDs != null && SpawnPoint.ExecuterOccupiedStateIDs.Contains(StateID)))
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

	public SceneTriggerExecuterDetails RemovePlayerFromExecuter(Player pl)
	{
		if (PlayerThatActivated != pl.FakeGuid || !States.ContainsKey(_stateID) || States[_stateID].PlayerDisconnectToStateID == 0 || !States.ContainsKey(States[_stateID].PlayerDisconnectToStateID))
		{
			return null;
		}
		return new SceneTriggerExecuterDetails
		{
			InSceneID = InSceneID,
			IsFail = false,
			CurrentStateID = StateID,
			NewStateID = States[_stateID].PlayerDisconnectToStateID,
			IsImmediate = States[_stateID].PlayerDisconnectToStateImmediate,
			PlayerThatActivated = 0L
		};
	}

	public SceneTriggerExecuterDetails RemovePlayerFromProximity(Player pl)
	{
		if (ProximityTriggers == null || ProximityTriggers.Count == 0)
		{
			return null;
		}
		SceneTriggerProximity proxEmptied = null;
		bool hasProxWithPlayer = false;
		foreach (KeyValuePair<int, SceneTriggerProximity> prox in ProximityTriggers)
		{
			if (prox.Value.ObjectsInTrigger.Contains(pl.GUID))
			{
				prox.Value.ObjectsInTrigger.Remove(pl.GUID);
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
		return new SceneTriggerExecuterDetails
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

	public bool AreStatesEqual(SceneTriggerExecuter other)
	{
		return States.Count == other.States.Count;
	}

	public PersistenceObjectData GetPersistenceData()
	{
		return new PersistenceObjectDataExecuter
		{
			InSceneID = InSceneID,
			StateID = StateID,
			PlayerThatActivated = PlayerThatActivated
		};
	}

	public void LoadPersistenceData(PersistenceObjectData persistenceData)
	{
		try
		{
			if (!(persistenceData is PersistenceObjectDataExecuter data))
			{
				Dbg.Warning("PersistenceObjectDataExecuter data is null");
				return;
			}
			StateID = data.StateID;
			PlayerThatActivated = data.PlayerThatActivated;
		}
		catch (Exception e)
		{
			Dbg.Exception(e);
		}
	}
}
