using System.Collections.Generic;
using ZeroGravity.Data;
using ZeroGravity.Network;

namespace ZeroGravity;

public class PersistenceObjectDataAsteroid : PersistenceObjectData
{
	public OrbitData OrbitData;

	public string Name;

	public string Tag;

	public GameScenes.SceneId SceneID;

	public bool IsAlwaysVisible;

	public double[] Forward;

	public double[] Up;

	public double[] Rotation;

	public List<AsteroidMiningPointDetails> MiningPoints;
}
