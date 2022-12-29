using System;
using System.Collections.Generic;
using ZeroGravity.Math;

namespace ZeroGravity.Objects;

public class Station : SpaceObjectVessel
{
	private List<Vector3D> playerSpawnPoints = new List<Vector3D>();

	private List<Vector3D> shipSpawnPoints = new List<Vector3D>();

	public override SpaceObjectType ObjectType => SpaceObjectType.Station;

	public bool CanSpawnPlayers => playerSpawnPoints.Count > 0;

	public bool CanSpawnShips => playerSpawnPoints.Count > 0;

	public Station(long guid, bool initializeOrbit, Vector3D position, Vector3D velocity, Vector3D forward, Vector3D up)
		: base(guid, initializeOrbit, position, velocity, forward, up)
	{
	}

	~Station()
	{
	}

	public void PositionUpdated()
	{
	}

	public bool GetPlayerSpawnPoint(out Vector3D spawnPoint)
	{
		spawnPoint = Vector3D.Zero;
		if (CanSpawnPlayers)
		{
			spawnPoint = playerSpawnPoints[0];
			return true;
		}
		return false;
	}

	public override void RemovePlayerFromCrew(Player pl, bool checkDetails = false)
	{
		throw new NotImplementedException();
	}

	public override void AddPlayerToCrew(Player pl)
	{
		throw new NotImplementedException();
	}

	public override bool HasPlayerInCrew(Player pl)
	{
		throw new NotImplementedException();
	}
}
