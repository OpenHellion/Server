namespace ZeroGravity;

/// <summary>
/// 	An item a player released inside a vessel. At runtime such an item floats on its own pivot and
/// 	belongs to no vessel, so nothing else that is persisted can reach it. This records which vessel
/// 	it was released in and where, so it can be put back into that vessel on load, where it behaves
/// 	like any other loose item lying in a room.
/// 	Items released in open space are not recorded: they drift out of reach and are cleaned up.
/// </summary>
public class PersistenceObjectDataPivot : PersistenceObjectData
{
	public long ParentVesselGUID;

	public float[] LocalPosition;

	public PersistenceObjectData Child;
}
