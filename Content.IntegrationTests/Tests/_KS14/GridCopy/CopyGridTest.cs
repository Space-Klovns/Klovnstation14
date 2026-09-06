#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.GridCopy;
using Content.Shared.Mind.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.GridCopy;

public sealed class CopyGridTest : GameTest
{
    private static List<(Vector2i, Tile)> SquareTiles(int size)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
            tiles.Add((new Vector2i(x, y), new Tile(1)));
        return tiles;
    }

    /// <summary>A copy is a distinct grid on the same map, at the requested offset, with the same tiles.</summary>
    [Test]
    public async Task TestCopyGridBasic()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var gridCopy = entManager.System<GridCopySystem>();

        var grid = default(Entity<MapGridComponent>);
        Entity<MapGridComponent>? copy = null;
        string? error = null;
        var ok = false;

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            grid = mapManager.CreateGridEntity(map.MapId);
            mapSystem.SetTiles(grid.Owner, grid.Comp, SquareTiles(4));
            xformSystem.SetLocalPosition(grid.Owner, new Vector2(100f, 50f));

            ok = gridCopy.TryCopyGrid(grid, new Vector2(20f, 0f), Angle.Zero, out copy, out error);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(ok, Is.True, $"copy failed: {error}");
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy!.Value.Owner, Is.Not.EqualTo(grid.Owner), "copy reused the source grid");

            Assert.That(entManager.GetComponent<TransformComponent>(copy.Value.Owner).MapID, Is.EqualTo(map.MapId));

            var origPos = xformSystem.GetWorldPosition(grid.Owner);
            var copyPos = xformSystem.GetWorldPosition(copy.Value.Owner);
            Assert.That(copyPos.X, Is.EqualTo(origPos.X + 20f).Within(0.05f));
            Assert.That(copyPos.Y, Is.EqualTo(origPos.Y).Within(0.05f));

            var origTiles = mapSystem.GetAllTiles(grid.Owner, grid.Comp!).Count();
            var copyTiles = mapSystem.GetAllTiles(copy.Value.Owner, copy.Value.Comp).Count();
            Assert.That(copyTiles, Is.EqualTo(origTiles).And.GreaterThan(0));
        });
    }

    /// <summary>
    ///     Rotating the copy pivots it about its own origin, so it lands next to the original, NOT flung across
    ///     the map about the map origin (guards the merge-path rotation quirk).
    /// </summary>
    [Test]
    public async Task TestCopyGridRotationStaysInPlace()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var gridCopy = entManager.System<GridCopySystem>();

        var grid = default(Entity<MapGridComponent>);
        Entity<MapGridComponent>? copy = null;
        var ok = false;
        string? error = null;

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            grid = mapManager.CreateGridEntity(map.MapId);
            mapSystem.SetTiles(grid.Owner, grid.Comp, SquareTiles(4));
            // Far from the map origin: a map-origin pivot would fling the copy ~200 tiles away.
            xformSystem.SetLocalPosition(grid.Owner, new Vector2(200f, 0f));

            ok = gridCopy.TryCopyGrid(grid, Vector2.Zero, Angle.FromDegrees(90), out copy, out error);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(ok, Is.True, $"copy failed: {error}");
            Assert.That(copy, Is.Not.Null);

            // Zero offset + in-place rotation => copy stays where the original is.
            var origPos = xformSystem.GetWorldPosition(grid.Owner);
            var copyPos = xformSystem.GetWorldPosition(copy!.Value.Owner);
            Assert.That(copyPos.X, Is.EqualTo(origPos.X).Within(0.05f), "rotation flung the copy off the X axis");
            Assert.That(copyPos.Y, Is.EqualTo(origPos.Y).Within(0.05f), "rotation flung the copy off the Y axis");

            var copyRot = xformSystem.GetWorldRotation(copy.Value.Owner);
            Assert.That(copyRot.Degrees, Is.EqualTo(90d).Within(0.5d));
        });
    }

    /// <summary>
    ///     The command's 'abs' mode pastes the copy's origin at absolute map coordinates,
    ///         wherever the original sits. Driven through the real console command so the
    ///         argument shift ('abs' occupying the X slot) is under test.
    /// </summary>
    [Test]
    public async Task TestCopyGridAbsoluteCoordinates()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var conHost = server.ResolveDependency<IConsoleHost>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var grid = default(Entity<MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            grid = mapManager.CreateGridEntity(map.MapId);
            mapSystem.SetTiles(grid.Owner, grid.Comp, SquareTiles(4));
            // Away from the origin, so absolute and relative placement cannot coincide.
            xformSystem.SetLocalPosition(grid.Owner, new Vector2(100f, 50f));

            conHost.ExecuteCommand($"copygrid {entManager.GetNetEntity(grid.Owner)} abs 40 -25");
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            EntityUid? copy = null;
            var query = entManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId && uid != grid.Owner)
                    copy = uid;
            }

            Assert.That(copy, Is.Not.Null, "the command produced no copy");

            var copyPos = xformSystem.GetWorldPosition(copy!.Value);
            Assert.That(copyPos.X, Is.EqualTo(40f).Within(0.05f), "the copy's origin missed the absolute X");
            Assert.That(copyPos.Y, Is.EqualTo(-25f).Within(0.05f), "the copy's origin missed the absolute Y");
        });
    }

    /// <summary>Minded mobs aboard the source grid are stripped from the copy, so their minds aren't duplicated.</summary>
    [Test]
    public async Task TestCopyGridStripsMindedMobs()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var mindSystem = entManager.System<Content.Server.Mind.MindSystem>();
        var gridCopy = entManager.System<GridCopySystem>();

        var grid = default(Entity<MapGridComponent>);
        Entity<MapGridComponent>? copy = null;
        var ok = false;
        string? error = null;
        var mobHadMind = false;

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            grid = mapManager.CreateGridEntity(map.MapId);
            mapSystem.SetTiles(grid.Owner, grid.Comp, SquareTiles(4));

            var mob = entManager.SpawnEntity(null, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            var mind = mindSystem.CreateMind(null, "copygrid-test-mind");
            mindSystem.TransferTo(mind, mob, mind: mind.Comp);
            mobHadMind = entManager.TryGetComponent<MindContainerComponent>(mob, out var mc) && mc.HasMind;

            ok = gridCopy.TryCopyGrid(grid, new Vector2(20f, 0f), Angle.Zero, out copy, out error);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(mobHadMind, Is.True, "test setup failed: mob never got a mind");
            Assert.That(ok, Is.True, $"copy failed: {error}");
            Assert.That(copy, Is.Not.Null);

            var mindedOnCopy = 0;
            var query = entManager.AllEntityQueryEnumerator<MindContainerComponent, TransformComponent>();
            while (query.MoveNext(out _, out var mc, out var xform))
            {
                if (xform.GridUid == copy!.Value.Owner && mc.HasMind)
                    mindedOnCopy++;
            }

            Assert.That(mindedOnCopy, Is.EqualTo(0), "a minded mob was cloned onto the copy");
        });
    }
}
