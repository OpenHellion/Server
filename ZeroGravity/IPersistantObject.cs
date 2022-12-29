namespace ZeroGravity;

public interface IPersistantObject
{
	PersistenceObjectData GetPersistenceData();

	void LoadPersistenceData(PersistenceObjectData persistenceData);
}
