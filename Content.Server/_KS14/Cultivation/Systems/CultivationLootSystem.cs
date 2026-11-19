using Content.Shared.GameTicking;
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
    public static readonly EntProtoId[] QiManuals =
    {
        "CultivationManualMetal", "CultivationManualWood", "CultivationManualWater", "CultivationManualFire", "CultivationManualEarth",
    };

    public static readonly EntProtoId[] MartialManuals =
    {
        "MartialManualDragonPalms", "MartialManualSixMeridians", "MartialManualNorthernDarkness", "MartialManualStarAbsorbing",
        "MartialManualGreatShift", "MartialManualNineYang", "MartialManualGoldenBell", "MartialManualGracefulSteps",
        "MartialManualLadderCloud", "MartialManualDugu", "MartialManualMuscleTendon", "MartialManualDrunkenFist",
        "MartialManualEagleClaw", "MartialManualWingChun",
    };

    public static readonly EntProtoId BodyMoldingArt = "BodyManualMoldingArt";

    /// <summary>
    ///     The treasured armours of the jianghu, which only an old dynasty's treasury still keeps.
    /// </summary>
    public static readonly EntProtoId[] LegendaryArmours =
    {
        "ClothingOuterSoftHedgehogArmor", "ClothingOuterGoldenSilkVest", "ClothingOuterGoldenChainMail",
        "ClothingHeadPhoenixWingCrown", "ClothingShoesCloudStepping",
    };
    private static readonly EntProtoId BodyPrimer = "BodyManualCopperSkinPrimer";
    private static readonly EntProtoId DemonicScripture = "KsDemonicScripture";
    private static readonly EntProtoId AncestralRing = "KsAncestralRing";

    /// <summary>
    ///     One legendary turns up each round. The pairs come together: Ganjiang and Moye side by side, the sword and the
    ///         saber in two different places.
    /// </summary>
    private static readonly EntProtoId[][] Legendaries =
    {
        new EntProtoId[] { "KsGanjiang", "KsMoye" },
        new EntProtoId[] { "KsHeavenReliantSword" },
        new EntProtoId[] { "KsRuyiJinguBang" },
        new EntProtoId[] { "KsPurpleGoldGourd" },
        new EntProtoId[] { "KsPlantainFan" },
        new EntProtoId[] { "KsBaguaMirror" },
        new EntProtoId[] { "KsQiankunPouch" },
        new EntProtoId[] { "KsXuanyuanSword" },
        new EntProtoId[] { "KsGreenDragonBlade" },
        new EntProtoId[] { "KsSevenStarSword" },
        new EntProtoId[] { "KsLinglongPagoda" },
        new EntProtoId[] { "KsUniverseRing" },
        new EntProtoId[] { "KsWindFireWheels" },
    };

    private static readonly EntProtoId DragonSaber = "KsDragonSlayingSaber";

    private const int MaintenanceQiManuals = 3;
    private const int MaintenanceMartialManuals = 2;
    private const float ScriptureChance = 0.3f;
    private const int BountifulQiManuals = 5;
    private const int BountifulMartialManuals = 7;
    private const int BountifulBodyArts = 3;
    private const int BountifulLegendaries = 3;

    private static readonly HashSet<string> LegendaryIds = new() { "KsDragonSlayingSaber" };

    static CultivationLootSystem()
    {
        foreach (var legendary in Legendaries)
            LegendaryIds.Add(legendary[0]);
    }

    /// <summary>
    ///     Legendaries already handed out this round, so none turns up twice.
    /// </summary>
    private readonly HashSet<string> _spawnedLegendaries = new();

    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    ///     Hide the round's cultivation loot about a station grid.
    /// </summary>
    public void SpawnLoot(EntityUid gridUid, bool bountiful = false)
    {
        var maintenance = MaintenanceSpots(gridUid);
        if (bountiful)
        {
            // Wuxia: Ascension. Everyone is cultivating, so there's far more to find: every element's manual at least
            //     once, a heap of martial arts, several copies of the body's arts, and three different legendaries.
            foreach (var manual in QiManuals)
                SpawnAt(manual, maintenance, gridUid);
        }

        for (var i = 0; i < (bountiful ? BountifulQiManuals : MaintenanceQiManuals); i++)
            SpawnAt(_robustRandom.Pick(QiManuals), maintenance, gridUid);

        // Martial manuals are drawn without repeats until every one is out.
        var martial = new List<EntProtoId>(MartialManuals);
        for (var i = 0; i < (bountiful ? BountifulMartialManuals : MaintenanceMartialManuals) && martial.Count > 0; i++)
            SpawnAt(_robustRandom.PickAndTake(martial), maintenance, gridUid);

        SpawnAt(AncestralRing, maintenance, gridUid);
        for (var i = 0; i < (bountiful ? BountifulLegendaries : 1); i++)
            SpawnLegendary(maintenance, gridUid);

        for (var i = 0; i < (bountiful ? BountifulBodyArts : 1); i++)
        {
            SpawnAt(BodyMoldingArt, maintenance, gridUid);
            SpawnAt(BodyPrimer, maintenance, gridUid);
        }

        if (bountiful || _robustRandom.Prob(ScriptureChance))
            SpawnAt(DemonicScripture, maintenance, gridUid);

        var library = SpotsBeside(gridUid, "Bookshelf");
        for (var i = 0; i < (bountiful ? 2 : 1); i++)
        {
            SpawnAt(_robustRandom.Pick(QiManuals), library, gridUid);
            SpawnAt(BodyPrimer, library, gridUid);
        }
    }

    /// <summary>
    ///     Pick a legendary (or pair) that isn't already in the round, or null when every one is out. The same
    ///         artifact never turns up twice.
    /// </summary>
    private EntProtoId[]? PickLegendary()
    {
        var present = new HashSet<string>();
        var query = EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metaData))
        {
            if (metaData.EntityPrototype is { } prototype && LegendaryIds.Contains(prototype.ID))
                present.Add(prototype.ID);
        }

        var choices = new List<EntProtoId[]>();
        foreach (var legendary in Legendaries)
        {
            if (!_spawnedLegendaries.Contains(legendary[0]) && !present.Contains(legendary[0]))
                choices.Add(legendary);
        }

        if (choices.Count == 0)
            return null;

        var chosen = _robustRandom.Pick(choices);
        _spawnedLegendaries.Add(chosen[0]);
        return chosen;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _spawnedLegendaries.Clear();
    }

    /// <summary>
    ///     A legendary artifact (or pair) appears at a spot, as from the Imperial Treasury.
    /// </summary>
    public bool SpawnLegendaryAt(MapCoordinates coordinates)
    {
        if (PickLegendary() is not { } legendary)
            return false;

        foreach (var artifact in legendary)
            Spawn(artifact, coordinates);
        return true;
    }

    /// <summary>
    ///     Hide one legendary artifact (or pair) in the maintenance tunnels.
    /// </summary>
    public void SpawnLegendary(List<EntityCoordinates> spots, EntityUid gridUid)
    {
        if (PickLegendary() is not { } legendary)
            return;

        var first = spots.Count > 0 ? _robustRandom.PickAndTake(spots) : RandomSpot(gridUid);
        if (first is not { } spot)
            return;

        foreach (var prototype in legendary)
            Spawn(prototype, spot);

        // The Heaven Reliant Sword's other half lies somewhere else entirely.
        if (legendary[0] == "KsHeavenReliantSword")
            SpawnAt(DragonSaber, spots, gridUid);
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
