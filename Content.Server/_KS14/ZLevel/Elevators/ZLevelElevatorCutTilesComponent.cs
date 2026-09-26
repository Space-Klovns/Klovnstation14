using Robust.Shared.Map;

namespace Content.Server._KS14.ZLevel.Elevators;

/// <summary>
///     The tiles an elevator cut its way through to reach the floor it is parked on, so that they can be put
///         back when it leaves.
/// </summary>
/// <remarks>
///     An elevator shears through whatever is in its footprint rather than refusing to move, which is what
///         stops a welded-over shaft from trapping anyone. Left at that, a shaft with a floor at the bottom -
///         which is the ordinary way to map a pit, rather than leaving it open to space - is permanently
///         destroyed the first time the lift goes down.
///     Putting the tiles back on departure is deliberately preferred over letting the lift simply sit on top
///         of them. Two grids occupying the same tile collide, and
///         <see cref="Robust.Shared.GameObjects.SharedGridTraversalSystem"/> parents anything standing there
///         to whichever grid it finds first, which is not ordered - so a passenger could be parented to the
///         pit floor instead of to the lift and be left behind when it rose. Cutting and restoring means
///         there is never more than one floor in a tile.
///     Server-only: the cut and the restore are both tile mutation, which a client may not do.
/// </remarks>
[RegisterComponent]
public sealed partial class ZLevelElevatorCutTilesComponent : Component
{
    /// <summary>
    ///     What was removed, and from where.
    /// </summary>
    /// <remarks>
    ///     Anchored entities that were on those tiles are <em>not</em> recorded: they were deleted, and
    ///         bringing back a wall someone welded over a shaft - or the grille the lift tore out - would be
    ///         restoring the obstruction rather than the floor.
    ///     Restoring is best effort. A grid whose last tile the elevator cut is deleted outright by the
    ///         engine, so there is nothing left to put a floor back on - map a shaft pit as part of the
    ///         station grid rather than as a grid of its own, which is the ordinary way round anyway.
    /// </remarks>
    [ViewVariables]
    public readonly List<ZLevelElevatorCutTile> CutTiles = [];
}

/// <summary>
///     One tile an elevator removed to make room for itself.
/// </summary>
public readonly record struct ZLevelElevatorCutTile(EntityUid GridUid, Vector2i Indices, Tile Tile);
