using System.Collections.Generic;
using ProtoBuf;

namespace ZeroGravity.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class SignInResponse : NetworkData
{
	public ResponseResult Response = ResponseResult.Success;

	public string Message = "";

	public Dictionary<long, ServerData> Servers = new Dictionary<long, ServerData>();

	public int BanPoints = 0;

	public string LastSignInTime = "";
}
