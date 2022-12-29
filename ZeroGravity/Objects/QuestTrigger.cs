using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.Spawn;

namespace ZeroGravity.Objects;

public class QuestTrigger
{
	public class QuestTriggerID
	{
		public long PlayerGUID;

		public uint QuestID;

		public uint ID;

		public override bool Equals(object obj)
		{
			return this == obj as QuestTriggerID;
		}

		public override int GetHashCode()
		{
			return new object[(int)checked((nint)PlayerGUID), QuestID, ID].GetHashCode();
		}

		public static bool operator ==(QuestTriggerID x, QuestTriggerID y)
		{
			return x.PlayerGUID == y.PlayerGUID && x.QuestID == y.QuestID && x.ID == y.ID;
		}

		public static bool operator !=(QuestTriggerID x, QuestTriggerID y)
		{
			return x.PlayerGUID != y.PlayerGUID || x.QuestID != y.QuestID || x.ID != y.ID;
		}
	}

	public uint ID;

	public uint BatchID;

	public QuestTriggerType Type;

	public string Station;

	public string Tag;

	public CelestialBodyGUID Celestial = CelestialBodyGUID.None;

	public uint DependencyBatchID;

	public QuestTriggerDependencyTpe DependencyTpe;

	public string SpawnRuleName;

	public Quest Quest;

	private QuestStatus _Status;

	public QuestStatus Status
	{
		get
		{
			return _Status;
		}
		set
		{
			if (_Status != value)
			{
				_Status = value;
				if (_Status == QuestStatus.Active && !SpawnRuleName.IsNullOrEmpty())
				{
					SpawnQuestStation();
				}
			}
		}
	}

	public QuestTrigger(Quest quest, QuestTriggerData data)
	{
		ID = data.ID;
		BatchID = data.BatchID;
		Type = data.Type;
		Station = data.Station;
		Tag = data.Tag;
		Celestial = data.Celestial;
		if (Type == QuestTriggerType.Activate)
		{
			Status = QuestStatus.Active;
		}
		DependencyBatchID = data.DependencyBatchID;
		DependencyTpe = data.DependencyTpe;
		SpawnRuleName = data.SpawnRuleName;
		Quest = quest;
	}

	public QuestTriggerDetails GetDetails()
	{
		long locGUID = SpawnManager.GetStationMainVesselGUID(Station);
		return new QuestTriggerDetails
		{
			ID = ID,
			Status = Status,
			StationMainVesselGUID = locGUID
		};
	}

	public void UpdateDependentTriggers(Quest quest)
	{
		if (BatchID == 0)
		{
			return;
		}
		List<QuestTrigger> batch = quest.QuestTriggers.Where((QuestTrigger m) => m.BatchID == BatchID).ToList();
		int completedCount = batch.Count((QuestTrigger m) => m.Status == QuestStatus.Completed);
		bool fixIncomplete = false;
		foreach (QuestTrigger qt2 in quest.QuestTriggers.Where((QuestTrigger m) => m.DependencyBatchID == BatchID))
		{
			if (qt2.DependencyTpe == QuestTriggerDependencyTpe.Any)
			{
				fixIncomplete = true;
			}
			if ((qt2.DependencyTpe == QuestTriggerDependencyTpe.Any && completedCount > 0) || (qt2.DependencyTpe == QuestTriggerDependencyTpe.All && completedCount == batch.Count))
			{
				qt2.Status = QuestStatus.Active;
			}
		}
		if (!fixIncomplete)
		{
			return;
		}
		foreach (QuestTrigger qt in batch)
		{
			qt.Status = QuestStatus.Completed;
		}
	}

	private void SpawnQuestStation()
	{
		SpawnManager.SpawnQuestSetup(this);
	}

	public QuestTriggerID GetQuestTriggerID()
	{
		return new QuestTriggerID
		{
			PlayerGUID = Quest.Player.GUID,
			QuestID = Quest.ID,
			ID = ID
		};
	}
}
