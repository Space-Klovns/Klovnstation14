#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Robust.Shared;
using Robust.Shared.Analyzers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Benchmarks._KS14.Atmos;

/// <summary>
///     What a whole atmos cycle costs on a real station: tiles, pipe nets and every atmos device, run through every
///         processing stage once. Uses only upstream API, so the same file runs against a build from before a change to
///         atmos, for a before-and-after comparison.
/// </summary>
[Virtual]
[MemoryDiagnoser]
public class KsAtmosStationBenchmark
{
    private const string MapPath = "/Maps/box.yml";

    /// <summary>
    ///     Atmos cycles per benchmark call, so the call's own scheduling is a small share of it.
    /// </summary>
    private const int CyclesPerCall = 10;

    private static bool _poolStarted;

    private TestPair _pair = default!;
    private AtmosphereSystem _atmosphereSystem = default!;
    private Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> _station;
    private Entity<MapAtmosphereComponent?> _mapAtmosphere;

    [GlobalSetup]
    public void Setup()
    {
        // As in NpcNavigationBenchmark: resources sit four folders up out of process, and none in process.
        ProgramShared.PathOffset = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "../../RobustToolbox")) ? "" : "../../../../";
        if (!_poolStarted)
        {
            PoolManager.Startup();
            _poolStarted = true;
        }

        _pair = PoolManager.GetServerClient(testContext: new ExternalTestContext("Benchmark", StreamWriter.Null)).GetAwaiter().GetResult();
        var server = _pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        _atmosphereSystem = entityManager.System<AtmosphereSystem>();

        server.WaitPost(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            if (!entityManager.System<MapLoaderSystem>().TryLoadMap(new ResPath(MapPath), out _, out _, options))
                throw new Exception("Map load failed");
        }).GetAwaiter().GetResult();

        // Let the station's air and pipes settle from their mapped state, as they would early in a round.
        server.WaitRunTicks(300).GetAwaiter().GetResult();

        server.WaitPost(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            var bestTileCount = -1;
            var enumerator = entityManager.AllEntityQueryEnumerator<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>();
            while (enumerator.MoveNext(out var gridUid, out var gridAtmosphere, out var overlay, out var mapGrid, out var transform))
            {
                var tileCount = mapSystem.GetAllTiles(gridUid, mapGrid).Count();
                if (tileCount <= bestTileCount)
                    continue;

                bestTileCount = tileCount;
                _station = (gridUid, gridAtmosphere, overlay, mapGrid, transform);
            }

            if (bestTileCount < 0)
                throw new Exception("The map has no grid with an atmosphere.");

            var mapUid = _station.Comp4.MapUid!.Value;
            _mapAtmosphere = (mapUid, entityManager.GetComponentOrNull<MapAtmosphereComponent>(mapUid));
        }).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _pair.DisposeAsync();
    }

    [Benchmark]
    public async Task StationAtmosCycles()
    {
        await _pair.Server.WaitPost(() =>
        {
            for (var i = 0; i < CyclesPerCall; i++)
            {
                _atmosphereSystem.RunProcessingFull(_station, _mapAtmosphere, _atmosphereSystem.AtmosTime);
            }
        });
    }
}
