using Content.IntegrationTests.Fixtures;
using Content.Shared._Starlight.Atmos;
using Content.Shared._Starlight.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Plumbing;

/// <summary>
///     Regression tests for plumbing ducts and atmospherics pipes blocking each other.
///     Both node types implement <see cref="IPipeNode"/>, and the overlap check only compared direction and layer,
///         so a fluid duct counted as an overlapping pipe and got unanchored - even though the two are entirely
///         separate networks that never interact.
/// </summary>
[TestFixture]
[TestOf(typeof(PipeRestrictOverlapSystem))]
public sealed class PlumbingPipeOverlapTest : GameTest
{
    private const string GasPipeProto = "GasPipeStraight";
    private const string PlumbingDuctProto = "PlumbingDuctStraight";

    /// <summary>
    ///     A fluid duct and a gas pipe must be able to share a tile.
    /// </summary>
    [Test]
    public async Task TestPlumbingDuctDoesNotOverlapGasPipe()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;

        var (grid, gasPipe) = await SpawnOnTestGrid(GasPipeProto);

        EntityUid duct = default;

        await server.WaitAssertion(() => duct = entityMan.SpawnEntity(PlumbingDuctProto, new EntityCoordinates(grid, 0.5f, 0.5f)));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var overlapSystem = entityMan.System<PipeRestrictOverlapSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(overlapSystem.CheckOverlap(duct), Is.False,
                    "A fluid duct counted as overlapping a gas pipe");
                Assert.That(overlapSystem.CheckOverlap(gasPipe), Is.False,
                    "A gas pipe counted as overlapping a fluid duct");

                Assert.That(entityMan.GetComponent<TransformComponent>(duct).Anchored,
                    "The fluid duct got unanchored by the gas pipe on its tile");
                Assert.That(entityMan.GetComponent<TransformComponent>(gasPipe).Anchored,
                    "The gas pipe got unanchored by the fluid duct on its tile");
            });
        });
    }

    /// <summary>
    ///     Two gas pipes on one tile must still conflict.
    /// </summary>
    [Test]
    public Task TestGasPipesStillOverlap() => TestSameKindStillOverlaps(GasPipeProto);

    /// <summary>
    ///     Two fluid ducts on one tile must still conflict.
    /// </summary>
    [Test]
    public Task TestPlumbingDuctsStillOverlap() => TestSameKindStillOverlaps(PlumbingDuctProto);

    private async Task TestSameKindStillOverlaps(string proto)
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;

        var (grid, first) = await SpawnOnTestGrid(proto);

        EntityUid second = default;

        await server.WaitAssertion(() => second = entityMan.SpawnEntity(proto, new EntityCoordinates(grid, 0.5f, 0.5f)));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // The newcomer gets kicked back off the tile, which is what makes CheckOverlap read false afterwards.
                Assert.That(entityMan.GetComponent<TransformComponent>(second).Anchored, Is.False,
                    $"A second {proto} was allowed to anchor on an occupied tile");
                Assert.That(entityMan.GetComponent<TransformComponent>(first).Anchored,
                    $"The first {proto} got unanchored instead of the second one");
            });
        });
    }

    /// <summary>
    ///     The RPD placement check has to make the same distinction, otherwise it would deconstruct the gas pipe
    ///         it thinks is in the way of a duct.
    /// </summary>
    [Test]
    public async Task TestProposedPlumbingDoesNotConflictWithGasPipe()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityMan = server.EntMan;

        var (grid, gasPipe) = await SpawnOnTestGrid(GasPipeProto);

        await server.WaitAssertion(() =>
        {
            var overlapSystem = entityMan.System<PipeRestrictOverlapSystem>();

            var plumbingProposal = new PipeRestrictOverlapSystem.ProposedPipe(
                PipeDirection.Longitudinal,
                AtmosPipeLayer.Primary,
                PipeNodeKind.Plumbing);

            var atmosProposal = new PipeRestrictOverlapSystem.ProposedPipe(
                PipeDirection.Longitudinal,
                AtmosPipeLayer.Primary,
                PipeNodeKind.Atmospherics);

            Assert.Multiple(() =>
            {
                Assert.That(overlapSystem.CheckIfWouldConflict(grid, Vector2i.Zero, plumbingProposal), Is.Null,
                    "A proposed fluid duct conflicted with an existing gas pipe");
                Assert.That(overlapSystem.CheckIfWouldConflict(grid, Vector2i.Zero, atmosProposal), Is.EqualTo(gasPipe),
                    "A proposed gas pipe did not conflict with an existing gas pipe");
            });
        });
    }

    /// <summary>
    ///     Makes a one-tile grid with <paramref name="proto"/> anchored on it.
    /// </summary>
    private async Task<(EntityUid Grid, EntityUid Spawned)> SpawnOnTestGrid(string proto)
    {
        var pair = Pair;
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entityMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSys = entityMan.System<SharedMapSystem>();

        EntityUid gridUid = default;
        EntityUid spawned = default;

        await server.WaitAssertion(() =>
        {
            Entity<MapGridComponent> grid = mapMan.CreateGridEntity(testMap.MapId);
            gridUid = grid.Owner;

            mapSys.SetTile(grid, grid, Vector2i.Zero, new Tile(1));

            spawned = entityMan.SpawnEntity(proto, new EntityCoordinates(grid, 0.5f, 0.5f));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityMan.GetComponent<TransformComponent>(spawned).Anchored,
                $"{proto} did not anchor on spawn");
        });

        return (gridUid, spawned);
    }
}
