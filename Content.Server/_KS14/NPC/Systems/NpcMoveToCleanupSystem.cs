using Content.Server._KS14.NPC.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
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

        var query = EntityQueryEnumerator<NpcPendingMoveCleanupComponent, HTNComponent>();

        while (query.MoveNext(out var uid, out var cleanupComponent, out var htnComponent))
        {
            if (!TryComp<NPCSteeringComponent>(uid, out var steering))
            {
                Finish(uid, cleanupComponent, htnComponent, unregister: false);
                continue;
            }

            if (!steering.Coordinates.Equals(cleanupComponent.Coordinates))
            {
                // Something else re-registered steering over ours - leave it alone, just clean up our keys.
                Finish(uid, cleanupComponent, htnComponent, unregister: false);
                continue;
            }

            if (steering.Status == SteeringStatus.Moving)
                continue;

            Finish(uid, cleanupComponent, htnComponent, unregister: true);
        }
    }

    private void Finish(EntityUid uid, NpcPendingMoveCleanupComponent cleanup, HTNComponent htnComponent, bool unregister)
    {
        var blackboard = htnComponent.Blackboard;
        blackboard.Remove<PathResultEvent>(cleanup.PathfindKey);

        if (cleanup.RemoveKeyOnFinish)
            blackboard.Remove<EntityCoordinates>(cleanup.TargetKey);

        if (unregister)
            _steeringSystem.Unregister(uid);

        RemComp<NpcPendingMoveCleanupComponent>(uid);
    }
}
