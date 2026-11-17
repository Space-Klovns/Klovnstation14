using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Hides the cultivation world around the station when a round starts: manuals and an ancestral ring in the
///         maintenance tunnels, more manuals on the library shelves, and sometimes a forbidden scripture for the wrong
///         person to find. It also finds the places other things turn up in, like heavenly treasures.
/// </summary>
public sealed partial class CultivationLootSystem : EntitySystem
{
    private static readonly EntProtoId[] QiManuals =
    {
        "CultivationManualMetal", "CultivationManualWood", "CultivationManualWater", "CultivationManualFire", "CultivationManualEarth",
    };

    private static readonly EntProtoId[] MartialManuals =
    {
        "MartialManualDragonPalms", "MartialManualSixMeridians", "MartialManualNorthernDarkness", "MartialManualStarAbsorbing",
        "MartialManualGreatShift", "MartialManualNineYang", "MartialManualGoldenBell", "MartialManualGracefulSteps",
        "MartialManualLadderCloud", "MartialManualDugu", "MartialManualMuscleTendon", "MartialManualDrunkenFist",
        "MartialManualEagleClaw", "MartialManualWingChun",
    };

    private static readonly EntProtoId BodyMoldingArt = "BodyManualMoldingArt";
    private static readonly EntProtoId BodyPrimer = "BodyManualCopperSkinPrimer";
    private static readonly EntProtoId DemonicScripture = "KsDemonicScripture";
    private static readonly EntProtoId AncestralRing = "KsAncestralRing";

    private const int MaintenanceQiManuals = 3;
    private const int MaintenanceMartialManuals = 2;
    private const float ScriptureChance = 0.3f;

    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
    }

    private void OnStationPostInit(ref StationPostInitEvent args)
    {
        if (_stationSystem.GetLargestGrid(args.Station.AsNullable()) is not { } gridUid)
            return;

        SpawnLoot(gridUid);
    }

    /// <summary>
    ///     Hide the round's cultivation loot about a station grid.
    /// </summary>
    public void SpawnLoot(EntityUid gridUid)
    {
        var maintenance = MaintenanceSpots(gridUid);
        for (var i = 0; i < MaintenanceQiManuals; i++)
            SpawnAt(_robustRandom.Pick(QiManuals), maintenance, gridUid);

        for (var i = 0; i < MaintenanceMartialManuals; i++)
            SpawnAt(_robustRandom.Pick(MartialManuals), maintenance, gridUid);

        SpawnAt(AncestralRing, maintenance, gridUid);
        SpawnAt(BodyMoldingArt, maintenance, gridUid);
        SpawnAt(BodyPrimer, maintenance, gridUid);
        if (_robustRandom.Prob(ScriptureChance))
            SpawnAt(DemonicScripture, maintenance, gridUid);

        var library = SpotsBeside(gridUid, "Bookshelf");
        SpawnAt(_robustRandom.Pick(QiManuals), library, gridUid);
        SpawnAt(BodyPrimer, library, gridUid);
    }

    private void SpawnAt(EntProtoId prototype, List<EntityCoordinates> spots, EntityUid gridUid)
    {
        if (spots.Count > 0)
        {
            Spawn(prototype, _robustRandom.PickAndTake(spots));
            return;
        }

        if (RandomSpot(gridUid) is { } fallback)
            Spawn(prototype, fallback);
    }

    #region Finding places

    /// <summary>
    ///     Open floor just beside the station's maintenance hatches: as near as a station comes to SS13's maintenance
    ///         landmarks.
    /// </summary>
    public List<EntityCoordinates> MaintenanceSpots(EntityUid gridUid)
    {
        var spots = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<DoorComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var metaData, out var transformComponent))
        {
            if (transformComponent.GridUid != gridUid || metaData.EntityPrototype?.ID.StartsWith("AirlockMaint") != true)
                continue;

            if (FreeBeside(gridUid, transformComponent.Coordinates) is { } spot)
                spots.Add(spot);
        }

        return spots;
    }

    private List<EntityCoordinates> SpotsBeside(EntityUid gridUid, string prototypePrefix)
    {
        var spots = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var metaData, out var transformComponent))
        {
            if (transformComponent.GridUid != gridUid || metaData.EntityPrototype?.ID.StartsWith(prototypePrefix) != true)
                continue;

            if (FreeBeside(gridUid, transformComponent.Coordinates) is { } spot)
                spots.Add(spot);
        }

        return spots;
    }

    /// <summary>
    ///     An open floor tile next to a place, if any.
    /// </summary>
    public EntityCoordinates? FreeBeside(EntityUid gridUid, EntityCoordinates coordinates)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return null;

        var centre = _mapSystem.TileIndicesFor(gridUid, gridComponent, coordinates);
        foreach (var offset in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
        {
            if (IsOpenFloor(gridUid, gridComponent, centre + offset))
                return _mapSystem.GridTileToLocal(gridUid, gridComponent, centre + offset);
        }

        return null;
    }

    /// <summary>
    ///     Any open floor tile on a grid.
    /// </summary>
    public EntityCoordinates? RandomSpot(EntityUid gridUid)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return null;

        var tiles = new List<Vector2i>();
        var enumerator = _mapSystem.GetAllTilesEnumerator(gridUid, gridComponent);
        while (enumerator.MoveNext(out var tileRef))
            tiles.Add(tileRef.Value.GridIndices);

        for (var attempt = 0; attempt < 50 && tiles.Count > 0; attempt++)
        {
            var tile = _robustRandom.PickAndTake(tiles);
            if (IsOpenFloor(gridUid, gridComponent, tile))
                return _mapSystem.GridTileToLocal(gridUid, gridComponent, tile);
        }

        return null;
    }

    private bool IsOpenFloor(EntityUid gridUid, MapGridComponent gridComponent, Vector2i tile)
    {
        return _mapSystem.TryGetTileRef(gridUid, gridComponent, tile, out var tileRef) && !tileRef.Tile.IsEmpty
            && !_turfSystem.IsSpace(tileRef) && !_turfSystem.IsTileBlocked(gridUid, tile, CollisionGroup.Impassable, gridComponent);
    }

    #endregion
}
