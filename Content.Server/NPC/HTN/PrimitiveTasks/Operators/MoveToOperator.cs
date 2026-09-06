using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
/// Moves an NPC to the specified target key. Hands the actual steering off to NPCSystem.Steering
/// </summary>
public sealed partial class MoveToOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;
    private NPCSteeringSystem _steering = default!;
    private PathfindingSystem _pathfind = default!;
    private SharedTransformSystem _transform = default!;
    [Dependency] private Interaction.InteractionSystem _interactionSystem = default!; // KS14

    /// <summary>
    /// When to shut the task down.
    /// </summary>
    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>
    /// Should we assume the MovementTarget is reachable during planning or should we pathfind to it?
    /// </summary>
    [DataField("pathfindInPlanning")]
    public bool PathfindInPlanning = true;

    /// <summary>
    /// When we're finished moving to the target should we remove its key?
    /// </summary>
    [DataField("removeKeyOnFinish")]
    public bool RemoveKeyOnFinish = true;

    /// <summary>
    /// Target Coordinates to move to. This gets removed after execution.
    /// </summary>
    [DataField("targetKey")]
    public string TargetKey = "TargetCoordinates";

    /// <summary>
    /// Where the pathfinding result will be stored (if applicable). This gets removed after execution.
    /// </summary>
    [DataField("pathfindKey")]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    /// <summary>
    /// How close we need to get before considering movement finished.
    /// </summary>
    [DataField("rangeKey")]
    public string RangeKey = "MovementRange";

    /// <summary>
    /// Do we only need to move into line of sight.
    /// </summary>
    [DataField("stopOnLineOfSight")]
    public bool StopOnLineOfSight;

    // KS14
    /// <summary>
    ///     By default, when this is false, an NPC will be considered as
    ///         moved to its target if both are simply in range of each other.
    ///
    ///     If this is true, this check will also need to be in LOS to be considered as in range.
    /// </summary>
    [DataField]
    public bool RequireLosForRangeCheck;

    // KS14
    [DataField]
    public bool WeAreAtTargetCoordsAfterSuccessfullyPlanning = true;

    private const string MovementCancelToken = "MovementCancelToken";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfind = sysManager.GetEntitySystem<PathfindingSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var targetCoordinates, _entManager))
        {
            return (false, null);
        }

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var xform) ||
            !_entManager.TryGetComponent<PhysicsComponent>(owner, out var body))
            return (false, null);

        if (!_entManager.TryGetComponent<MapGridComponent>(xform.GridUid, out var ownerGrid) ||
            !_entManager.TryGetComponent<MapGridComponent>(_transform.GetGrid(targetCoordinates), out var targetGrid))
        {
            return (false, null);
        }

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        // KS14: ANK: start: Reworked this if statement
        var ownerMapCoords = _transform.GetMapCoordinates(xform);
        var targetMapCoords = _transform.ToMapCoordinates(targetCoordinates);
        var distance = (targetMapCoords.Position - ownerMapCoords.Position).Length();

        if (RequireLosForRangeCheck ? _interactionSystem.InRangeUnobstructed(ownerMapCoords, targetMapCoords, range: range) : distance <= range)
        // KS14: ANK: end
        {
            // In range
            // KS14 start: only report OwnerCoordinates as an effect when WeAreAtTargetCoordsAfterSuccessfullyPlanning,
            // since other tasks in the plan may rely on OwnerCoordinates not being set as if we'd reached the target
            var inRangeEffects = new Dictionary<string, object>();
            if (WeAreAtTargetCoordsAfterSuccessfullyPlanning)
                inRangeEffects[NPCBlackboard.OwnerCoordinates] = blackboard.GetValueOrDefault<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, _entManager);
            return (true, inRangeEffects);
            // KS14 end
        }

        if (!PathfindInPlanning)
        {
            // KS14 start: only report OwnerCoordinates as an effect when WeAreAtTargetCoordsAfterSuccessfullyPlanning
            var noPathfindEffects = new Dictionary<string, object>();
            if (WeAreAtTargetCoordsAfterSuccessfullyPlanning)
                noPathfindEffects[NPCBlackboard.OwnerCoordinates] = targetCoordinates;
            return (true, noPathfindEffects);
            // KS14 end
        }

        var path = await _pathfind.GetPath(
            blackboard.GetValue<EntityUid>(NPCBlackboard.Owner),
            xform.Coordinates,
                targetCoordinates,
            RequireLosForRangeCheck ? MathF.Min(_interactionSystem.UnobstructedDistance(ownerMapCoords, targetMapCoords), range) : range, // KS14: RequireLosForRangeCheck
            cancelToken,
            _pathfind.GetFlags(blackboard));

        if (path.Result != PathResult.Path)
        {
            return (false, null);
        }

        // KS14 start: only report OwnerCoordinates as an effect when WeAreAtTargetCoordsAfterSuccessfullyPlanning
        var pathEffects = new Dictionary<string, object>
        {
            {PathfindKey, path}
        };
        if (WeAreAtTargetCoordsAfterSuccessfullyPlanning)
            pathEffects[NPCBlackboard.OwnerCoordinates] = targetCoordinates;

        return (true, pathEffects);
        // KS14 end
    }

    // Given steering is complicated we'll hand it off to a dedicated system rather than this singleton operator.

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        // Need to remove the planning value for execution.
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);
        var targetCoordinates = blackboard.GetValue<EntityCoordinates>(TargetKey);
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // Re-use the path we may have if applicable.
        var comp = _steering.Register(uid, targetCoordinates);
        comp.ArriveOnLineOfSight = StopOnLineOfSight;

        // KS14: ANK: add RequireLosForRangeCheck
        comp.Range = RequireLosForRangeCheck ? 0f : blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        if (blackboard.TryGetValue<PathResultEvent>(PathfindKey, out var result, _entManager))
        {
            if (blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OwnerCoordinates, out var coordinates, _entManager))
            {
                var mapCoords = _transform.ToMapCoordinates(coordinates);
                _steering.PrunePath(uid, mapCoords, _transform.ToMapCoordinates(targetCoordinates).Position - mapCoords.Position, result.Path);
            }

            comp.CurrentPath = new Queue<PathPoly>(result.Path);
        }

        // KS14: ANK: Never means ConditionalShutdown will never run via the HTN task/plan lifecycle (that's
        // the point - the movement survives task/plan transitions), so nothing would otherwise remove
        // TargetKey/PathfindKey or unregister steering once we actually arrive. NpcMoveToCleanupSystem
        // watches for the steering we just registered to stop and finishes that cleanup itself.
        if (ShutdownState == HTNPlanState.Never)
        {
            var cleanup = _entManager.EnsureComponent<NpcPendingMoveCleanupComponent>(uid);
            cleanup.TargetKey = TargetKey;
            cleanup.PathfindKey = PathfindKey;
            cleanup.RemoveKeyOnFinish = RemoveKeyOnFinish;
            cleanup.Coordinates = targetCoordinates;
        }
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<NPCSteeringComponent>(owner, out var steering))
            return HTNOperatorStatus.Failed;

        // KS14: ANK: start
        if (RequireLosForRangeCheck)
        {
            var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
            steering.Range = _interactionSystem.InRangeUnobstructed(owner, steering.Coordinates, range: range) ?
                range :
                0f;
        }
        // KS14: ANK: end

        // Just keep moving in the background and let the other tasks handle it.
        if ((ShutdownState == HTNPlanState.PlanFinished || ShutdownState == HTNPlanState.Never /* KS14: ANK: include Never */) && steering.Status == SteeringStatus.Moving)
        {
            return HTNOperatorStatus.Finished;
        }

        return steering.Status switch
        {
            SteeringStatus.InRange => HTNOperatorStatus.Finished,
            SteeringStatus.NoPath => HTNOperatorStatus.Failed,
            SteeringStatus.Moving => HTNOperatorStatus.Continuing,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        // Cleanup the blackboard and remove steering.
        if (blackboard.TryGetValue<CancellationTokenSource>(MovementCancelToken, out var cancelToken, _entManager))
        {
            cancelToken.Cancel();
            blackboard.Remove<CancellationTokenSource>(MovementCancelToken);
        }

        // OwnerCoordinates is only used in planning so dump it.
        blackboard.Remove<PathResultEvent>(PathfindKey);

        if (RemoveKeyOnFinish)
        {
            blackboard.Remove<EntityCoordinates>(TargetKey);
        }

        _steering.Unregister(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
