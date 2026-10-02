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
    ///     If set, also whether the order comes with a locker to open, or does not.
    /// </summary>
    [DataField] public bool? WithStorage;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!_npcSquadTacticsSystem.TryGetOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var order) ||
            Kinds.Count > 0 && !Kinds.Contains(order.Kind))
            return false;

        return WithStorage is not { } withStorage || (order.StorageUid != null) == withStorage;
    }
}
