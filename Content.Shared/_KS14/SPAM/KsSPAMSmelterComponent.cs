using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.SPAM;

/// <summary>
///     A machine that pays mining points for the ore put into it. The points wait on the machine until a salvager
///         claims them with their ID or S.P.A.M., and its window shows them where a lathe shows its queue.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
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
    [DataField, AutoNetworkedField]
    public float Unclaimed;
}
