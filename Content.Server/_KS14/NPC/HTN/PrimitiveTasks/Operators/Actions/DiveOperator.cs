using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Actions;
using Content.Shared.Physics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;

/// <summary>
///     Dives towards <see cref="DestinationKey"/> with the world-targeted jump action <see cref="KsBaseActionOperator.Id"/>:
///         straight at it, as far as <see cref="MaxDistance"/>, stopping short of the first thing the owner would
///         crash into. Fails to plan if the owner has no such action, it is on cooldown, or the way is blocked within
///         <see cref="MinDistance"/> - a dive into a wall is not getting away - so make it optional.
/// </summary>
/// <remarks>
///     The jump goes all the way to its target, however far that is, which is why the target is worked out here and
///         is never the destination itself.
/// </remarks>
public sealed partial class DiveOperator : KsBaseActionOperator
{
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [DataField] public string DestinationKey = "RetreatTargetCoordinates";

    /// <summary>
    ///     Where the dive lands, worked out while planning.
    /// </summary>
    [DataField] public string Key = "DiveCoordinates";

    [DataField] public float MaxDistance = 3.5f;

    [DataField] public float MinDistance = 1.5f;

    /// <summary>
    ///     How far short of an obstacle the dive stops.
    /// </summary>
    [DataField] public float ObstacleMargin = 0.6f;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!TryGetValidAction(blackboard, out var ownerUid, out _) ||
            !blackboard.TryGetValue<EntityCoordinates>(DestinationKey, out var destinationCoordinates, EntityManager) ||
            !TryGetDivePoint(ownerUid, destinationCoordinates, out var diveCoordinates))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { Key, diveCoordinates },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(Key, out var diveCoordinates, EntityManager) ||
            !TryGetValidAction(blackboard, out var ownerUid, out var actionEntity) ||
            ActionsSystem.GetEvent(actionEntity) is not WorldTargetActionEvent worldTargetActionEvent)
            return HTNOperatorStatus.Failed;

        worldTargetActionEvent.Target = diveCoordinates;
        ActionsSystem.PerformAction(ownerUid, actionEntity, actionEvent: worldTargetActionEvent, predicted: false);

        return HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityCoordinates>(Key);
    }

    private bool TryGetDivePoint(EntityUid ownerUid, EntityCoordinates destinationCoordinates, out EntityCoordinates diveCoordinates)
    {
        diveCoordinates = default;

        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var destinationMapCoordinates = _transformSystem.ToMapCoordinates(destinationCoordinates);

        if (ownerMapCoordinates.MapId != destinationMapCoordinates.MapId)
            return false;

        var offset = destinationMapCoordinates.Position - ownerMapCoordinates.Position;
        var length = offset.Length();
        var distance = MathF.Min(length, MaxDistance);
        if (distance < MinDistance)
            return false;

        var direction = offset / length;
        var clearDistance = _npcLineOfSightSystem.GetClearDistance(ownerMapCoordinates,
            direction,
            distance + ObstacleMargin,
            (int)CollisionGroup.MobMask);

        distance = MathF.Min(distance, clearDistance - ObstacleMargin);
        if (distance < MinDistance)
            return false;

        diveCoordinates = _transformSystem.ToCoordinates(ownerMapCoordinates.Offset(direction * distance));
        return true;
    }
}
