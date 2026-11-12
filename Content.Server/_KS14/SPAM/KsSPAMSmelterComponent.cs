using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SPAM;

/// <summary>
///     A machine that pays mining points for the ore put into it. The points wait on the machine until a salvager
///         claims them with their ID or S.P.A.M.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSPAMSmelterSystem))]
public sealed partial class KsSPAMSmelterComponent : Component
{
    /// <summary>
    ///     Mining points for one unit of each kind of ore. Ore that isn't listed pays nothing.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<StackPrototype>, float> PointsPerUnit = [];

    /// <summary>
    ///     Points nobody has claimed yet.
    /// </summary>
    [DataField]
    public float Unclaimed;
}
