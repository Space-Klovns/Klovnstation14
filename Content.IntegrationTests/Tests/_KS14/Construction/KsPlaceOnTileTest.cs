#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._KS14.Construction.TilePlacement;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Construction;

/// <summary>
///     Clicking a tile with a <see cref="KsPlaceOnTileComponent"/> item builds its structure there once the do-after
///         ends, paying for it from the stack or with the item, and refuses a tile that is taken. The player clicks
///         through their own client, as in game, so the client predicts it.
/// </summary>
[TestFixture]
[TestOf(typeof(KsPlaceOnTileSystem))]
public sealed class KsPlaceOnTileTest : MovementTest
{
    private readonly EntProtoId _bags = "KsPlaceOnTileTestBags";
    private readonly EntProtoId _coil = "KsPlaceOnTileTestCoil";
    private readonly EntProtoId _wall = "KsPlaceOnTileTestWall";
    private readonly EntProtoId _wire = "KsPlaceOnTileTestWire";

    [TestPrototypes]
    private const string Prototypes = @"
- type: stack
  id: KsPlaceOnTileTestStack
  name: stack-steel # any existing name: the test is about placing, not the stack
  spawn: KsPlaceOnTileTestBags
  maxCount: 30

- type: entity
  parent: BaseItem
  id: KsPlaceOnTileTestBags
  components:
  - type: Stack
    stackType: KsPlaceOnTileTestStack
    count: 10
  - type: KsPlaceOnTile
    prototype: KsPlaceOnTileTestWall
    doAfter: 1
    stackCost: 3

- type: entity
  parent: BaseItem
  id: KsPlaceOnTileTestCoil
  components:
  - type: KsPlaceOnTile
    prototype: KsPlaceOnTileTestWire
    doAfter: 0.5

- type: entity
  id: KsPlaceOnTileTestWall
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        mask:
        - FullTileMask
        hard: true

- type: entity
  id: KsPlaceOnTileTestWire
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.4,-0.4,0.4,0.4""
        layer:
        - MidImpassable
        hard: false
  # Joined into lines like barbed wire, to check the neighbours through the predicted stand-in.
  - type: Sprite
    sprite: _KS14/Structures/barbed_wire.rsi
    layers:
    - state: wire
      map: [ ""wireVertical"" ]
    - state: wire
      map: [ ""wireHorizontal"" ]
    - state: post
      map: [ ""postNorth"" ]
    - state: post
      map: [ ""postSouth"" ]
    - state: post
      map: [ ""postEast"" ]
    - state: post
      map: [ ""postWest"" ]
  - type: KsLineSmooth
    key: ksPlaceOnTileTest
";

    /// <summary>
    ///     How far ahead of the server the client predicts, standing in for latency.
    /// </summary>
    private const int PredictedAheadTicks = 8;

    private Vector2i TargetTile => MapSystem.TileIndicesFor(MapData.Grid, SEntMan.GetCoordinates(TargetCoords));

    [Test]
    public async Task StackBuildsAfterTheDoAfterAndPaysItsCost()
    {
        var bags = await PlaceInHands(_bags, 10);
        var placeOnTile = SEntMan.GetComponent<KsPlaceOnTileComponent>(ToServer(bags));

        await ClickTarget();
        await Server.WaitAssertion(() =>
        {
            Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1), "clicking a tile should start building");
            Assert.That(ServerOnTile(_wall, TargetTile), Is.Empty, "nothing should be built before the do-after ends");
        });

        await AwaitDoAfters();

        await Server.WaitAssertion(() =>
        {
            var built = ServerOnTile(_wall, TargetTile);
            Assert.That(built, Has.Count.EqualTo(1), "the do-after ending should build one wall on the clicked tile");
            var builtTransform = SEntMan.GetComponent<TransformComponent>(built[0]);
            Assert.That(builtTransform.Anchored, "the wall should be anchored");
            Assert.That(builtTransform.LocalPosition, Is.EqualTo(MapSystem.TileCenterToVector(MapData.Grid, TargetTile)),
                "the wall should stand on the middle of the tile");
            Assert.That(SEntMan.GetComponent<StackComponent>(ToServer(bags)).Count, Is.EqualTo(10 - placeOnTile.StackCost),
                "the stack should pay the build's cost, and no more");
        });
    }

    [Test]
    public async Task SingleItemIsUsedUp()
    {
        var coil = await PlaceInHands(_coil);

        await ClickTarget();
        await AwaitDoAfters();

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerOnTile(_wire, TargetTile), Has.Count.EqualTo(1), "the coil should unroll onto the clicked tile");
            Assert.That(SEntMan.Deleted(ToServer(coil)) || SEntMan.IsQueuedForDeletion(ToServer(coil)),
                "an item that isn't a stack should be used up by building");
        });
    }

    /// <summary>
    ///     A wall on the tile refuses it: no do-after, nothing built, nothing paid.
    /// </summary>
    [Test]
    public async Task TakenTileIsRefused()
    {
        await SpawnTarget(_wall);
        var bags = await PlaceInHands(_bags, 10);

        await ClickTarget();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ActiveDoAfters.Any(), Is.False, "a taken tile shouldn't start building");
            Assert.That(ServerOnTile(_wall, TargetTile), Has.Count.EqualTo(1), "nothing more should be built on a taken tile");
            Assert.That(SEntMan.GetComponent<StackComponent>(ToServer(bags)).Count, Is.EqualTo(10), "a refused build shouldn't cost anything");
        });
    }

    /// <summary>
    ///     A structure with no hard fixtures blocks nothing, so only the same structure on the tile stops a second one.
    /// </summary>
    [Test]
    public async Task SameStructureIsNotBuiltTwice()
    {
        await PlaceInHands(_coil);
        await ClickTarget();
        await AwaitDoAfters();

        var secondCoil = await PlaceInHands(_coil);
        await ClickTarget();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ActiveDoAfters.Any(), Is.False, "a tile with the same structure on it shouldn't start building");
            Assert.That(ServerOnTile(_wire, TargetTile), Has.Count.EqualTo(1), "a second wire shouldn't go onto the first");
            Assert.That(SEntMan.EntityExists(ToServer(secondCoil)), "a refused build shouldn't use up the coil");
        });
    }

    [Test]
    public async Task StackTooSmallIsRefused()
    {
        var bags = await PlaceInHands(_bags, 1);
        var placeOnTile = SEntMan.GetComponent<KsPlaceOnTileComponent>(ToServer(bags));
        Assert.That(placeOnTile.StackCost, Is.GreaterThan(1), "the test needs a cost a single bag can't pay");

        await ClickTarget();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ActiveDoAfters.Any(), Is.False, "a stack that can't pay shouldn't start building");
            Assert.That(ServerOnTile(_wall, TargetTile), Is.Empty);
            Assert.That(SEntMan.GetComponent<StackComponent>(ToServer(bags)).Count, Is.EqualTo(1));
        });
    }

    /// <summary>
    ///     The builder's client shows the wall the moment its own do-after ends, before the server has built it, and
    ///         ends up with the server's wall alone, not a leftover stand-in beside it.
    /// </summary>
    [Test]
    public async Task ClientPredictsTheStructure()
    {
        await PlaceInHands(_bags, 10);

        var clientFirst = await ClickAndWatch(_wall);

        Assert.That(clientFirst, "the client should show the wall before the server confirms it");
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var onClient = ClientOnTile(_wall, TargetTile);
            Assert.That(onClient, Has.Count.EqualTo(1), "the client should end up with exactly one wall");
            Assert.That(CEntMan.IsClientSide(onClient[0]), Is.False, "the client's wall should be the server's, not its stand-in");
        });
    }

    /// <summary>
    ///     A line-joined structure built next to one already there joins it into a line on the builder's client, through
    ///         the stand-in coming and going: the old piece ends capped towards the new one, not left alone.
    /// </summary>
    [Test]
    public async Task LineJoinsItsNeighbourThroughThePrediction()
    {
        var neighbourTile = TargetTile + Vector2i.Right;
        var neighbour = await Spawn(_wire, SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid, MapSystem.TileCenterToVector(MapData.Grid, neighbourTile))));
        await RunTicks(5);
        Assert.That(await ShownLayers(ToClient(neighbour)), Is.EquivalentTo(new[] { "postNorth", "postSouth" }),
            "a lone piece should show a post at each end before the new one is built");

        await PlaceInHands(_coil);
        Assert.That(await ClickAndWatch(_wire), "the client should show the new piece before the server confirms it");
        await RunTicks(10);

        Assert.That(await ShownLayers(ToClient(neighbour)), Is.EquivalentTo(new[] { "postWest" }),
            "the old piece should be capped towards the new one to its west");

        EntityUid built = default;
        await Client.WaitAssertion(() =>
        {
            var onClient = ClientOnTile(_wire, TargetTile);
            Assert.That(onClient, Has.Count.EqualTo(1), "the client should end up with exactly one new piece");
            built = onClient[0];
        });
        Assert.That(await ShownLayers(built), Is.EquivalentTo(new[] { "postEast" }),
            "the new piece should be capped towards the old one to its east");
    }

    private async Task ClickTarget()
    {
        await PressKey(EngineKeyFunctions.Use, coordinates: TargetCoords);
    }

    /// <summary>
    ///     Clicks the target tile with the client predicting well ahead of the server, and watches until the server has
    ///         built <paramref name="prototype"/> there. True if the client showed it first.
    /// </summary>
    private async Task<bool> ClickAndWatch(EntProtoId prototype)
    {
        var configurationManager = Client.ResolveDependency<IConfigurationManager>();
        var oldBias = 0;
        await Client.WaitPost(() =>
        {
            oldBias = configurationManager.GetCVar(CVars.NetPredictTickBias);
            configurationManager.SetCVar(CVars.NetPredictTickBias, PredictedAheadTicks);
        });

        try
        {
            await RunTicks(PredictedAheadTicks * 2);
            await ClickTarget();

            var clientFirst = false;
            for (var tick = 0; tick < (int)STiming.TickRate * 10; tick++)
            {
                await RunTicks(1);
                var onClient = 0;
                var onServer = 0;
                await Client.WaitPost(() => onClient = ClientOnTile(prototype, TargetTile).Count);
                await Server.WaitPost(() => onServer = ServerOnTile(prototype, TargetTile).Count);

                if (onClient > 0 && onServer == 0)
                    clientFirst = true;

                if (onServer > 0)
                    break;
            }

            await Server.WaitAssertion(() => Assert.That(ServerOnTile(prototype, TargetTile), Has.Count.EqualTo(1), "the server should build it"));
            return clientFirst;
        }
        finally
        {
            await Client.WaitPost(() => configurationManager.SetCVar(CVars.NetPredictTickBias, oldBias));
        }
    }

    private List<EntityUid> ServerOnTile(EntProtoId prototype, Vector2i tile)
    {
        return OnTile(SEntMan, MapData.Grid.Owner, prototype, tile);
    }

    private List<EntityUid> ClientOnTile(EntProtoId prototype, Vector2i tile)
    {
        return OnTile(CEntMan, CEntMan.GetEntity(SEntMan.GetNetEntity(MapData.Grid.Owner)), prototype, tile);
    }

    private static List<EntityUid> OnTile(IEntityManager entityManager, EntityUid gridUid, EntProtoId prototype, Vector2i tile)
    {
        var mapSystem = entityManager.System<SharedMapSystem>();
        var gridComponent = entityManager.GetComponent<MapGridComponent>(gridUid);
        var found = new List<EntityUid>();
        var query = entityManager.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var metaDataComponent, out var transformComponent))
        {
            if (metaDataComponent.EntityPrototype?.ID != prototype.Id ||
                transformComponent.GridUid != gridUid ||
                entityManager.IsQueuedForDeletion(uid) ||
                metaDataComponent.EntityLifeStage >= EntityLifeStage.Terminating)
            {
                continue;
            }

            if (mapSystem.TileIndicesFor((gridUid, gridComponent), transformComponent.Coordinates) == tile)
                found.Add(uid);
        }

        return found;
    }

    private static readonly string[] WireLayers = ["wireVertical", "wireHorizontal", "postNorth", "postSouth", "postEast", "postWest"];

    private async Task<HashSet<string>> ShownLayers(EntityUid clientUid)
    {
        var shown = new HashSet<string>();
        await Client.WaitAssertion(() =>
        {
            var spriteComponent = CEntMan.GetComponent<SpriteComponent>(clientUid);
            var spriteSystem = CEntMan.System<SpriteSystem>();
            foreach (var layer in WireLayers)
            {
                Assert.That(spriteSystem.LayerMapTryGet((clientUid, spriteComponent), layer, out var index, logMissing: false));
                if (spriteComponent[index].Visible)
                    shown.Add(layer);
            }
        });

        return shown;
    }
}
