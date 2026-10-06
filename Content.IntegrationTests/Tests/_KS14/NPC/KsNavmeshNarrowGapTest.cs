#nullable enable
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Something that only partly blocks a tile, leaving too little of it for a mob to get through, blocks the tile
///         (<c>PathfindingSystem.BlockNarrowGaps</c>). A closet pushed a little off the middle of its tile used to leave
///         half a tile beside it as floor, and paths ran past it where no mob fits.
/// </summary>
public sealed class KsNavmeshNarrowGapTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly Vector2i Gap = new(3, 3);

    /// <summary>
    ///     The only way through a wall is one tile wide, walked through from below. With a closet in it, centred or
    ///         pushed a little to one side - which leaves half a tile free beside it, too narrow for a mob - there is no
    ///         way through. With a thin window along one side of it, or nothing, there is.
    /// </summary>
    [TestCase(null, 0f, true)]
    [TestCase("ClosetSteelBase", 0f, false)]
    [TestCase("ClosetSteelBase", 0.15f, false)]
    [TestCase("WindowDirectional", 0f, true)]
    public async Task TestPartlyBlockedGap(string? obstacle, float offsetX, bool expectPath)
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var pathfindingSystem = SEntMan.System<PathfindingSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(0, 0), new Vector2i(6, 6)).Owner;

            for (var x = 0; x <= 6; x++)
            {
                if (x != Gap.X)
                    SpawnAt(SEntMan, "WallSolid", gridUid, x, Gap.Y);
            }

            if (obstacle == null)
                return;

            var obstacleUid = SEntMan.SpawnEntity(obstacle, new EntityCoordinates(gridUid, new Vector2(Gap.X + 0.5f + offsetX, Gap.Y + 0.5f)));

            // Along the side of the tile, the way through, rather than across it.
            if (obstacle == "WindowDirectional")
                SEntMan.System<SharedTransformSystem>().SetLocalRotation(obstacleUid, Angle.FromDegrees(90));
        });

        await Pair.RunTicksSync(90); // navmesh

        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Server.WaitPost(() => pathTask = pathfindingSystem.GetPath(new EntityCoordinates(gridUid, new Vector2(Gap.X + 0.5f, 0.5f)),
            new EntityCoordinates(gridUid, new Vector2(Gap.X + 0.5f, 6.5f)),
            0f,
            PathfindingSystem.PathfindingCollisionLayer,
            PathfindingSystem.PathfindingCollisionMask,
            CancellationToken.None));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "the path request never finished");
        Assert.That((await pathTask).Result, Is.EqualTo(expectPath ? PathResult.Path : PathResult.NoPath));
    }
}
