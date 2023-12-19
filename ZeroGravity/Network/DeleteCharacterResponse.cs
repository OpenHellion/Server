using OpenHellion.Net.Message.MainServer;
using ProtoBuf;

namespace ZeroGravity.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class DeleteCharacterResponse : NetworkData
{
	public ResponseResult Response = ResponseResult.Success;

	public string Message = "";
}
