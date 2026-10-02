using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Orders;

/// <summary>
///     Writes the owner's current order (see <see cref="NpcSquadTacticsSystem"/>) to the blackboard: where to go, how
///         close counts, the locker to open if there is one, and the order's id - which <c>OrderCurrentPrecondition</c>
///         compares against, to drop the plan the moment the order changes. Fails if the owner has no order.
/// </summary>
/// <remarks>
///     Not which way to face: that is relative to the order's grid, and only means anything put into the world at the
///         moment of turning. <c>RotateToOrderFacingOperator</c> reads it from the order itself.
/// </remarks>
public sealed partial class GetOrderOperator : HTNOperator
{
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;

    [DataField] public string CoordinatesKey = "OrderCoordinates";

    [DataField] public string RangeKey = "OrderRange";

    [DataField] public string StorageKey = "OrderStorage";

    [DataField] public string IdKey = "OrderId";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_npcSquadTacticsSystem.TryGetOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var order))
            return (false, null);

        var effects = new Dictionary<string, object>
        {
            { CoordinatesKey, order.Coordinates },
            { RangeKey, order.Range },
            { IdKey, order.Id },
        };

        if (order.StorageUid is { } storageUid)
            effects[StorageKey] = storageUid;

        return (true, effects);
    }
}
