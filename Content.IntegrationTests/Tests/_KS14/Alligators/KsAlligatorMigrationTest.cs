using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.Alligators;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Alligators;

[TestFixture]
[TestOf(typeof(KsAlligatorMigrationRule))]
public sealed class KsAlligatorMigrationTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId Migration = "KsAlligatorMigration";
    private static readonly EntProtoId Wall = "WallSolid";
    private static readonly ProtoId<ContentTileDefinition> MaintenanceFloor = "FloorTechMaint";
    private static readonly ProtoId<EntityTablePrototype> CalmEvents = "BasicCalmEventsTable";

    [TestCase(true)]
    [TestCase(false)]
    public async Task MigrationSpawnsOnlyOnClearMaintenanceTiles(bool hasClearMaintenance)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var table = SEntMan.System<EntityTableSystem>();
            Assert.That(table.GetSpawns(SProtoMan.Index(CalmEvents)), Does.Contain(Migration));

            var station = SSpawn(null);
            SEntMan.AddComponent<StationDataComponent>(station);
            SEntMan.AddComponent<StationEventEligibleComponent>(station);
            SEntMan.System<StationSystem>().AddGridToStation(station, map.Grid);

            var ticker = SEntMan.System<GameTicker>();
            var rule = ticker.AddGameRule(Migration);
            var count = SComp<KsAlligatorMigrationRuleComponent>(rule).SpawnCount;
            var maps = SEntMan.System<SharedMapSystem>();
            var maintenance = new Tile(SProtoMan.Index(MaintenanceFloor).TileId);
            var expected = new HashSet<Vector2i>();

            // The origin is standard plating. Provide a blocked maintenance tile in both cases.
            var blocked = new Vector2i(1, 0);
            maps.SetTile(map.Grid, blocked, maintenance);
            SSpawnAtPosition(Wall, maps.GridTileToLocal(map.Grid, map.Grid.Comp, blocked));

            for (var i = 0; hasClearMaintenance && i < count; i++)
            {
                var indices = new Vector2i(i + 2, 0);
                maps.SetTile(map.Grid, indices, maintenance);
                expected.Add(indices);
            }

            Assert.That(ticker.StartGameRule(rule), Is.True);

            var occupants = SEntMan.EntityQuery<KsAlligatorComponent, TransformComponent>()
                .Where(entry => entry.Item2.GridUid == map.Grid.Owner)
                .Select(entry => maps.TileIndicesFor(map.Grid, map.Grid.Comp, entry.Item2.Coordinates));
            Assert.That(occupants, Is.EquivalentTo(expected));
        });
    }
}
