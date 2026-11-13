#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A windoor stands on one edge of its tile, and only that edge is a door to the navmesh: the rest of the tile is open
///         floor. A tile's points once shared their flags, so every point after the first the windoor covered was a Door too,
///         and steering, which lets doors on a Door node through its collision avoidance, walked into the closed windoor
///         beside it.
/// </summary>
public sealed class KsNpcWindoorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task TestOnlyTheWindoorsEdgeIsADoor(int facing)
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -3), new Vector2i(3, 3)).Owner;
            var windoorUid = SpawnAt(entManager, "Windoor", gridUid, 0, 0);
            entManager.System<SharedTransformSystem>().SetLocalRotation(windoorUid, Angle.FromDegrees(facing));
        });

        await Pair.RunTicksSync(90);

        // Turned anticlockwise from the south edge: east at 90, north at 180, west at 270. The far side is the open one.
        var edge = Angle.FromDegrees(facing).RotateVec(new Vector2(0f, -0.375f));
        var center = new Vector2(0.5f, 0.5f);

        await Pair.Server.WaitAssertion(() =>
        {
            var pathfindingSystem = entManager.System<PathfindingSystem>();
            var doorPoly = pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, center + edge));
            var openPoly = pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, center - edge));

            Assert.That(doorPoly, Is.Not.Null);
            Assert.That(openPoly, Is.Not.Null);
            Assert.That(doorPoly!.Data.Flags & PathfindingBreadcrumbFlag.Door, Is.Not.EqualTo(PathfindingBreadcrumbFlag.None),
                "the windoor's own edge is a door");
            Assert.That(openPoly!.Data.Flags & PathfindingBreadcrumbFlag.Door, Is.EqualTo(PathfindingBreadcrumbFlag.None),
                "the open side of its tile is floor, not a door");
            Assert.That(openPoly.Data.CollisionLayer, Is.Zero);
        });
    }
}
