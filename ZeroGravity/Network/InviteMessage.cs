using ProtoBuf;

namespace ZeroGravity.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class InviteMessage : NetworkData
{
	public float Time;

	public long ServerId;

	public VesselObjectID SpawnPointId;
}
