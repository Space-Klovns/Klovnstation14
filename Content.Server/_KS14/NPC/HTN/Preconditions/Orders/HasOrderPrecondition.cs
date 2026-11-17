using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Orders;

/// <summary>
///     Met when the owner has an order of one of <see cref="Kinds"/> (see <see cref="NpcSquadTacticsSystem"/>), or any
///         order if none are given, and not of one of <see cref="ExceptKinds"/>.
/// </summary>
/// <example>
///     <code>
///     - !type:HasOrderPrecondition # no order, or only to hold still or to look
///       exceptKinds: [ Watch, Investigate ]
///       invert: true
///     </code>
/// </example>
public sealed partial class HasOrderPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;

    [DataField] public List<NpcOrderKind> Kinds = new();

    /// <summary>
    ///     Orders of these kinds do not count. With <c>invert</c>, the way to say "no order but these": a list that does
    ///         not have to grow with every new kind of order, as one of everything else would.
    /// </summary>
    [DataField] public List<NpcOrderKind> ExceptKinds = new();

    /// <summary>
    ///     If set, also whether the order is about something - a locker to open, a door to force - or is not.
    /// </summary>
    [DataField] public bool? WithTarget;

    /// <summary>
    ///     If set, also whether the order comes before fighting. See <see cref="NpcOrder.OverCombat"/>.
    /// </summary>
    [DataField] public bool? OverCombat;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!_npcSquadTacticsSystem.TryGetOrder(blackboard.GetOwner(), out var order) ||
            Kinds.Count > 0 && !Kinds.Contains(order.Kind) ||
            ExceptKinds.Contains(order.Kind) ||
            OverCombat is { } overCombat && order.OverCombat != overCombat)
            return false;

        return WithTarget is not { } withTarget || (order.TargetUid != null) == withTarget;
    }
}
