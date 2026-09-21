using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.ZLevel.Transit;
using Content.Server.Shuttles.Systems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel.Transit;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.ZLevel.Elevators;

/// <summary>
///     The half of the elevator that actually moves a grid between maps, and clears whatever was standing
///         where it landed.
/// </summary>
/// <remarks>
///     Server-only because all of it is: gibbing, tile destruction and making a map to cross are none of
///         them things a client may guess at. Moving the grid itself belongs to
///         <see cref="KsZLevelGapSystem"/>, which owns the maps between z-levels - an elevator is only one
///         of the things that might want to be on one.
/// </remarks>
public sealed partial class ZLevelElevatorSystem : SharedZLevelElevatorSystem
{
    [Dependency] private KsZLevelGapSystem _gapSystem = default!;
    [Dependency] private ShuttleSystem _shuttleSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    // Not readonly: FindGridsIntersecting takes it by ref so that it can grow or replace the list itself.
    private List<Entity<MapGridComponent>> _intersectingGrids = [];
    private readonly List<Vector2i> _obstructingTiles = [];
    private readonly List<EntityUid> _anchoredEntities = [];

    /// <summary>
    ///     How far inside its own bounds the footprint is measured, so that merely bordering a tile does
    ///         not count as standing on it.
    /// </summary>
    private const float BorderTolerance = 0.1f;

    /// <inheritdoc/>
    protected override bool TryEnterGap(
        Entity<ZLevelElevatorComponent> entity,
        Entity<KsZLevelComponent> lowerZLevel,
        Entity<KsZLevelComponent> upperZLevel,
        bool rising)
    {
        // An elevator already on a gap is one whose last leg was never tidied up. Refusing beats stranding
        //      the old gap map with a grid that has walked off it.
        if (TryGetGap(entity.Owner, out _))
            return false;

        if (!_gapSystem.TryEnterGap(
                entity.Owner,
                lowerZLevel,
                upperZLevel,
                progress: rising ? 0f : 1f,
                out _))
            return false;

        // Only once it is actually out of the way, so the floor it cut is never briefly laid back down
        //      underneath it.
        RestoreCutTiles(entity.Owner);
        return true;
    }

    /// <inheritdoc/>
    protected override bool TryLeaveGap(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> targetZLevel)
    {
        if (!TryGetGap(entity.Owner, out var gapEntity))
        {
            // Not on a gap at all - the map was swept out from under it, or it never left in the first
            //      place. Put it down directly rather than refusing, so it cannot be left in nullspace.
            return _gapSystem.TryMoveGridToMap(entity.Owner, targetZLevel.Owner);
        }

        return _gapSystem.TryLeaveGap(gapEntity.Value, targetZLevel);
    }

    /// <inheritdoc/>
    protected override void AbortLeg(Entity<ZLevelElevatorComponent> entity)
    {
        if (!TryGetGap(entity.Owner, out var gapEntity))
            return;

        if (!_gapSystem.TryGetNearestZLevel(gapEntity.Value, out var nearestZLevelEntity))
        {
            // Neither end of the gap is a z-level any more, so there is nowhere to put it down. Tearing the
            //      gap down takes the grid with it, which beats leaving a map nobody can reach.
            _gapSystem.DestroyGap(gapEntity.Value);
            return;
        }

        _gapSystem.TryLeaveGap(gapEntity.Value, nearestZLevelEntity.Value);
    }

    /// <inheritdoc/>
    protected override void SetLegProgress(Entity<ZLevelElevatorComponent> entity, float progress)
    {
        if (!TryGetGap(entity.Owner, out var gapEntity))
            return;

        _gapSystem.SetGapProgress(gapEntity.Value, progress);
    }

    /// <inheritdoc/>
    protected override void FlattenArrival(Entity<ZLevelElevatorComponent> entity)
    {
        if (!_mapGridQuery.TryGetComponent(entity.Owner, out var gridComponent))
            return;

        ClearObstructingTiles((entity.Owner, gridComponent));
        Smimsh((entity.Owner, gridComponent));
    }

    /// <summary>
    ///     The gap the elevator is currently crossing, if it is on one.
    /// </summary>
    private bool TryGetGap(EntityUid elevatorUid, [NotNullWhen(true)] out Entity<KsZLevelGapComponent>? gapEntity)
    {
        gapEntity = null;

        if (Transform(elevatorUid).MapUid is not { } mapUid ||
            !TryComp<KsZLevelGapComponent>(mapUid, out var gapComponent))
            return false;

        gapEntity = (mapUid, gapComponent);
        return true;
    }

    /// <summary>
    ///     Gibs and deletes whatever is loose in the shaft where the elevator now is.
    /// </summary>
    /// <remarks>
    ///     Reuses the shuttle arrival sweep rather than growing a crush of its own, because its scope is
    ///         already the right one: it queries the <em>map</em> broadphase, so it takes everything lying
    ///         loose in the open shaft and leaves alone anything parented to the elevator (passengers and
    ///         cargo), anything on another grid (the shaft walls and the landings) and anything marked
    ///         FTLSmashImmune.
    /// </remarks>
    private void Smimsh(Entity<MapGridComponent> gridEntity)
    {
        _shuttleSystem.Smimsh(gridEntity.Owner, grid: gridEntity.Comp);
    }

    /// <summary>
    ///     Cuts the elevator's footprint out of any grid it has arrived inside.
    /// </summary>
    /// <remarks>
    ///     An elevator shears through rather than refusing to move, so that a shaft a mapper forgot to cut -
    ///         or one a player has welded plating over to trap somebody - cannot stop it. Anchored entities
    ///         on the tiles being removed go with them, since they sit on another grid and so are out of
    ///         <see cref="Smimsh"/>'s reach.
    ///     What is cut is recorded and put back when the elevator leaves - see
    ///     <see cref="ZLevelElevatorCutTilesComponent"/> for why that is preferred over letting the two
    ///         floors overlap.
    /// </remarks>
    private void ClearObstructingTiles(Entity<MapGridComponent> gridEntity)
    {
        var transformComponent = Transform(gridEntity.Owner);
        if (transformComponent.MapID == MapId.Nullspace ||
            !_fixturesQuery.TryGetComponent(gridEntity.Owner, out var fixturesComponent))
            return;

        var cutComponent = EnsureComp<ZLevelElevatorCutTilesComponent>(gridEntity.Owner);

        // Shrunk a little, because an AABB touches the tiles it merely borders. A tile-aligned elevator's
        //      box ends exactly on the edge of the landing's floor, and clearing that would chew a tile off
        //      every landing it passed rather than only what is actually in the shaft.
        var worldAabb = _physicsSystem.GetWorldAABB(gridEntity.Owner, fixturesComponent).Enlarged(-BorderTolerance);

        _intersectingGrids.Clear();
        _mapSystem.FindGridsIntersecting(
            transformComponent.MapID,
            worldAabb,
            ref _intersectingGrids,
            approx: false,
            includeMap: false
        );

        foreach (var otherGridEntity in _intersectingGrids)
        {
            if (otherGridEntity.Owner == gridEntity.Owner)
                continue;

            // Gathered before anything is removed: SetTile can split a grid, which rewrites the very
            //      chunks the enumerator is walking.
            _obstructingTiles.Clear();
            var tileEnumerator = _mapSystem.GetTilesIntersecting(
                otherGridEntity.Owner,
                otherGridEntity.Comp,
                worldAabb,
                ignoreEmpty: true
            );

            while (tileEnumerator.MoveNext(out var tileRef))
                _obstructingTiles.Add(tileRef.GridIndices);

            foreach (var tileIndices in _obstructingTiles)
            {
                _anchoredEntities.Clear();
                _mapSystem.GetAnchoredEntities(otherGridEntity, tileIndices, _anchoredEntities);

                foreach (var anchoredUid in _anchoredEntities)
                {
                    if (!TerminatingOrDeleted(anchoredUid))
                        QueueDel(anchoredUid);
                }

                cutComponent.CutTiles.Add(new ZLevelElevatorCutTile(
                    otherGridEntity.Owner,
                    tileIndices,
                    _mapSystem.GetTileRef(otherGridEntity.Owner, otherGridEntity.Comp, tileIndices).Tile
                ));

                _mapSystem.SetTile(otherGridEntity.Owner, otherGridEntity.Comp, tileIndices, Tile.Empty);
            }
        }
    }

    /// <summary>
    ///     Puts back whatever the elevator cut to get where it was.
    /// </summary>
    /// <remarks>
    ///     A tile that has since been filled in by something else is left alone, so this can only ever undo
    ///         the elevator's own hole and never overwrite a floor somebody has laid in the meantime.
    /// </remarks>
    private void RestoreCutTiles(EntityUid elevatorUid)
    {
        if (!TryComp<ZLevelElevatorCutTilesComponent>(elevatorUid, out var cutComponent))
            return;

        foreach (var (gridUid, tileIndices, tile) in cutComponent.CutTiles)
        {
            if (TerminatingOrDeleted(gridUid) ||
                !_mapGridQuery.TryGetComponent(gridUid, out var gridComponent))
                continue;

            if (!_mapSystem.GetTileRef(gridUid, gridComponent, tileIndices).Tile.IsEmpty)
                continue;

            _mapSystem.SetTile(gridUid, gridComponent, tileIndices, tile);
        }

        cutComponent.CutTiles.Clear();
        RemComp<ZLevelElevatorCutTilesComponent>(elevatorUid);
    }

    /// <summary>
    ///     An elevator destroyed where it stood still has to leave the floor it cut behind it intact.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCutTilesShutdown(Entity<ZLevelElevatorCutTilesComponent> entity, ref ComponentShutdown args)
    {
        foreach (var (gridUid, tileIndices, tile) in entity.Comp.CutTiles)
        {
            if (TerminatingOrDeleted(gridUid) ||
                !_mapGridQuery.TryGetComponent(gridUid, out var gridComponent) ||
                !_mapSystem.GetTileRef(gridUid, gridComponent, tileIndices).Tile.IsEmpty)
                continue;

            _mapSystem.SetTile(gridUid, gridComponent, tileIndices, tile);
        }

        entity.Comp.CutTiles.Clear();
    }

    /// <summary>
    ///     An elevator cut in half stops being one elevator, so it stops being an elevator at all.
    /// </summary>
    /// <remarks>
    ///     Only one of the pieces keeps the component, and it is arbitrary which. Letting that piece carry
    ///         on its sweep would have half a lift travelling z-levels while the other half stayed behind on
    ///         the floor it was cut on, so the whole thing is brought to a halt instead and an admin or a
    ///         mapper can set it going again.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<ZLevelElevatorComponent>(args.Grid, out var elevatorComponent))
            return;

        StopElevator((args.Grid, elevatorComponent));
    }
}
