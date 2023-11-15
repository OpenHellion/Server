using System.Collections.Generic;
using System.Linq;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity.Objects;

public class Quest
{
	public uint ID;

	public List<QuestTrigger> QuestTriggers;

	public QuestTriggerDependencyTpe ActivationDependencyTpe;

	public QuestTriggerDependencyTpe CompletionDependencyTpe;

	public QuestStatus Status;

	public List<uint> DependencyQuests;

	public bool AutoActivate;

	public Player Player;

	public bool IsFineshed => Status == QuestStatus.Completed || Status == QuestStatus.Failed;

	public Quest(QuestData data, Player player)
	{
		ID = data.ID;
		QuestTriggers = data.QuestTriggers.Select((QuestTriggerData m) => new QuestTrigger(this, m)).ToList();
		ActivationDependencyTpe = data.ActivationDependencyTpe;
		CompletionDependencyTpe = data.CompletionDependencyTpe;
		DependencyQuests = data.DependencyQuests;
		AutoActivate = data.AutoActivate;
		Player = player;
	}

	public QuestDetails GetDetails()
	{
		return new QuestDetails
		{
			ID = ID,
			QuestTriggers = QuestTriggers != null ? QuestTriggers.Select((QuestTrigger m) => m.GetDetails()).ToList() : null,
			Status = Status
		};
	}

	public void UpdateActivation()
	{
		if (Status == QuestStatus.Inactive)
		{
			List<QuestTrigger> batch = QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Activate).ToList();
			int completedCount = batch.Count((QuestTrigger m) => m.Status == QuestStatus.Completed);
			if ((ActivationDependencyTpe == QuestTriggerDependencyTpe.Any && completedCount > 0) || (ActivationDependencyTpe == QuestTriggerDependencyTpe.All && completedCount == batch.Count))
			{
				Status = QuestStatus.Active;
			}
		}
	}

	public void UpdateCompletion()
	{
		if (!IsFineshed)
		{
			List<QuestTrigger> batch = QuestTriggers.Where((QuestTrigger m) => m.Type == QuestTriggerType.Complete).ToList();
			int completedCount = batch.Count((QuestTrigger m) => m.Status == QuestStatus.Completed);
			if ((CompletionDependencyTpe == QuestTriggerDependencyTpe.Any && completedCount > 0) || (CompletionDependencyTpe == QuestTriggerDependencyTpe.All && completedCount == batch.Count))
			{
				Status = QuestStatus.Completed;
			}
		}
	}
}
