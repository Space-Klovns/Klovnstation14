#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.NPC.Pathfinding;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Physics;
using Robust.Shared;
using Robust.Shared.Analyzers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Benchmarks._KS14.NPC;

/// <summary>
///     What NPC sight and pathfinding cost on a real station: line of sight between floor tiles, A* and tactical floods
///         between them, and rebuilding the navmesh. Every sample is drawn from a fixed seed, so runs compare like for
///         like. The private pathfinding steps are called directly, single-threaded, so each measures one search and
///         not the scheduling around it.
/// </summary>
[Virtual]
[MemoryDiagnoser]
public class NpcNavigationBenchmark
{
    private const string MapPath = "/Maps/box.yml";

    private const int RayCount = 256;
    private const int PathCount = 32;
    private const int FloodCount = 16;

    private TestPair _pair = default!;
    private IEntityManager _entityManager = default!;
    private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    private PathfindingSystem _pathfindingSystem = default!;

    private Func<AStarPathRequest, PathResult> _updateAStarPath = default!;
    private Func<TacticalPathRequest, PathResult> _updateTacticalPath = default!;
    private Action<TacticalPathRequest> _rentTacticalSearchState = default!;
    private Action<TacticalPathRequest> _returnTacticalSearchState = default!;
    private Action<GridPathfindingChunk, Entity<MapGridComponent>> _buildBreadcrumbs = default!;

    // Only there once A* pools its search state; without them, requests use their own.
    private Action<AStarPathRequest>? _rentAStarSearchState;
    private Action<AStarPathRequest>? _returnAStarSearchState;

    private Entity<MapGridComponent> _grid;
    private GridPathfindingChunk[] _chunks = default!;

    private (MapCoordinates Origin, MapCoordinates Other)[] _rays = default!;
    private (EntityCoordinates Start, EntityCoordinates End)[] _paths = default!;
    private EntityCoordinates[] _floods = default!;

    private const int SweepCount = 16;
    private const float SweepRange = 7f;
    private const float ExposureRange = 15f;

    // Each sweep: a member's position, and every floor tile centre within the sweep range of it.
    private (MapCoordinates Member, MapCoordinates[] Tiles)[] _sweeps = default!;

    // Each exposure pass: a threat's probes, and the spots weighed against them.
    private (List<MapCoordinates> Probes, MapCoordinates[] Spots)[] _exposures = default!;

    private readonly NpcSightField _sightField = new();
    private readonly List<NpcSightField> _probeFields = new();

    private static bool _poolStarted;

    private int _collisionLayer;
    private int _collisionMask;

    [GlobalSetup]
    public void Setup()
    {
        // Resources are found relative to the executable, which out of process (the default) sits four folders below
        //      the usual one; in process (--inProcess) it is the usual one.
        ProgramShared.PathOffset = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "../../RobustToolbox")) ? "" : "../../../../";

        // Once per process: in process, every benchmark's setup runs in the same one, and the pool cannot start twice.
        if (!_poolStarted)
        {
            PoolManager.Startup();
            _poolStarted = true;
        }

        _pair = PoolManager.GetServerClient(testContext: new ExternalTestContext("Benchmark", StreamWriter.Null)).GetAwaiter().GetResult();
        var server = _pair.Server;
        _entityManager = server.ResolveDependency<IEntityManager>();
        _npcLineOfSightSystem = _entityManager.System<NpcLineOfSightSystem>();
        _pathfindingSystem = _entityManager.System<PathfindingSystem>();

        server.WaitPost(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            if (!_entityManager.System<MapLoaderSystem>().TryLoadMap(new ResPath(MapPath), out _, out _, options))
                throw new Exception("Map load failed");
        }).GetAwaiter().GetResult();

        // Long enough for every chunk's navmesh to have been built.
        server.WaitRunTicks(120).GetAwaiter().GetResult();

        BindPrivateSteps();

        var mapSystem = _entityManager.System<SharedMapSystem>();
        var transformSystem = _entityManager.System<SharedTransformSystem>();

        // The station: the grid with the most tiles.
        var gridEnumerator = _entityManager.AllEntityQueryEnumerator<MapGridComponent>();
        var bestTileCount = -1;
        while (gridEnumerator.MoveNext(out var gridUid, out var mapGridComponent))
        {
            var tileCount = mapSystem.GetAllTiles(gridUid, mapGridComponent).Count();
            if (tileCount <= bestTileCount)
                continue;

            bestTileCount = tileCount;
            _grid = (gridUid, mapGridComponent);
        }

        _chunks = _entityManager.GetComponent<GridPathfindingComponent>(_grid).Chunks.Values.ToArray();

        // Floor tiles nothing is anchored on: where NPCs stand and look from.
        var floorTiles = new List<Vector2i>();
        foreach (var tileRef in mapSystem.GetAllTiles(_grid, _grid.Comp))
        {
            if (!mapSystem.GetAnchoredEntitiesEnumerator(_grid, _grid.Comp, tileRef.GridIndices).MoveNext(out _))
                floorTiles.Add(tileRef.GridIndices);
        }

        floorTiles.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var random = new Random(42);

        EntityCoordinates TileCenter(Vector2i tile, bool jitter)
        {
            var offset = jitter
                ? new System.Numerics.Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.8f
                : System.Numerics.Vector2.Zero;

            return new EntityCoordinates(_grid, new System.Numerics.Vector2(tile.X + 0.5f, tile.Y + 0.5f) + offset);
        }

        Vector2i PickWithin(Vector2i from, int minRange, int maxRange)
        {
            while (true)
            {
                var candidate = floorTiles[random.Next(floorTiles.Count)];
                var distance = (candidate - from).Length;
                if (distance >= minRange && distance <= maxRange)
                    return candidate;
            }
        }

        // Half from tile centre to tile centre, as room sweeps cast them; half from anywhere on a tile, as perception does.
        _rays = new (MapCoordinates, MapCoordinates)[RayCount];
        for (var i = 0; i < RayCount; i++)
        {
            var from = floorTiles[random.Next(floorTiles.Count)];
            var to = PickWithin(from, 1, 12);
            var jitter = i % 2 == 1;
            _rays[i] = (transformSystem.ToMapCoordinates(TileCenter(from, jitter)), transformSystem.ToMapCoordinates(TileCenter(to, jitter)));
        }

        _paths = new (EntityCoordinates, EntityCoordinates)[PathCount];
        for (var i = 0; i < PathCount; i++)
        {
            var from = floorTiles[random.Next(floorTiles.Count)];
            _paths[i] = (TileCenter(from, jitter: false), TileCenter(PickWithin(from, 6, 25), jitter: false));
        }

        _floods = new EntityCoordinates[FloodCount];
        for (var i = 0; i < FloodCount; i++)
        {
            _floods[i] = TileCenter(floorTiles[random.Next(floorTiles.Count)], jitter: false);
        }

        MapCoordinates[] FloorWithin(Vector2i centre, float range, int max)
        {
            return floorTiles
                .Where(tile => (tile - centre).Length <= range)
                .Take(max)
                .Select(tile => transformSystem.ToMapCoordinates(TileCenter(tile, jitter: false)))
                .ToArray();
        }

        _sweeps = new (MapCoordinates, MapCoordinates[])[SweepCount];
        for (var i = 0; i < SweepCount; i++)
        {
            var member = floorTiles[random.Next(floorTiles.Count)];
            _sweeps[i] = (transformSystem.ToMapCoordinates(TileCenter(member, jitter: true)), FloorWithin(member, SweepRange, int.MaxValue));
        }

        _exposures = new (List<MapCoordinates>, MapCoordinates[])[4];
        for (var i = 0; i < _exposures.Length; i++)
        {
            var threat = floorTiles[random.Next(floorTiles.Count)];
            _exposures[i] = (FloorWithin(threat, 4f, 12).ToList(), FloorWithin(threat, 10f, 200).Where((_, index) => index % 4 == 0).Take(32).ToArray());
        }

        while (_probeFields.Count < 12)
        {
            _probeFields.Add(new NpcSightField());
        }

        _collisionLayer = (int)CollisionGroup.MobLayer;
        _collisionMask = (int)CollisionGroup.MobMask;
    }

    private void BindPrivateSteps()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var type = typeof(PathfindingSystem);

        _updateAStarPath = type.GetMethod("UpdateAStarPath", flags)!.CreateDelegate<Func<AStarPathRequest, PathResult>>(_pathfindingSystem);
        _updateTacticalPath = type.GetMethod("UpdateTacticalPath", flags)!.CreateDelegate<Func<TacticalPathRequest, PathResult>>(_pathfindingSystem);
        _rentTacticalSearchState = type.GetMethod("RentTacticalSearchState", flags)!.CreateDelegate<Action<TacticalPathRequest>>(_pathfindingSystem);
        _returnTacticalSearchState = type.GetMethod("ReturnTacticalSearchState", flags)!.CreateDelegate<Action<TacticalPathRequest>>(_pathfindingSystem);
        _buildBreadcrumbs = type.GetMethod("BuildBreadcrumbs", flags)!.CreateDelegate<Action<GridPathfindingChunk, Entity<MapGridComponent>>>(_pathfindingSystem);
        _rentAStarSearchState = type.GetMethod("RentAStarSearchState", flags)?.CreateDelegate<Action<AStarPathRequest>>(_pathfindingSystem);
        _returnAStarSearchState = type.GetMethod("ReturnAStarSearchState", flags)?.CreateDelegate<Action<AStarPathRequest>>(_pathfindingSystem);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        // The pool is left running for the next benchmark in this process; it goes with the process.
        await _pair.DisposeAsync();
    }

    /// <summary>
    ///     One line of sight check, averaged over the samples.
    /// </summary>
    [Benchmark(OperationsPerInvoke = RayCount)]
    public int LineOfSight()
    {
        var clear = 0;
        foreach (var (origin, other) in _rays)
        {
            if (_npcLineOfSightSystem.InLineOfSight(origin, other, range: 15f))
                clear++;
        }

        return clear;
    }

    /// <summary>
    ///     One member's sweep of the floor round it, a ray a tile, as room sweeps worked before sight fields.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SweepCount)]
    public int SweepByRays()
    {
        var seen = 0;
        foreach (var (member, tiles) in _sweeps)
        {
            foreach (var tile in tiles)
            {
                if (_npcLineOfSightSystem.InLineOfSight(member, tile, SweepRange))
                    seen++;
            }
        }

        return seen;
    }

    /// <summary>
    ///     The same sweep through one sight field per member.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SweepCount)]
    public int SweepByField()
    {
        var seen = 0;
        foreach (var (member, tiles) in _sweeps)
        {
            _npcLineOfSightSystem.BuildSightField(member, SweepRange, _sightField);
            foreach (var tile in tiles)
            {
                if (_npcLineOfSightSystem.InLineOfSight(_sightField, tile))
                    seen++;
            }
        }

        return seen;
    }

    /// <summary>
    ///     Weighing every spot of an exposure pass against every probe, a ray each.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 4)]
    public int ExposureByRays()
    {
        var seen = 0;
        foreach (var (probes, spots) in _exposures)
        {
            foreach (var spot in spots)
            {
                foreach (var probe in probes)
                {
                    if (_npcLineOfSightSystem.InLineOfSight(probe, spot, ExposureRange))
                        seen++;
                }
            }
        }

        return seen;
    }

    /// <summary>
    ///     The same pass through one sight field per probe.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 4)]
    public int ExposureByFields()
    {
        var seen = 0;
        foreach (var (probes, spots) in _exposures)
        {
            for (var i = 0; i < probes.Count; i++)
            {
                _npcLineOfSightSystem.BuildSightField(probes[i], ExposureRange, _probeFields[i]);
            }

            foreach (var spot in spots)
            {
                for (var i = 0; i < probes.Count; i++)
                {
                    if (_npcLineOfSightSystem.InLineOfSight(_probeFields[i], spot))
                        seen++;
                }
            }
        }

        return seen;
    }

    /// <summary>
    ///     One A* search, start to finish, averaged over the samples. Some samples have no path, which costs the whole
    ///         node limit, as it does in game.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PathCount)]
    public int AStarPath()
    {
        var found = 0;
        foreach (var (start, end) in _paths)
        {
            var request = new AStarPathRequest(start, end, PathFlags.Interact, distance: 0f, _collisionLayer, _collisionMask, CancellationToken.None);
            _rentAStarSearchState?.Invoke(request);

            PathResult result;
            do
            {
                result = _updateAStarPath(request);
            }
            while (result == PathResult.Continuing);

            if (result == PathResult.Path)
                found++;

            _returnAStarSearchState?.Invoke(request);
        }

        return found;
    }

    /// <summary>
    ///     One tactical position flood, as an NPC holding a position makes on every replan.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FloodCount)]
    public int TacticalFlood()
    {
        var candidates = 0;
        foreach (var reference in _floods)
        {
            var request = new TacticalPathRequest(reference, 10f, 32, PathFlags.Interact, _collisionLayer, _collisionMask, CancellationToken.None);
            _rentTacticalSearchState(request);

            PathResult result;
            do
            {
                result = _updateTacticalPath(request);
            }
            while (result == PathResult.Continuing);

            candidates += request.Candidates.Count;
            _returnTacticalSearchState(request);
        }

        return candidates;
    }

    /// <summary>
    ///     Rebuilding one chunk's breadcrumbs, the per-tile half of a navmesh rebuild, averaged over the station's chunks.
    /// </summary>
    [Benchmark]
    public void NavmeshBreadcrumbs()
    {
        foreach (var chunk in _chunks)
        {
            _buildBreadcrumbs(chunk, _grid);
        }
    }
}
