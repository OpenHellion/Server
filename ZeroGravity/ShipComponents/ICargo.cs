using System.Collections.Generic;
using ZeroGravity.Data;

namespace ZeroGravity.ShipComponents;

public interface ICargo
{
	List<CargoCompartmentData> Compartments { get; }

	CargoCompartmentData GetCompartment(int? id = null);

	float ChangeQuantityBy(int compartmentID, ResourceType resourceType, float quantity, bool wholeAmount = false);
}
