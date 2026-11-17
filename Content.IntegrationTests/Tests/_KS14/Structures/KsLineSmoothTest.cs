#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Structures;
using Content.IntegrationTests.Fixtures;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Structures;

/// <summary>
///     Line-joined structures show, per axis, the wire where the line runs on both ways and only the capping post where
///         it ends, never a post over a wire; a lone piece shows a post at each end.
/// </summary>
[TestFixture]
[TestOf(typeof(KsLineSmoothSystem))]
public sealed class KsLineSmoothTest : GameTest
{
    private readonly EntProtoId _piece = "KsTestLineSmoothPiece";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestLineSmoothPiece
  components:
  - type: Transform
    anchored: true
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
    key: test
";

    private static readonly string[] Layers = ["wireVertical", "wireHorizontal", "postNorth", "postSouth", "postEast", "postWest"];

    /// <summary>
    ///     Spawns a piece on each of <paramref name="tiles"/> and returns the layers shown on the piece at
    ///         <paramref name="inspected"/>, as the client draws it.
    /// </summary>
    /// <param name="destroyed">A piece to delete once they have all been drawn, or null.</param>
    private async Task<HashSet<string>> ShownLayers(Vector2i[] tiles, Vector2i inspected, Vector2i? destroyed = null)
    {
        var testMap = await Pair.CreateTestMap();
        var serverUids = new Dictionary<Vector2i, EntityUid>();

        await Server.WaitPost(() =>
        {
            var mapSystem = Server.System<SharedMapSystem>();
            foreach (var tile in tiles)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, tile, testMap.Tile.Tile);
                serverUids[tile] = SSpawnAtPosition(_piece, new EntityCoordinates(testMap.Grid, tile.X + 0.5f, tile.Y + 0.5f));
            }
        });
        await Pair.RunTicksSync(5);

        if (destroyed is { } destroyedTile)
        {
            await Server.WaitPost(() => Server.EntMan.DeleteEntity(serverUids[destroyedTile]));
            await Pair.RunTicksSync(5);
        }

        var shown = new HashSet<string>();
        await Client.WaitAssertion(() =>
        {
            var clientUid = ToClientUid(serverUids[inspected]);
            var spriteComponent = CComp<SpriteComponent>(clientUid);
            var spriteSystem = Client.System<SpriteSystem>();
            foreach (var layer in Layers)
            {
                Assert.That(spriteSystem.LayerMapTryGet((clientUid, spriteComponent), layer, out var index, logMissing: false));
                if (spriteComponent[index].Visible)
                    shown.Add(layer);
            }
        });

        return shown;
    }

    [Test]
    public async Task LonePieceShowsBothPostsAndNoWire()
    {
        var shown = await ShownLayers([new Vector2i(0, 0)], new Vector2i(0, 0));
        // Spawned facing south, so it runs north to south.
        Assert.That(shown, Is.EquivalentTo(new[] { "postNorth", "postSouth" }));
    }

    [Test]
    public async Task MiddleOfALineShowsOnlyWire()
    {
        var shown = await ShownLayers([new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(0, 1)], new Vector2i(0, 0));
        Assert.That(shown, Is.EquivalentTo(new[] { "wireVertical" }));
    }

    [Test]
    public async Task EndOfALineShowsOnlyItsPost()
    {
        // The north end has its neighbour to the south: the post capping a line that runs south, and no wire under it.
        var shown = await ShownLayers([new Vector2i(0, 0), new Vector2i(0, 1)], new Vector2i(0, 1));
        Assert.That(shown, Is.EquivalentTo(new[] { "postSouth" }));
    }

    /// <summary>
    ///     A piece cut out of a line leaves its neighbours capped where it was, not still reaching for it.
    /// </summary>
    [Test]
    public async Task DestroyedPieceCapsItsNeighbours()
    {
        Vector2i[] line = [new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(0, 1)];

        var middle = await ShownLayers(line, new Vector2i(0, 0), destroyed: new Vector2i(0, 1));
        Assert.That(middle, Is.EquivalentTo(new[] { "postSouth" }), "the middle piece, with the north one gone");

        var end = await ShownLayers(line, new Vector2i(0, 1), destroyed: new Vector2i(0, 0));
        Assert.That(end, Is.EquivalentTo(new[] { "postNorth", "postSouth" }), "the north end, cut off from the line");
    }

    [Test]
    public async Task CornerCapsEachAxis()
    {
        var shown = await ShownLayers([new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(1, 0)], new Vector2i(0, 0));
        Assert.That(shown, Is.EquivalentTo(new[] { "postNorth", "postEast" }));
    }

    [Test]
    public async Task JunctionsJoinEachAxisOnItsOwn()
    {
        // A T: the line runs on north and south, and branches off east.
        var tee = await ShownLayers([new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(0, -1), new Vector2i(1, 0)], new Vector2i(0, 0));
        Assert.That(tee, Is.EquivalentTo(new[] { "wireVertical", "postEast" }));

        var cross = await ShownLayers(
            [new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(0, -1), new Vector2i(1, 0), new Vector2i(-1, 0)],
            new Vector2i(0, 0));
        Assert.That(cross, Is.EquivalentTo(new[] { "wireVertical", "wireHorizontal" }));
    }

    /// <summary>
    ///     A layer's offset turns with the entity, so one placed facing east must have its grid offset turned back:
    ///         two pixels east of centre stays two pixels east, whichever way the piece faces.
    /// </summary>
    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void LayerOffsetsStayAlongTheGrid(float degrees)
    {
        var rotation = Angle.FromDegrees(degrees);
        var gridPixels = new System.Numerics.Vector2(2f, 0f);
        var local = KsLineSmoothSystem.GetLocalOffset(rotation, gridPixels);
        var world = rotation.RotateVec(local) * EyeManager.PixelsPerMeter;
        Assert.That(world.X, Is.EqualTo(gridPixels.X).Within(0.001f));
        Assert.That(world.Y, Is.EqualTo(gridPixels.Y).Within(0.001f));
    }

    [TestCase(Direction.South, Direction.South, SpriteComponent.DirectionOffset.None)]
    [TestCase(Direction.South, Direction.North, SpriteComponent.DirectionOffset.Flip)]
    [TestCase(Direction.North, Direction.East, SpriteComponent.DirectionOffset.Clockwise)]
    [TestCase(Direction.North, Direction.West, SpriteComponent.DirectionOffset.CounterClockwise)]
    [TestCase(Direction.East, Direction.South, SpriteComponent.DirectionOffset.Clockwise)]
    public void LayersAreTurnedToTheirOwnDirection(Direction facing, Direction direction, SpriteComponent.DirectionOffset expected)
    {
        Assert.That(KsLineSmoothSystem.GetDirOffset(facing, direction), Is.EqualTo(expected));
    }
}
