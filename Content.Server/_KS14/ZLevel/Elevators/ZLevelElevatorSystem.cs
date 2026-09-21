using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
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
///     Server-only because all of it is: gibbing, tile destruction and reparenting a whole grid are none of
///         them things a client may guess at.
/// </remarks>
public sealed partial class ZLevelElevatorSystem : SharedZLevelElevatorSystem
{
    [Dependency] private ShuttleSystem _shuttleSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

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

    public override void Initialize()
    {
        base.Initialize();

        // Stays an explicit call: GridSplitEvent is a broadcast by-ref event, and the subscription generator
        //      reads a lone by-ref parameter as the non-ref EntityEventHandler, which does not compile.
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    /// <inheritdoc/>
    protected override bool TryCrossToZLevel(Entity<ZLevelElevatorComponent> entity, Entity<KsZLevelComponent> targetZLevel)
    {
        if (!_mapGridQuery.TryGetComponent(entity.Owner, out var gridComponent) ||
            !TryComp<MapComponent>(targetZLevel.Owner, out var targetMapComponent))
            return false;

        var transformComponent = Transform(entity.Owner);
        if (transformComponent.MapUid == targetZLevel.Owner)
            return true;

        var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

        // Reparenting wipes joints and can reset momentum, so anything the grid was carrying has to be put
        //      back by hand afterwards. An elevator is not meant to be moving under its own power, but it
        //      can be shoved, tugged by a docked shuttle or thrown about by an explosion, and a lift that
        //      quietly came to a dead stop every time it changed floor would be its own bug report.
        var hadPhysics = _physicsQuery.TryGetComponent(entity.Owner, out var physicsComponent);
        var linearVelocity = hadPhysics ? physicsComponent!.LinearVelocity : Vector2.Zero;
        var angularVelocity = hadPhysics ? physicsComponent!.AngularVelocity : 0f;

        // SetMapCoordinates special-cases grids: it parents them straight to the map rather than snapping
        //      them under whichever grid happens to occupy that spot, which is exactly what moving a whole
        //      grid wants. Everything anchored to or standing on the elevator rides along for free, because
        //      none of their own parents change.
        _transformSystem.SetMapCoordinates(
            entity.Owner,
            new MapCoordinates(worldPosition, targetMapComponent.MapId)
        );

        if (hadPhysics)
        {
            _physicsSystem.SetLinearVelocity(entity.Owner, linearVelocity, body: physicsComponent);
            _physicsSystem.SetAngularVelocity(entity.Owner, angularVelocity, body: physicsComponent);
        }

        ClearObstructingTiles((entity.Owner, gridComponent));
        Smimsh((entity.Owner, gridComponent));

        return !TerminatingOrDeleted(entity.Owner);
    }

    /// <inheritdoc/>
    protected override void FlattenArrival(Entity<ZLevelElevatorComponent> entity)
    {
        if (!_mapGridQuery.TryGetComponent(entity.Owner, out var gridComponent))
            return;

        Smimsh((entity.Owner, gridComponent));
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
    /// </remarks>
    private void ClearObstructingTiles(Entity<MapGridComponent> gridEntity)
    {
        var transformComponent = Transform(gridEntity.Owner);
        if (transformComponent.MapID == MapId.Nullspace ||
            !_fixturesQuery.TryGetComponent(gridEntity.Owner, out var fixturesComponent))
            return;

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

                _mapSystem.SetTile(otherGridEntity.Owner, otherGridEntity.Comp, tileIndices, Tile.Empty);
            }
        }
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
    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<ZLevelElevatorComponent>(args.Grid, out var elevatorComponent))
            return;

        StopElevator((args.Grid, elevatorComponent));
    }
}
