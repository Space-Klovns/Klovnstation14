using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Orders;

/// <summary>
///     Met when the owner has an order of one of <see cref="Kinds"/> (see <see cref="NpcSquadTacticsSystem"/>), or any
///         order if none are given.
/// </summary>
public sealed partial class HasOrderPrecondition : HTNPrecondition
{
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;

    [DataField] public List<NpcOrderKind> Kinds = new();

    /// <summary>
    ///     If set, also whether the order is about something - a locker to open, a door to force - or is not.
    /// </summary>
    [DataField] public bool? WithTarget;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!_npcSquadTacticsSystem.TryGetOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var order) ||
            Kinds.Count > 0 && !Kinds.Contains(order.Kind))
            return false;

        return WithTarget is not { } withTarget || (order.TargetUid != null) == withTarget;
    }
}
