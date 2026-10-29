using System.Numerics;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Fluids.EntitySystems;
using Content.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Firefighting;

/// <summary>
///     How an NPC fights a chemical fire: which tile to walk to, and which to spray once it is there. See
///         <see cref="KsNpcFirefighterComponent"/>.
/// </summary>
/// <remarks>
///     A burning puddle is only out for good once it has cooled, so a tile stays a target after its flames die for as
///         long as its puddle is hot and its foam is thin. On a large spill the NPC sprays the tiles in its reach, nearest
///         edge first, and walks on to the next once nothing it can reach needs it.
/// </remarks>
public sealed partial class KsNpcFirefightingSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedKsFoamBlanketSystem _foamBlanketSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedSpraySystem _spraySystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private EntityQuery<KsNpcFirefighterComponent> _firefighterQuery = default!;
    [Dependency] private EntityQuery<SprayComponent> _sprayQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<ChemicalFireGridComponent> _chemicalFireGridQuery = default!;
    [Dependency] private EntityQuery<PuddleComponent> _puddleQuery = default!;

    /// <summary>
    ///     A tile counts as on the line of a spray when its centre is within this far of it, in tiles.
    /// </summary>
    private const float SprayLineHalfWidth = 0.5f;

    private readonly List<Vector2i> _targetTiles = new();
    private readonly List<Vector2i> _expiredTiles = new();
    private readonly List<SprayTarget> _tilesInReach = new();

    /// <param name="Weight">What spraying the tile is worth: more if it is burning.</param>
    private readonly record struct SprayTarget(Vector2i Tile, Vector2 Center, float Distance, float Weight);

    /// <summary>
    ///     Picks the tile the NPC should go to: the nearest that needs it. Also gives how close it should get.
    /// </summary>
    public bool TryPickTarget(EntityUid uid, out EntityCoordinates targetCoordinates, out float approachRange)
    {
        targetCoordinates = default;
        approachRange = 0f;

        if (!_firefighterQuery.TryComp(uid, out var firefighterComponent) ||
            !_sprayQuery.TryComp(uid, out var sprayComponent))
            return false;

        var entity = new Entity<KsNpcFirefighterComponent>(uid, firefighterComponent);
        Scan(entity);

        if (entity.Comp.WatchedGrid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var gridComponent))
            return false;

        var ownPosition = GetLocalPosition(uid, gridUid);

        GetTargetTiles(entity, (gridUid, gridComponent), _targetTiles);

        var bestDistance = float.MaxValue;
        Vector2i? bestTile = null;
        foreach (var tile in _targetTiles)
        {
            var distance = (TileCenter(tile, gridComponent) - ownPosition).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestTile = tile;
        }

        if (bestTile is not { } targetTile)
            return false;

        targetCoordinates = new EntityCoordinates(gridUid, TileCenter(targetTile, gridComponent));
        approachRange = sprayComponent.SprayDistance * entity.Comp.ApproachFraction;
        return true;
    }

    /// <summary>
    ///     Sprays the best tile in reach. Returns false once nothing in reach needs spraying, so the NPC moves on.
    /// </summary>
    public bool UpdateSpraying(EntityUid uid)
    {
        if (!_firefighterQuery.TryComp(uid, out var firefighterComponent) ||
            !_sprayQuery.TryComp(uid, out var sprayComponent))
            return false;

        var entity = new Entity<KsNpcFirefighterComponent>(uid, firefighterComponent);
        if (_gameTiming.CurTime >= entity.Comp.NextScanTime)
        {
            entity.Comp.NextScanTime = _gameTiming.CurTime + entity.Comp.ScanInterval;
            Scan(entity);
        }

        if (entity.Comp.WatchedGrid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var gridComponent))
            return false;

        var ownPosition = GetLocalPosition(uid, gridUid);

        Entity<MapGridComponent> grid = (gridUid, gridComponent);
        GetTargetTiles(entity, grid, _targetTiles);

        var reach = sprayComponent.SprayDistance;
        var lineHalfWidth = SprayLineHalfWidth * gridComponent.TileSize;

        // Only tiles in reach can be aimed at or lie on a line to one, so the rest of a large spill is left out of the
        //      scoring below, which compares every pair. Centres and burning are worked out once per tile here.
        _tilesInReach.Clear();
        foreach (var targetTile in _targetTiles)
        {
            var center = TileCenter(targetTile, gridComponent);
            var distance = (center - ownPosition).Length();
            if (distance <= reach + lineHalfWidth)
                _tilesInReach.Add(new SprayTarget(targetTile, center, distance, IsBurning(grid, targetTile) ? 2f : 1f));
        }

        Vector2i? aimTile = null;
        var bestScore = 0f;
        var bestDistance = float.MaxValue;
        foreach (var candidate in _tilesInReach)
        {
            if (candidate.Distance > reach)
                continue;

            // A spray foams every tile it crosses, so aim where the line from here covers the most: burning tiles first.
            var score = 0f;
            foreach (var other in _tilesInReach)
            {
                if (DistanceToSegment(other.Center, ownPosition, candidate.Center) <= lineHalfWidth)
                    score += other.Weight;
            }

            if (score < bestScore || score == bestScore && candidate.Distance >= bestDistance)
                continue;

            bestScore = score;
            bestDistance = candidate.Distance;
            aimTile = candidate.Tile;
        }

        if (aimTile is not { } tile)
            return false;

        // Waiting out the spray's delay, or for its tank to refill, still counts as fighting this fire.
        if (_useDelaySystem.IsDelayed(uid))
            return true;

        var aimCoordinates = _transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, TileCenter(tile, gridComponent)));
        _spraySystem.Spray((uid, sprayComponent), aimCoordinates, user: uid);
        return true;
    }

    /// <summary>
    ///     Adds every fire the NPC can see to its watch, and forgets tiles that no longer need it.
    /// </summary>
    private void Scan(Entity<KsNpcFirefighterComponent> entity)
    {
        var transformComponent = EntityManager.TransformQuery.GetComponent(entity);
        if (transformComponent.GridUid != entity.Comp.WatchedGrid)
        {
            entity.Comp.WatchedTiles.Clear();
            entity.Comp.WatchedGrid = transformComponent.GridUid;
        }

        if (entity.Comp.WatchedGrid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var gridComponent))
            return;

        Entity<MapGridComponent> grid = (gridUid, gridComponent);
        var now = _gameTiming.CurTime;

        if (_chemicalFireGridQuery.TryComp(gridUid, out var chemicalFireGridComponent))
        {
            var ownPosition = GetLocalPosition(entity, gridUid);
            var searchRange = entity.Comp.SearchRange;
            foreach (var tile in chemicalFireGridComponent.Tiles.Keys)
            {
                var tileCenter = TileCenter(tile, gridComponent);
                if ((tileCenter - ownPosition).Length() > searchRange || !IsBurning(grid, tile))
                    continue;

                // Tiles already watched stay so through walls; a new one has to be seen.
                if (!entity.Comp.WatchedTiles.ContainsKey(tile) &&
                    !_examineSystem.InRangeUnOccluded(entity.Owner, new EntityCoordinates(gridUid, tileCenter), searchRange))
                    continue;

                entity.Comp.WatchedTiles[tile] = now;
            }
        }

        _expiredTiles.Clear();
        foreach (var (tile, lastBurning) in entity.Comp.WatchedTiles)
        {
            var sinceBurning = now - lastBurning;
            if (sinceBurning > entity.Comp.MaxWatchTime ||
                sinceBurning > entity.Comp.VigilTime && !IsHotPuddle(entity, grid, tile))
                _expiredTiles.Add(tile);
        }

        foreach (var tile in _expiredTiles)
            entity.Comp.WatchedTiles.Remove(tile);
    }

    /// <summary>
    ///     The watched tiles that need spraying now: those burning, and those whose hot puddle has too little foam on it.
    /// </summary>
    private void GetTargetTiles(Entity<KsNpcFirefighterComponent> entity, Entity<MapGridComponent> grid, List<Vector2i> targetTiles)
    {
        targetTiles.Clear();
        foreach (var tile in entity.Comp.WatchedTiles.Keys)
        {
            if (IsBurning(grid, tile) || IsHotPuddle(entity, grid, tile) && NeedsFoam(entity, grid, tile))
                targetTiles.Add(tile);
        }
    }

    /// <summary>
    ///     Whether the tile has a chemfire that water or foam can put out. The rest the NPC can do nothing about.
    /// </summary>
    private bool IsBurning(Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (_chemicalFireSystem.GetTileChemicalFires((grid.Owner, null), tile) is not { } tileFires)
            return false;

        foreach (var fire in tileFires.Fires.Values)
        {
            if (fire.Comp.Extinguishable || fire.Comp.FoamSmotherable)
                return true;
        }

        return false;
    }

    private bool IsHotPuddle(Entity<KsNpcFirefighterComponent> entity, Entity<MapGridComponent> grid, Vector2i tile)
    {
        var tileRef = _mapSystem.GetTileRef(grid, tile);
        if (!_puddleSystem.TryGetPuddle(tileRef, out var puddleUid) ||
            !_puddleQuery.TryComp(puddleUid, out var puddleComponent) ||
            !_solutionContainerSystem.ResolveSolution(puddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution))
            return false;

        return solution.Temperature >= entity.Comp.HotPuddleTemperature;
    }

    private bool NeedsFoam(Entity<KsNpcFirefighterComponent> entity, Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (!_foamBlanketSystem.TryGetBlanket(grid, tile, out var blanket) || !_foamBlanketSystem.IsActive(blanket))
            return true;

        return blanket.Comp.Foam < blanket.Comp.MaxFoam * entity.Comp.RefoamFraction;
    }

    private Vector2 GetLocalPosition(EntityUid uid, EntityUid gridUid)
    {
        return Vector2.Transform(_transformSystem.GetWorldPosition(uid), _transformSystem.GetInvWorldMatrix(gridUid));
    }

    private static Vector2 TileCenter(Vector2i tile, MapGridComponent gridComponent)
    {
        return (tile + new Vector2(0.5f, 0.5f)) * gridComponent.TileSize;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0f)
            return (point - start).Length();

        var fraction = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return (point - (start + segment * fraction)).Length();
    }
}
