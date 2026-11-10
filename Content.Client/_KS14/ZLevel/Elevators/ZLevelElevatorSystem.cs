using Content.Client._KS14.ZLevel.Transit;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel.Transit;

namespace Content.Client._KS14.ZLevel.Elevators;

/// <summary>
///     Moves the elevator through the gap it is crossing, every frame, so the ride looks like a ride.
/// </summary>
/// <remarks>
///     Nothing else here is client work. The shared system recomputes the leg's progress from the clock on
///         this side, and everything the ride looks like falls out of pushing that onto the gap map: the
///         viewport draws the gap as the viewer's own z-level - unscaled, through their real eye, so the
///         platform stays fixed underfoot - and works out the depth to everything below it by resolving the
///         gap to the z-level it is anchored to.
///     Only the progress is driven here, and not the entering or leaving: a client that made its own gap map
///         would be inventing a place the server has not agreed exists.
/// </remarks>
public sealed partial class ZLevelElevatorSystem : SharedZLevelElevatorSystem
{
    [Dependency] private KsZLevelGapSystem _gapSystem = default!;

    [Dependency] private EntityQuery<KsZLevelGapComponent> _gapQuery = default!;

    /// <inheritdoc/>
    protected override void SetLegProgress(Entity<ZLevelElevatorComponent> entity, float progress)
    {
        // Replicated, so the gap may simply not have arrived yet - the elevator's own state can be applied
        //      a state before the map it is riding is. There is nothing to move until it does.
        if (Transform(entity.Owner).MapUid is not { } mapUid ||
            !_gapQuery.TryGetComponent(mapUid, out var gapComponent))
            return;

        _gapSystem.SetGapProgress((mapUid, gapComponent), progress);
    }
}
