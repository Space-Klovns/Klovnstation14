using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     Finishes MoveToOperator's cleanup for movement tasks that opted out of the normal HTN task/plan
///     shutdown hooks via shutdownState: Never (background movement that survives task/plan transitions),
///     once the steering they started has actually stopped - either it arrived/failed to path, or something
///     else took over steering for the NPC. See <see cref="NpcPendingMoveCleanupComponent"/>.
/// </summary>
public sealed partial class NpcMoveToCleanupSystem : EntitySystem
{
    [Dependency] private NPCSteeringSystem _steeringSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Removed after the loop rather than deferred: MoveToOperator ensures this component for the next move, and later
        //      in the same tick it would get back the one deferred for removal, and lose the new move's cleanup with it.
        var finished = new ValueList<EntityUid>();
        var query = EntityQueryEnumerator<NpcPendingMoveCleanupComponent, HTNComponent>();

        while (query.MoveNext(out var uid, out var cleanupComponent, out var htnComponent))
        {
            var entity = new Entity<NpcPendingMoveCleanupComponent, HTNComponent>(uid, cleanupComponent, htnComponent);

            if (!TryComp<NPCSteeringComponent>(uid, out var steering) ||
                // Something else re-registered steering over ours - leave it alone, just clean up our keys.
                !steering.Coordinates.Equals(cleanupComponent.Coordinates))
            {
                Finish(entity, unregister: false);
                finished.Add(uid);
                continue;
            }

            if (steering.Status == SteeringStatus.Moving)
                continue;

            Finish(entity, unregister: true);
            finished.Add(uid);
        }

        foreach (var uid in finished)
        {
            RemComp<NpcPendingMoveCleanupComponent>(uid);
        }
    }

    /// <summary>
    ///     Clears the move's keys, and stops its steering if asked. The cleanup itself is removed by the caller.
    /// </summary>
    private void Finish(Entity<NpcPendingMoveCleanupComponent, HTNComponent> entity, bool unregister)
    {
        var blackboard = entity.Comp2.Blackboard;
        blackboard.Remove<PathResultEvent>(entity.Comp1.PathfindKey);

        if (entity.Comp1.RemoveKeyOnFinish)
            blackboard.Remove<EntityCoordinates>(entity.Comp1.TargetKey);

        if (unregister)
            _steeringSystem.Unregister(entity);
    }
}
