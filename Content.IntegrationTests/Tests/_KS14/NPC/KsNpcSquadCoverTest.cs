#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Room detection and threshold cover. The test room is 7x6 tiles of floor (x 0..6, y 0..5) walled in
///         on a 21x21 grid, with an airlock in the north wall, a window in the east wall, and a west opening
///         that is either an airlock or a bare archway. The squad of two stands inside it.
/// </summary>
public sealed class KsNpcSquadCoverTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const int RoomTileCount = 7 * 6;

    private static readonly Vector2i NorthDoor = new(3, 6);
    private static readonly Vector2i WestOpening = new(-1, 2);
    private static readonly Vector2i WindowTile = new(7, 2);

    /// <summary>
    ///     Enough for the pathfinding graph to build and the squad to form.
    /// </summary>
    private const int SettleTicks = 150;

    [Test]
    public async Task TestRoomWithDoorsIsCoveredFromGoodPositions()
    {
        var (entManager, squadMembers) = await SetUpRoom(RoomKind.TwoDoors);
        var coverSystem = entManager.System<NpcSquadCoverSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var examineSystem = entManager.System<ExamineSystemShared>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var assignments = new List<NpcSquadCoverAssignment>();
            foreach (var memberUid in squadMembers)
            {
                Assert.That(coverSystem.TryGetAssignment(memberUid, out var assignment), "every member should get a threshold");
                assignments.Add(assignment);
            }

            Assert.That(squadSystem.TryGetSquad(squadMembers[0], out var squad));
            var plan = squad!.Value.Comp.CoverPlan!;

            Assert.Multiple(() =>
            {
                Assert.That(plan.HasRoom);
                Assert.That(plan.RoomTiles, Has.Count.EqualTo(RoomTileCount), "the room is exactly its floor");
                Assert.That(plan.Thresholds, Has.Count.EqualTo(2), "one threshold per door");
                Assert.That(assignments.Select(assignment => assignment.ThresholdIndex).Distinct().Count(), Is.EqualTo(2),
                    "each member should cover a different threshold");
            });

            foreach (var assignment in assignments)
            {
                var threshold = plan.Thresholds[assignment.ThresholdIndex];
                var position = assignment.Coordinates.Position;
                var fromThreshold = Vector2.Normalize(position - threshold.Center);
                var offAxisDegrees = MathF.Acos(Vector2.Dot(fromThreshold, threshold.InwardNormal)) * 180f / MathF.PI;

                Assert.Multiple(() =>
                {
                    Assert.That(examineSystem.InRangeUnOccluded(
                            transformSystem.ToMapCoordinates(assignment.Coordinates),
                            transformSystem.ToMapCoordinates(new EntityCoordinates(plan.GridUid, threshold.AimPoint)),
                            20f,
                            null),
                        $"cover position {position} cannot see its threshold");

                    Assert.That(offAxisDegrees, Is.GreaterThanOrEqualTo(15f),
                        $"cover position {position} stands in the fatal funnel of the threshold at {threshold.Center}");

                    Assert.That(Vector2.Distance(position, WindowTile + new Vector2(0.5f, 0.5f)), Is.GreaterThan(1.5f),
                        $"cover position {position} is right next to the window");
                });
            }
        });
    }

    /// <summary>
    ///     With the west airlock replaced by a bare archway, the doors-only flood leaks out into the whole grid;
    ///         the archway must then be found as a threshold of its own.
    /// </summary>
    [Test]
    public async Task TestArchwayIsDetectedAsThreshold()
    {
        var (entManager, squadMembers) = await SetUpRoom(RoomKind.DoorAndArchway);
        var coverSystem = entManager.System<NpcSquadCoverSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(coverSystem.TryGetAssignment(squadMembers[0], out _));
            Assert.That(squadSystem.TryGetSquad(squadMembers[0], out var squad));

            var plan = squad!.Value.Comp.CoverPlan!;

            Assert.Multiple(() =>
            {
                Assert.That(plan.RoomTiles, Has.Count.EqualTo(RoomTileCount));
                Assert.That(plan.Thresholds, Has.Count.EqualTo(2));
                Assert.That(plan.Thresholds.Any(threshold => threshold.Tiles.Contains(WestOpening)),
                    "the archway should be a threshold");
            });
        });
    }

    /// <summary>
    ///     In open space there is no room, and no assignment, so the HTN falls back to tactical positions.
    /// </summary>
    [Test]
    public async Task TestOpenAreaHasNoRoom()
    {
        var (entManager, squadMembers) = await SetUpRoom(RoomKind.None);
        var coverSystem = entManager.System<NpcSquadCoverSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(coverSystem.TryGetAssignment(squadMembers[0], out _), Is.False);
            Assert.That(squadSystem.TryGetSquad(squadMembers[0], out var squad));
            Assert.That(squad!.Value.Comp.CoverPlan!.HasRoom, Is.False);
        });
    }

    /// <summary>
    ///     One door on the west wall and a window directly behind the tile that scores best on distance, angle
    ///         and wall cover alone - so only the window penalty keeps the member off it.
    /// </summary>
    [Test]
    public async Task TestCoverAvoidsWindow()
    {
        var windowTile = new Vector2i(1, -1);
        var assignment = await GetSoloAssignment(windowTile, pillar: false);

        Assert.That(Vector2.Distance(assignment.Coordinates.Position, windowTile + new Vector2(0.5f, 0.5f)), Is.GreaterThan(1.5f),
            $"cover position {assignment.Coordinates.Position} is right next to the window");
    }

    /// <summary>
    ///     One door on the west wall and a pillar that gives the tile straight in front of the door, at the ideal
    ///         standoff, a wall at its back - so only the funnel penalty keeps the member off the door's axis.
    /// </summary>
    [Test]
    public async Task TestCoverAvoidsFatalFunnel()
    {
        var assignment = await GetSoloAssignment(windowTile: null, pillar: true);
        var doorCenter = new Vector2(-0.5f, 2.5f);
        var fromDoor = Vector2.Normalize(assignment.Coordinates.Position - doorCenter);
        var offAxisDegrees = MathF.Acos(Vector2.Dot(fromDoor, new Vector2(1f, 0f))) * 180f / MathF.PI;

        Assert.That(offAxisDegrees, Is.GreaterThanOrEqualTo(15f),
            $"cover position {assignment.Coordinates.Position} stands in the door's fatal funnel");
    }

    /// <summary>
    ///     A lone NPC in the 7x6 room with only the west airlock, an optional window in the wall, and an optional
    ///         pillar at x 3, y 1..3.
    /// </summary>
    private async Task<NpcSquadCoverAssignment> GetSoloAssignment(Vector2i? windowTile, bool pillar)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var coverSystem = entManager.System<NpcSquadCoverSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid memberUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    var tile = new Vector2i(x, y);
                    var isWall = x is -1 or 7 || y is -1 or 6;
                    var isPillar = pillar && x == 3 && y is >= 1 and <= 3;

                    if (!isWall && !isPillar)
                        continue;

                    var prototype = tile == WestOpening ? "Airlock"
                        : tile == windowTile ? "Window"
                        : "WallSolid";

                    SpawnAt(entManager, prototype, gridUid, x, y);
                }
            }

            memberUid = SpawnAt(entManager, SyndicateMob, gridUid, 5, 4);
        });

        await Pair.RunTicksSync(SettleTicks);

        NpcSquadCoverAssignment assignment = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(coverSystem.TryGetAssignment(memberUid, out assignment), "the lone NPC should get the door");
        });

        return assignment;
    }

    private enum RoomKind
    {
        None,
        TwoDoors,
        DoorAndArchway,
    }

    private async Task<(IEntityManager EntManager, List<EntityUid> SquadMembers)> SetUpRoom(RoomKind roomKind)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var squadMembers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            if (roomKind != RoomKind.None)
            {
                for (var x = -1; x <= 7; x++)
                {
                    for (var y = -1; y <= 6; y++)
                    {
                        if (x is > -1 and < 7 && y is > -1 and < 6)
                            continue;

                        var tile = new Vector2i(x, y);
                        var prototype = tile == NorthDoor ? "Airlock"
                            : tile == WindowTile ? "Window"
                            : tile == WestOpening ? (roomKind == RoomKind.TwoDoors ? "Airlock" : null)
                            : "WallSolid";

                        if (prototype != null)
                            SpawnAt(entManager, prototype, gridUid, x, y);
                    }
                }
            }

            squadMembers.Add(SpawnAt(entManager, SyndicateMob, gridUid, 3, 2));
            squadMembers.Add(SpawnAt(entManager, SyndicateMob, gridUid, 4, 3));
        });

        await Pair.RunTicksSync(SettleTicks);

        return (entManager, squadMembers);
    }
}
