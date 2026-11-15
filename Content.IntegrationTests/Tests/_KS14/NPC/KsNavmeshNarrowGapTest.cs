#nullable enable
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.CCVar;
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
///         half a tile beside it as floor, and paths ran past it where no mob fits. Something thinner than the gap
///         between the navmesh's points, such as a tree trunk, still counts (<c>PathfindingSystem.FitsBetweenPoints</c>).
/// </summary>
public sealed class KsNavmeshNarrowGapTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly Vector2i Gap = new(3, 3);

    private const string DirectionalWindow = "WindowDirectional";

    // A trunk as thin as a snowy tree's or a conifer's: a fifth of a tile wide, narrower than the gap between the
    //      navmesh's points.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestThinTrunk
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.1,-0.3,0.1,0.3""
        density: 1000
        layer:
        - WallLayer
";

    /// <summary>
    ///     The only way through a wall is one tile wide, walked through from below. With in the gap:
    ///         <list type="bullet">
    ///             <item>nothing: there is a way through;</item>
    ///             <item>a closet, centred or pushed a little to one side (which leaves half a tile beside it, too narrow
    ///                 for a mob): there is none, unless the path may push it out of the way
    ///                 (<see cref="PathFlags.Pushing"/>) and it isn't bolted to the floor;</item>
    ///             <item>a thin window along one side: there is;</item>
    ///             <item>a tree trunk, thin enough to fit between the navmesh's points: there is none;</item>
    ///             <item>a mob, mob pushing on or not: there is, as mobs move and the navmesh doesn't count them.</item>
    ///         </list>
    /// </summary>
    [TestCase(null, 0f, false, PathFlags.None, true)]
    [TestCase("ClosetSteelBase", 0f, false, PathFlags.None, false)]
    [TestCase("ClosetSteelBase", 0.15f, false, PathFlags.None, false)]
    [TestCase("ClosetSteelBase", 0f, false, PathFlags.Pushing, true)]
    [TestCase("ClosetSteelBase", 0.15f, false, PathFlags.Pushing, true)]
    [TestCase("ClosetSteelBase", 0f, true, PathFlags.Pushing, false)]
    [TestCase(DirectionalWindow, 0f, true, PathFlags.None, true)]
    [TestCase("KsTestThinTrunk", 0f, true, PathFlags.None, false)]
    [TestCase("MobHuman", 0f, false, PathFlags.None, true)]
    public async Task TestPartlyBlockedGap(string? obstacle, float offsetX, bool anchored, PathFlags flags, bool expectPath)
    {
        await OverrideCVar(Side.Server, CCVars.MovementMobPushing, true);

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
            var transformSystem = SEntMan.System<SharedTransformSystem>();

            // Along the side of the tile, the way through, rather than across it.
            if (obstacle == DirectionalWindow)
                transformSystem.SetLocalRotation(obstacleUid, Angle.FromDegrees(90));

            if (anchored && !SEntMan.GetComponent<TransformComponent>(obstacleUid).Anchored)
                transformSystem.AnchorEntity(obstacleUid);
            Assert.That(SEntMan.GetComponent<TransformComponent>(obstacleUid).Anchored, Is.EqualTo(anchored));
        });

        await WaitForNavmeshSettled(Pair, gridUid);

        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Server.WaitPost(() => pathTask = pathfindingSystem.GetPath(new EntityCoordinates(gridUid, new Vector2(Gap.X + 0.5f, 0.5f)),
            new EntityCoordinates(gridUid, new Vector2(Gap.X + 0.5f, 6.5f)),
            0f,
            PathfindingSystem.PathfindingCollisionLayer,
            PathfindingSystem.PathfindingCollisionMask,
            CancellationToken.None,
            flags));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "the path request never finished");
        Assert.That((await pathTask).Result, Is.EqualTo(expectPath ? PathResult.Path : PathResult.NoPath));
    }
}
