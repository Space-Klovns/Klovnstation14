using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Interaction;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Hiding;

/// <summary>
///     Gets into the closet at <see cref="TargetKey"/>, shutting it behind itself, to hide (see <see cref="NpcHidingSystem"/>):
///         opens it, steps onto it, and shuts it, as anyone does. Fails if it is out of reach, it turns out not to be one
///         it could get out of again, it cannot get onto it in time, or a hostile is in earshot, as one would have it
///         come straight back out (<see cref="NpcHidingSystem.IsHostileInEarshot"/>).
/// </summary>
/// <remarks>
///     It was put in from where it stood, up to a tile and a half off: seen to jump into a shut closet.
/// </remarks>
public sealed partial class EnterEntityStorageOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcHidingSystem _npcHidingSystem = default!;
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     Set while this task steers it onto the closet, so <see cref="TaskShutdown"/> stops only steering it started; and
    ///         when it gives up getting there.
    /// </summary>
    private const string SteppingInUntilKey = "EnterStorageSteppingInUntil";

    /// <summary>
    ///     How near the closet's middle, in tiles, it has to be to shut itself in: about where shutting it takes in a mob
    ///         stood there (<c>EntityStorageComponent.EnteringRange</c>, against the mob's own size).
    /// </summary>
    private const float StepInRange = 0.45f;

    /// <summary>
    ///     How long it gets to step onto it, once it is open.
    /// </summary>
    private static readonly TimeSpan StepInTime = TimeSpan.FromSeconds(2);

    [DataField(required: true)]
    public string TargetKey = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return (blackboard.TryGetEntity(TargetKey, _entityManager, out var storageUid) &&
            _npcHidingSystem.IsHidingPlace(storageUid) &&
            !_npcHidingSystem.IsHostileInEarshot(blackboard.GetOwner()), null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetEntity(TargetKey, _entityManager, out var storageUid))
            return HTNOperatorStatus.Failed;

        var ownerUid = blackboard.GetOwner();

        if (!_interactionSystem.InRangeUnobstructed(ownerUid, storageUid) ||
            !_npcHidingSystem.IsHidingPlace(storageUid) ||
            _npcHidingSystem.IsHostileInEarshot(ownerUid))
            return HTNOperatorStatus.Failed;

        // First the door, then onto it, then shut it.
        if (!_entityStorageSystem.IsOpen(storageUid))
            return _entityStorageSystem.TryOpenStorage(ownerUid, storageUid, silent: true) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;

        var ownerPosition = _transformSystem.GetWorldPosition(ownerUid);
        var storagePosition = _transformSystem.GetWorldPosition(storageUid);

        if ((ownerPosition - storagePosition).Length() > StepInRange)
        {
            var now = _gameTiming.CurTime;

            if (!blackboard.TryGetValue<TimeSpan>(SteppingInUntilKey, out var until, _entityManager))
            {
                var steeringComponent = _npcSteeringSystem.Register(ownerUid, new EntityCoordinates(storageUid, System.Numerics.Vector2.Zero));
                steeringComponent.Range = StepInRange * 0.65f; // short of it is still in range: no circling a point it overshoots
                steeringComponent.Flags = _pathfindingSystem.GetFlags(blackboard);
                blackboard.SetValue(SteppingInUntilKey, now + StepInTime);
                return HTNOperatorStatus.Continuing;
            }

            return now < until ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
        }

        StopStepping(blackboard);
        return _npcHidingSystem.TryHideIn(ownerUid, storageUid) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        StopStepping(blackboard);
    }

    private void StopStepping(NPCBlackboard blackboard)
    {
        if (blackboard.Remove<TimeSpan>(SteppingInUntilKey))
            _npcSteeringSystem.Unregister(blackboard.GetOwner());
    }
}
