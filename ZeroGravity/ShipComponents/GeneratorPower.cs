using ZeroGravity.Data;
using ZeroGravity.Network;
using ZeroGravity.Objects;

namespace ZeroGravity.ShipComponents;

public class GeneratorPower : Generator
{
	public override GeneratorType Type => GeneratorType.Power;

	public override DistributionSystemType OutputType => DistributionSystemType.Power;

	public GeneratorPower(SpaceObjectVessel vessel, VesselObjectID id, GeneratorData genData)
		: base(vessel, id, genData)
	{
	}

	public override void Update(double duration)
	{
		base.Update(duration);
	}

	public override void SetAuxData(SystemAuxData auxData)
	{
	}
}
