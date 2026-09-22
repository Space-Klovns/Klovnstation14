using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Power.Components;
using Content.Server.Procedural;
using Content.Server.Radio.EntitySystems;
using Content.Server.Salvage;
using Content.Server.Salvage.Magnet;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Parallax;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radio;
using Content.Shared.Salvage.Magnet;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.SaturnClouds;

public sealed partial class SaturnCloudSystem : EntitySystem
{
    private const float PercentageScale = 100f;

    private static readonly ProtoId<RadioChannelPrototype> EngineeringChannel = "Engineering";

    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private DungeonSystem _dungeonSystem = default!;
    [Dependency] private RadioSystem _radioSystem = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedBatterySystem _batterySystem = default!;
    [Dependency] private ShuttleSystem _shuttleSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<ApcPowerReceiverBatteryComponent> _apcBatteryQuery = default!;
    [Dependency] private EntityQuery<ApcPowerReceiverComponent> _apcPowerQuery = default!;
    [Dependency] private EntityQuery<InnateMooringComponent> _innateMooringQuery = default!;
    [Dependency] private EntityQuery<SaturnCloudMapComponent> _saturnMapQuery = default!;

    private readonly HashSet<EntityUid> _protectedGrids = new();
    private readonly List<EntityUid> _exposedGrids = new();

    [SubscribeLocalEvent]
    private void OnLoadingMaps(LoadingMapsEvent args)
    {
        if (args.Maps.Count == 0 ||
            args.Maps[0].KsSaturnCloudMap is not { } cloudMapPrototypeId ||
            !ProtoMan.TryIndex(cloudMapPrototypeId, out var cloudMapPrototype) ||
            cloudMapPrototype.KsSaturnOrbitalMap is not { } orbitalMapPrototypeId ||
            !ProtoMan.TryIndex(orbitalMapPrototypeId, out var orbitalMapPrototype))
        {
            return;
        }

        var probability = Math.Clamp(
            _configurationManager.GetCVar(KsCCVars.SaturnCloudSettingProbability),
            0f,
            1f);
        if (!_robustRandom.Prob(probability))
            return;

        args.Maps[0] = cloudMapPrototype.KsCreateSaturnRuntimeVariant(orbital: false);
        args.Maps.Add(orbitalMapPrototype.KsCreateSaturnRuntimeVariant(orbital: true));
    }

    [SubscribeLocalEvent]
    private void OnPostGameMapLoad(PostGameMapLoad args)
    {
        var mapUid = _mapSystem.GetMapOrInvalid(args.Map);
        if (_saturnMapQuery.TryGetComponent(mapUid, out var cloudMapComponent))
            ConfigureCloudMap(mapUid, cloudMapComponent);

        if (!args.GameMap.KsIsSaturnCloudVariant && !args.GameMap.KsIsSaturnOrbitalVariant)
            return;

        _shuttleSystem.TryAddFTLDestination(
            args.Map,
            true,
            false,
            false,
            out _);

        if (args.GameMap.KsIsSaturnOrbitalVariant)
            GenerateOrbitalAsteroidBelt(args);
    }

    [SubscribeLocalEvent]
    private void OnRoundStarting(RoundStartingEvent args)
    {
        var mapQuery = EntityQueryEnumerator<SaturnCloudMapComponent>();
        while (mapQuery.MoveNext(out var mapUid, out var cloudMapComponent))
        {
            ConfigureCloudMap(mapUid, cloudMapComponent);

            foreach (var station in _stationSystem.GetStationEntities())
            {
                var mainGridUid = _stationSystem.GetLargestGrid(station.Owner);
                if (mainGridUid == null || Transform(mainGridUid.Value).MapUid != mapUid)
                    continue;

                EnsureComp<SaturnMainStationGridComponent>(mainGridUid.Value);
                EnsureGridExposure(mainGridUid.Value, cloudMapComponent);
                LinkMappedMooringDevices(mainGridUid.Value);
            }
        }
    }

    private void ConfigureCloudMap(EntityUid mapUid, SaturnCloudMapComponent cloudMapComponent)
    {
        _atmosphereSystem.SetMapAtmosphere(mapUid, false, cloudMapComponent.Atmosphere);

        var parallaxComponent = EnsureComp<ParallaxComponent>(mapUid);
        parallaxComponent.Parallax = cloudMapComponent.Parallax;
        Dirty(mapUid, parallaxComponent);
    }

    [SubscribeLocalEvent]
    private void OnMainStationGridShutdown(Entity<SaturnMainStationGridComponent> entity, ref ComponentShutdown args)
    {
        EndCloudRoundForLostMainGrid();
    }

    [SubscribeLocalEvent]
    private void OnMainStationGridRemoved(Entity<StationDataComponent> entity, ref StationGridRemovedEvent args)
    {
        if (HasComp<SaturnMainStationGridComponent>(args.GridId))
            EndCloudRoundForLostMainGrid();
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _protectedGrids.Clear();
    }

    [SubscribeLocalEvent]
    private void OnGridMapInit(Entity<MapGridComponent> entity, ref MapInitEvent args)
    {
        UpdateGridExposure(entity.Owner, Transform(entity.Owner));
    }

    [SubscribeLocalEvent]
    private void OnGridParentChanged(Entity<MapGridComponent> entity, ref EntParentChangedMessage args)
    {
        UpdateGridExposure(entity.Owner, args.Transform);
    }

    [SubscribeLocalEvent]
    private void OnGridSplit(ref GridSplitEvent args)
    {
        var exposedComponent = CompOrNull<SaturnWindExposedComponent>(args.Grid);
        var innateMooring = _innateMooringQuery.HasComp(args.Grid);
        var cloudMapComponent = TryGetCloudMap(args.Grid);

        foreach (var newGridUid in args.NewGrids)
        {
            if (exposedComponent != null && cloudMapComponent != null)
            {
                var newExposedComponent = EnsureGridExposure(newGridUid, cloudMapComponent);
                newExposedComponent.UnprotectedTime = exposedComponent.UnprotectedTime;
                newExposedComponent.NextDamageTime = exposedComponent.NextDamageTime;
            }

            if (innateMooring)
                EnsureComp<InnateMooringComponent>(newGridUid);
        }
    }

    [SubscribeLocalEvent]
    private void OnSalvageMagnetActivated(ref SalvageMagnetActivatedEvent args)
    {
        if (Transform(args.Magnet).MapUid is not { } mapUid || !_saturnMapQuery.HasComp(mapUid))
            return;

        var stationUid = _stationSystem.GetOwningStation(args.Magnet);
        if (stationUid == null || !TryComp<SalvageMagnetDataComponent>(stationUid, out var dataComponent))
            return;

        if (dataComponent.ActiveEntities == null)
            return;

        foreach (var gridUid in dataComponent.ActiveEntities)
            EnsureComp<InnateMooringComponent>(gridUid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        var mapQuery = EntityQueryEnumerator<SaturnCloudMapComponent>();
        while (mapQuery.MoveNext(out var mapUid, out var cloudMapComponent))
        {
            if (_gameTiming.CurTime < cloudMapComponent.NextUpdate)
                continue;

            var elapsed = cloudMapComponent.LastUpdate == TimeSpan.Zero
                ? cloudMapComponent.UpdateInterval
                : _gameTiming.CurTime - cloudMapComponent.LastUpdate;
            cloudMapComponent.LastUpdate = _gameTiming.CurTime;
            cloudMapComponent.NextUpdate = _gameTiming.CurTime + cloudMapComponent.UpdateInterval;

            UpdateMooringDevices(mapUid);
            UpdateExposedGrids(mapUid, cloudMapComponent, elapsed);
        }
    }

    private void LinkMappedMooringDevices(EntityUid mainGridUid)
    {
        var query = EntityQueryEnumerator<MooringDeviceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var deviceComponent, out var transformComponent))
        {
            if (transformComponent.GridUid != mainGridUid)
                continue;

            deviceComponent.ProtectedGrid = mainGridUid;
        }
    }

    private void UpdateGridExposure(EntityUid gridUid, TransformComponent transformComponent)
    {
        if (transformComponent.MapUid is { } mapUid &&
            _saturnMapQuery.TryGetComponent(mapUid, out var cloudMapComponent))
        {
            EnsureGridExposure(gridUid, cloudMapComponent);
            return;
        }

        RemCompDeferred<SaturnWindExposedComponent>(gridUid);
    }

    private SaturnWindExposedComponent EnsureGridExposure(EntityUid gridUid, SaturnCloudMapComponent cloudMapComponent)
    {
        var exposedComponent = EnsureComp<SaturnWindExposedComponent>(gridUid);
        if (exposedComponent.NextDamageTime == TimeSpan.Zero)
            exposedComponent.NextDamageTime = cloudMapComponent.DamageDelay;
        return exposedComponent;
    }

    private SaturnCloudMapComponent? TryGetCloudMap(EntityUid gridUid)
    {
        return Transform(gridUid).MapUid is { } mapUid
            ? _saturnMapQuery.CompOrNull(mapUid)
            : null;
    }

    private void UpdateMooringDevices(EntityUid mapUid)
    {
        _protectedGrids.Clear();
        var query = EntityQueryEnumerator<MooringDeviceComponent, BatteryComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var deviceComponent, out var batteryComponent, out var transformComponent))
        {
            if (transformComponent.MapUid != mapUid)
                continue;

            var charge = _batterySystem.GetCharge((uid, batteryComponent));
            var receiverComponent = _apcPowerQuery.CompOrNull(uid);
            var operational = deviceComponent.ProtectedGrid != null &&
                              transformComponent.GridUid == deviceComponent.ProtectedGrid &&
                              receiverComponent != null &&
                              !receiverComponent.PowerDisabled &&
                              (receiverComponent.Powered || charge > 0f);

            if (operational && transformComponent.GridUid is { } gridUid)
                _protectedGrids.Add(gridUid);

            UpdateDeviceWarnings((uid, deviceComponent, batteryComponent), operational, charge);
        }
    }

    private void UpdateDeviceWarnings(Entity<MooringDeviceComponent, BatteryComponent> entity, bool operational, float charge)
    {
        var chargeFraction = entity.Comp2.MaxCharge <= 0f ? 0f : charge / entity.Comp2.MaxCharge;
        var usingBattery = _apcBatteryQuery.TryGetComponent(entity.Owner, out var apcBatteryComponent) &&
                           apcBatteryComponent.Enabled;

        if (entity.Comp1.WarningLevels.Length > 0 &&
            !usingBattery &&
            chargeFraction > entity.Comp1.WarningLevels[0])
        {
            entity.Comp1.WarningIndex = 0;
        }

        while (usingBattery &&
               entity.Comp1.WarningIndex < entity.Comp1.WarningLevels.Length &&
               chargeFraction <= entity.Comp1.WarningLevels[entity.Comp1.WarningIndex])
        {
            var percent = (int)MathF.Round(chargeFraction * PercentageScale);
            _radioSystem.SendRadioMessage(
                entity.Owner,
                Loc.GetString("ks-mooring-device-radio-low-battery", ("percent", percent)),
                EngineeringChannel,
                entity.Owner);
            entity.Comp1.WarningIndex++;
        }

        if (entity.Comp1.WasOperational && !operational)
        {
            _chatSystem.DispatchStationAnnouncement(
                entity.Owner,
                Loc.GetString("ks-mooring-device-announcement-power-lost"),
                Loc.GetString("ks-mooring-device-announcement-sender"),
                colorOverride: Color.OrangeRed);
        }

        entity.Comp1.WasOperational = operational;
    }

    private void GenerateOrbitalAsteroidBelt(PostGameMapLoad args)
    {
        if (args.GameMap.KsSaturnAsteroidDungeon is not { } dungeonPrototypeId ||
            args.GameMap.KsSaturnAsteroidCount <= 0 ||
            args.GameMap.KsSaturnAsteroidMinimumDistance < 0f ||
            args.GameMap.KsSaturnAsteroidMaximumDistance < args.GameMap.KsSaturnAsteroidMinimumDistance ||
            !ProtoMan.TryIndex(dungeonPrototypeId, out var dungeonPrototype))
        {
            Log.Error($"Saturn orbital map {args.GameMap.ID} has an invalid asteroid-belt configuration.");
            return;
        }

        var angularStep = MathF.Tau / args.GameMap.KsSaturnAsteroidCount;
        var angularOffset = _robustRandom.NextFloat(0f, MathF.Tau);

        for (var asteroidIndex = 0; asteroidIndex < args.GameMap.KsSaturnAsteroidCount; asteroidIndex++)
        {
            var angle = angularOffset + angularStep * asteroidIndex;
            var distance = _robustRandom.NextFloat(
                args.GameMap.KsSaturnAsteroidMinimumDistance,
                args.GameMap.KsSaturnAsteroidMaximumDistance);
            var position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            var asteroidGrid = _mapSystem.CreateGridEntity(args.Map);

            _transformSystem.SetMapCoordinates(asteroidGrid, new MapCoordinates(position, args.Map));
            _dungeonSystem.GenerateDungeon(
                dungeonPrototype,
                asteroidGrid.Owner,
                asteroidGrid.Comp,
                Vector2i.Zero,
                _robustRandom.Next());
        }
    }

    private void EndCloudRoundForLostMainGrid()
    {
        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        _gameTicker.EndRound(Loc.GetString("ks-saturn-clouds-round-end-station-lost"));
    }

    private void UpdateExposedGrids(EntityUid mapUid, SaturnCloudMapComponent cloudMapComponent, TimeSpan elapsed)
    {
        _exposedGrids.Clear();
        var query = EntityQueryEnumerator<SaturnWindExposedComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out _, out _))
        {
            if (Transform(gridUid).MapUid != mapUid)
                continue;

            _exposedGrids.Add(gridUid);
        }

        // Tearing tiles can split a grid and add SaturnWindExposedComponent to the new grids.
        // Process a snapshot so those structural changes cannot invalidate an active entity query.
        foreach (var gridUid in _exposedGrids)
        {
            if (!TryComp<SaturnWindExposedComponent>(gridUid, out var exposedComponent) ||
                !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            {
                continue;
            }

            if (_innateMooringQuery.HasComp(gridUid) || _protectedGrids.Contains(gridUid))
            {
                exposedComponent.UnprotectedTime = TimeSpan.Zero;
                exposedComponent.NextDamageTime = cloudMapComponent.DamageDelay;
                continue;
            }

            exposedComponent.UnprotectedTime += elapsed;
            if (exposedComponent.UnprotectedTime >= cloudMapComponent.DestructionDelay)
            {
                _chatSystem.DispatchStationAnnouncement(
                    gridUid,
                    Loc.GetString("ks-mooring-grid-announcement-destroyed"),
                    Loc.GetString("ks-mooring-device-announcement-sender"),
                    colorOverride: Color.Red);
                QueueDel(gridUid);
                continue;
            }

            if (exposedComponent.UnprotectedTime < exposedComponent.NextDamageTime)
                continue;

            TearChunk((gridUid, gridComponent), cloudMapComponent, exposedComponent.UnprotectedTime);
            exposedComponent.NextDamageTime += cloudMapComponent.DamageInterval;
        }
    }

    private void TearChunk(
        Entity<MapGridComponent> grid,
        SaturnCloudMapComponent cloudMapComponent,
        TimeSpan unprotectedTime)
    {
        var allTiles = _mapSystem.GetAllTiles(grid.Owner, grid.Comp)
            .Where(tile => !tile.Tile.IsEmpty)
            .ToList();
        if (allTiles.Count == 0)
            return;

        var edgeTiles = new List<TileRef>();
        ReadOnlySpan<Vector2i> cardinalDirections =
        [
            new(1, 0),
            new(-1, 0),
            new(0, 1),
            new(0, -1),
        ];

        foreach (var tile in allTiles)
        {
            foreach (var direction in cardinalDirections)
            {
                if (_mapSystem.TryGetTileRef(grid.Owner, grid.Comp, tile.GridIndices + direction, out var neighbor) &&
                    !neighbor.Tile.IsEmpty)
                {
                    continue;
                }

                edgeTiles.Add(tile);
                break;
            }
        }

        var centerTile = _robustRandom.Pick(edgeTiles.Count > 0 ? edgeTiles : allTiles);
        var radiusIncreases = cloudMapComponent.DamageRadiusIncreaseInterval > TimeSpan.Zero
            ? (int)(unprotectedTime / cloudMapComponent.DamageRadiusIncreaseInterval)
            : 0;
        var radius = Math.Clamp(
            cloudMapComponent.InitialDamageRadius + radiusIncreases,
            cloudMapComponent.InitialDamageRadius,
            cloudMapComponent.MaximumDamageRadius);
        var removedTiles = new List<(Vector2i, Tile)>();

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;

                var indices = centerTile.GridIndices + new Vector2i(x, y);
                if (_mapSystem.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tile) && !tile.Tile.IsEmpty)
                    removedTiles.Add((indices, Tile.Empty));
            }
        }

        _mapSystem.SetTiles(grid.Owner, grid.Comp, removedTiles);
    }
}
