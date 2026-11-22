#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Passages;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Ladders;
using Content.Shared.Atmos;
using Content.Shared._KS14.NPC.Passages;
using Content.Shared.NPC;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     An NPC steering for somewhere on another map gets there by a passage: a ladder, a portal.
/// </summary>
[TestOf(typeof(KsNpcPassageSystem))]
public sealed class KsNpcPassageTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId LadderUp = "KsLadderWall";
    private static readonly EntProtoId LadderDown = "KsLadderFloor";
    private static readonly EntProtoId StockPortal = "PortalRed";
    private static readonly EntProtoId ViewPortal = "KsViewPortal";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _walker = "KsNpcPassageTestWalker";
    private readonly EntProtoId _ladder = "KsNpcPassageTestLadder";
    private readonly EntProtoId _slowLadder = "KsNpcPassageTestSlowLadder";
    private readonly EntProtoId _portal = "KsNpcPassageTestPortal";
    private readonly EntProtoId _sameMapPortal = "KsNpcPassageTestSameMapPortal";
    private readonly EntProtoId _viewPortal = "KsNpcPassageTestViewPortal";

    private static readonly Vector2i Min = new(-7, -3);
    private static readonly Vector2i Max = new(7, 3);

    /// <summary>
    ///     How near its destination the walker is sent.
    /// </summary>
    private const float Range = 1.5f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsNpcPassageTestWalker

- type: entity
  id: KsNpcPassageTestLadder
  components:
  - type: KsLadder
  - type: KsNpcPassage
    range: 1.25

- type: entity
  id: KsNpcPassageTestSlowLadder
  components:
  - type: KsLadder
    useDelay: 1
  - type: KsNpcPassage
    range: 1.25

# A portal that takes whatever touches it, and only to its own map: across maps it fizzles.
- type: entity
  id: KsNpcPassageTestSameMapPortal
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      portalFixture:
        shape:
          !type:PhysShapeAabb
          bounds: '-0.25,-0.48,0.25,0.48'
        mask:
        - FullTileMask
        layer:
        - WallLayer
        hard: false
  - type: Portal
  - type: KsNpcPassage

- type: entity
  parent: KsNpcPassageTestSameMapPortal
  id: KsNpcPassageTestPortal
  components:
  - type: Portal
    canTeleportToOtherMaps: true

# One that carries across whatever crosses its doorway, and does nothing to what only touches it.
- type: entity
  parent: KsNpcPassageTestSameMapPortal
  id: KsNpcPassageTestViewPortal
  components:
  - type: Fixtures
    fixtures:
      portalFixture:
        shape:
          !type:PhysShapeAabb
          bounds: '-0.25,-0.48,0.25,0.48'
        mask:
        - FullTileMask
        layer:
        - WallLayer
        hard: false
      ksPortalSensor:
        shape:
          !type:PhysShapeAabb
          bounds: '-1,-1,1,1'
        mask:
        - FullTileMask
        layer:
        - WallLayer
        hard: false
  - type: KsPortalView
";

    /// <summary>
    ///     A map of its own with a floor and air, and its navmesh built.
    /// </summary>
    private async Task<EntityUid> MakeFloor()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entManager.System<SharedMapSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId);
            var grid = mapSystem.CreateGridEntity(mapId);
            var plating = new Tile(tileDefinitionManager["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();

            for (var x = Min.X; x <= Max.X; x++)
            {
                for (var y = Min.Y; y <= Max.Y; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
            gridUid = grid.Owner;

            // Air to breathe, so that nothing but the walking slows it.
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            entManager.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, space: false, new GasMixture(moles, Atmospherics.T20C));
        });

        await WaitForNavmesh(Pair, gridUid, Min, Max);
        return gridUid;
    }

    /// <summary>
    ///     A ladder on each of two floors, linked.
    /// </summary>
    private void LinkLadders(EntProtoId ladder, EntityUid firstGridUid, Vector2i firstTile, EntityUid secondGridUid, Vector2i secondTile)
    {
        var entManager = Pair.Server.EntMan;
        var firstUid = SpawnAt(entManager, ladder, firstGridUid, firstTile.X, firstTile.Y);
        var secondUid = SpawnAt(entManager, ladder, secondGridUid, secondTile.X, secondTile.Y);
        entManager.System<KsLadderSystem>().Link(firstUid, secondUid);
    }

    /// <summary>
    ///     A portal on each of two floors, linked.
    /// </summary>
    private void LinkPortals(EntProtoId portal, EntityUid firstGridUid, Vector2i firstTile, EntityUid secondGridUid, Vector2i secondTile)
    {
        var entManager = Pair.Server.EntMan;
        var firstUid = SpawnAt(entManager, portal, firstGridUid, firstTile.X, firstTile.Y);
        var secondUid = SpawnAt(entManager, portal, secondGridUid, secondTile.X, secondTile.Y);
        entManager.System<LinkedEntitySystem>().TryLink(firstUid, secondUid);
    }

    private EntityUid SendWalker(EntityUid gridUid, Vector2i tile, EntityCoordinates destination)
    {
        var entManager = Pair.Server.EntMan;
        var walkerUid = SpawnAt(entManager, _walker, gridUid, tile.X, tile.Y);
        entManager.EnsureComponent<ActiveNPCComponent>(walkerUid);
        entManager.System<NPCSteeringSystem>().Register(walkerUid, destination).Range = Range;
        return walkerUid;
    }

    /// <summary>
    ///     Runs until the walker's steering says it is where it was sent, or has given up, for up to a minute.
    /// </summary>
    private async Task<SteeringStatus> WaitForWalker(EntityUid walkerUid)
    {
        var server = Pair.Server;
        var status = SteeringStatus.Moving;

        for (var i = 0; i < 120 && status == SteeringStatus.Moving; i++)
        {
            await Pair.RunTicksSync(15);
            await server.WaitPost(() => status = server.EntMan.GetComponent<NPCSteeringComponent>(walkerUid).Status);
        }

        return status;
    }

    private async Task AssertArrived(EntityUid walkerUid, EntityCoordinates destination, SteeringStatus status)
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var transformSystem = server.System<SharedTransformSystem>();
            var walkerCoordinates = transformSystem.GetMapCoordinates(walkerUid);
            var destinationCoordinates = transformSystem.ToMapCoordinates(destination);

            Assert.That(walkerCoordinates.MapId, Is.EqualTo(destinationCoordinates.MapId),
                $"it should have taken the ladder to the map it was sent to, but its steering ended as {status} on the map it started on, at {walkerCoordinates.Position}");
            Assert.That(status, Is.EqualTo(SteeringStatus.InRange), "and gone on from the ladder to where it was sent");
            Assert.That(Vector2.Distance(walkerCoordinates.Position, destinationCoordinates.Position), Is.LessThanOrEqualTo(Range + 0.5f));
        });
    }

    /// <summary>
    ///     Sent to a spot on another map, it walks to the ladder that leads there, climbs it, and walks on from the
    ///         other end: whether the ladder is climbed at once or takes standing at for a while.
    /// </summary>
    [Test]
    public async Task TestWalkerTakesALadderToAnotherMap([Values] bool slowLadder)
    {
        var server = Pair.Server;
        var firstGridUid = await MakeFloor();
        var secondGridUid = await MakeFloor();
        var destination = new EntityCoordinates(secondGridUid, new Vector2(5.5f, 2.5f));
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            // The far end is nowhere near above the near one: a ladder leads where it is linked to.
            LinkLadders(slowLadder ? _slowLadder : _ladder, firstGridUid, new Vector2i(5, 0), secondGridUid, new Vector2i(-5, -2));
            walkerUid = SendWalker(firstGridUid, new Vector2i(-5, 0), destination);
        });

        var status = await WaitForWalker(walkerUid);
        await AssertArrived(walkerUid, destination, status);
    }

    /// <summary>
    ///     Two maps away, with a ladder beside it that also leads there in the end but by one ladder more, it takes
    ///         the way with the fewest ladders. Taking the nearest ladder that leads there at all, it would go up the
    ///         one beside it and, at the far end, straight back down it, for good.
    /// </summary>
    [Test]
    public async Task TestWalkerTakesTheFewestLadders()
    {
        var server = Pair.Server;
        var firstGridUid = await MakeFloor();
        var betweenGridUid = await MakeFloor();
        var lastGridUid = await MakeFloor();
        var longWayGridUid = await MakeFloor();
        var longWayOnGridUid = await MakeFloor();
        var destination = new EntityCoordinates(lastGridUid, new Vector2(5.5f, 2.5f));
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            // The short way: two ladders.
            LinkLadders(_ladder, firstGridUid, new Vector2i(5, 0), betweenGridUid, new Vector2i(-5, 0));
            LinkLadders(_ladder, betweenGridUid, new Vector2i(5, 2), lastGridUid, new Vector2i(-5, -2));

            // The long way, which begins beside the walker: three.
            LinkLadders(_ladder, firstGridUid, new Vector2i(-4, 0), longWayGridUid, new Vector2i(0, 0));
            LinkLadders(_ladder, longWayGridUid, new Vector2i(5, 0), longWayOnGridUid, new Vector2i(-5, 0));
            LinkLadders(_ladder, longWayOnGridUid, new Vector2i(5, 0), lastGridUid, new Vector2i(5, -2));

            walkerUid = SendWalker(firstGridUid, new Vector2i(-5, 0), destination);
        });

        var status = await WaitForWalker(walkerUid);
        await AssertArrived(walkerUid, destination, status);
    }

    /// <summary>
    ///     Sent to a spot on another map that a portal leads to, it walks into the portal and on from the other one:
    ///         whether the portal takes what touches it or what crosses its doorway.
    /// </summary>
    [Test]
    public async Task TestWalkerTakesAPortalToAnotherMap([Values] bool viewPortal)
    {
        var server = Pair.Server;
        var firstGridUid = await MakeFloor();
        var secondGridUid = await MakeFloor();
        var destination = new EntityCoordinates(secondGridUid, new Vector2(5.5f, 2.5f));
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            LinkPortals(viewPortal ? _viewPortal : _portal, firstGridUid, new Vector2i(5, 0), secondGridUid, new Vector2i(-5, -2));
            walkerUid = SendWalker(firstGridUid, new Vector2i(-5, 0), destination);
        });

        var status = await WaitForWalker(walkerUid);
        await AssertArrived(walkerUid, destination, status);
    }

    /// <summary>
    ///     A portal that fizzles when it is asked to cross maps is no way to another map: it is left alone, and there
    ///         is no path.
    /// </summary>
    [Test]
    public async Task TestWalkerLeavesAPortalThatCannotCrossMaps()
    {
        var server = Pair.Server;
        var firstGridUid = await MakeFloor();
        var secondGridUid = await MakeFloor();
        var destination = new EntityCoordinates(secondGridUid, new Vector2(5.5f, 2.5f));
        EntityUid walkerUid = default;
        EntityUid portalUid = default;

        await server.WaitPost(() =>
        {
            LinkPortals(_sameMapPortal, firstGridUid, new Vector2i(5, 0), secondGridUid, new Vector2i(-5, -2));
            portalUid = server.EntMan.GetComponent<TransformComponent>(firstGridUid).ChildEnumerator.MoveNext(out var childUid) ? childUid : default;
            walkerUid = SendWalker(firstGridUid, new Vector2i(-5, 0), destination);
        });

        var status = await WaitForWalker(walkerUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(status, Is.EqualTo(SteeringStatus.NoPath));
            Assert.That(server.EntMan.Deleted(portalUid), Is.False, "the portal should not have been walked into, which fizzles it out");
            Assert.That(server.EntMan.GetComponent<TransformComponent>(walkerUid).GridUid, Is.EqualTo(firstGridUid));
        });
    }

    /// <summary>
    ///     The ladders and portals of the game are passages: what a mapper places, an NPC takes. The portals get it
    ///         from a partial prototype, which stops applying without a word if upstream renames theirs.
    /// </summary>
    [Test]
    public async Task TestLaddersAndPortalsAreSetUpAsPassages()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { LadderUp, LadderDown, StockPortal, ViewPortal })
            {
                var prototype = server.ProtoMan.Index(id);
                Assert.That(prototype.TryComp<KsNpcPassageComponent>(out _, server.EntMan.ComponentFactory), Is.True,
                    $"{id} should be a passage for NPCs");
            }

            Assert.That(server.ProtoMan.Index(StockPortal).TryComp<PortalComponent>(out _, server.EntMan.ComponentFactory), Is.True,
                "and the stock portal should still be a portal");
        });
    }

    /// <summary>
    ///     With no ladder that leads to the map it was sent to, it has no path, as before there were ladders.
    /// </summary>
    [Test]
    public async Task TestWalkerWithNoLadderThereHasNoPath()
    {
        var server = Pair.Server;
        var firstGridUid = await MakeFloor();
        var secondGridUid = await MakeFloor();
        var elsewhereGridUid = await MakeFloor();
        var destination = new EntityCoordinates(secondGridUid, new Vector2(5.5f, 2.5f));
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            LinkLadders(_ladder, firstGridUid, new Vector2i(5, 0), elsewhereGridUid, new Vector2i(0, 0));
            walkerUid = SendWalker(firstGridUid, new Vector2i(-5, 0), destination);
        });

        var status = await WaitForWalker(walkerUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(status, Is.EqualTo(SteeringStatus.NoPath));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(walkerUid).GridUid, Is.EqualTo(firstGridUid),
                "it should not have climbed a ladder that leads somewhere else");
        });
    }
}
