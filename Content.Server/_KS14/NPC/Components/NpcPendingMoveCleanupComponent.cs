using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Attached by MoveToOperator when its shutdownState is Never, since in that case the normal HTN
///     task/plan shutdown hooks never call its ConditionalShutdown - the operator instead relies on
///     NpcMoveToCleanupSystem to remove its target/pathfind blackboard keys and unregister steering once the
///     movement it started actually concludes (arrives, fails to path, or gets taken over by something else
///     re-registering steering).
/// </summary>
[RegisterComponent]
public sealed partial class NpcPendingMoveCleanupComponent : Component
{
    public string TargetKey = default!;
    public string PathfindKey = default!;
    public bool RemoveKeyOnFinish;

    /// <summary>
    /// The coordinates we registered steering towards. If the NPC's current steering no longer points here,
    /// something else has taken over movement and we should only clean up our keys, not touch steering.
    /// </summary>
    public EntityCoordinates Coordinates;
}
