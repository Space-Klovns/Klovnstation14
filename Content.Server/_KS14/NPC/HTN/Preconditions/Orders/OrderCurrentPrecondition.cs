using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Orders;

/// <summary>
///     Met while the order <c>GetOrderOperator</c> planned around is still the owner's order. Put it on every task
///         carrying out an order, with <c>recheckPreconditions: true</c>, so a new order is acted on the moment it
///         is given rather than once the old one is done.
/// </summary>
public sealed partial class OrderCurrentPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;

    [DataField] public string IdKey = "OrderId";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return blackboard.TryGetValue<int>(IdKey, out var plannedId, _entityManager) &&
            _npcSquadTacticsSystem.TryGetOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var order) &&
            order.Id == plannedId;
    }
}
