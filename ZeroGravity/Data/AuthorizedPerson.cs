using System;

namespace ZeroGravity.Data;

[Serializable]
public class AuthorizedPerson : ISceneData
{
	public AuthorizedPersonRank Rank;

	public long PlayerGUID;

	public string PlayerNativeId;

	public string PlayerId;

	public string Name;
}
