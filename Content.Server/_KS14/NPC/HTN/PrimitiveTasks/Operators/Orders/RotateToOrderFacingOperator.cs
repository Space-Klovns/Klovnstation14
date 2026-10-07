using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Interaction;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Orders;

/// <summary>
///     Turns the owner to face the way its current order says. An order's facing is relative to the grid its spot is
///         on, not to the world (see <see cref="NpcOrder.Facing"/>), so it is put into the world here, every update:
///         a member told to face into a room on a shuttle still faces into it after the shuttle has turned.
///         Fails if the owner no longer has the order this plan was made for.
/// </summary>
public sealed partial class RotateToOrderFacingOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;
    [Dependency] private RotateToFaceSystem _rotateToFaceSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [DataField] public string IdKey = "OrderId";

    [DataField] public string RotateSpeedKey = NPCBlackboard.RotateSpeed;

    [DataField] public Angle Tolerance = Angle.FromDegrees(1);

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<int>(IdKey, out var plannedId, _entityManager) ||
            !_npcSquadTacticsSystem.TryGetOrder(ownerUid, out var order) ||
            order.Id != plannedId ||
            !blackboard.TryGetValue<float>(RotateSpeedKey, out var rotateSpeed, _entityManager))
            return HTNOperatorStatus.Failed;

        var worldFacing = _entityManager.EntityExists(order.Coordinates.EntityId)
            ? order.Facing + _transformSystem.GetWorldRotation(order.Coordinates.EntityId)
            : order.Facing;

        return _rotateToFaceSystem.TryRotateTo(ownerUid, worldFacing, frameTime, Tolerance, rotateSpeed)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Continuing;
    }
}
