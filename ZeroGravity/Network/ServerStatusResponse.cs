using OpenHellion.Networking.Message.MainServer;
using ProtoBuf;

namespace ZeroGravity.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ServerStatusResponse : NetworkData
{
	public ResponseResult Response = ResponseResult.Success;

	public short CurrentPlayers;

	public short MaxPlayers;

	public CharacterData CharacterData;
}
