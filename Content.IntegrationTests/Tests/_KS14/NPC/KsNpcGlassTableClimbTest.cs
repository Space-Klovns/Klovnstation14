#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A glass table shatters under whoever climbs onto it, so a climber walks round one, unless its
///         <see cref="NPCBlackboard.NavClimbBreakables"/> says it climbs those too. Laid out as a row of walls across the
///         grid with one table in the middle, between an NPC 3 tiles one side and its goal 3 tiles the other, and a gap at
///         the far end of the row or none.
/// </summary>
public sealed class KsNpcGlassTableClimbTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsTileDangerTestWalker
  id: KsGlassTableTestClimber
  components:
  - type: Climbing
  - type: DoAfter
";

    private const string GlassTable = "TableGlass";
    private const string PlainTable = "Table";

    /// <summary>
    ///     Takes more than a person's weight (<c>tableMassLimit: 120</c>): climbed like any table.
    /// </summary>
    private const string ReinforcedGlassTable = "TableReinforcedGlass";

    /// <summary>
    ///     How far each side of the row the NPC and its goal are, in tiles.
    /// </summary>
    private const int Across = 3;

    /// <summary>
    ///     How much further it is to walk round through a gap <paramref name="gapAt"/> tiles along the row than straight
    ///         across, in tiles, as the pathfinder counts distance: diagonals at root two.
    /// </summary>
    private static float GetDetourExtra(int gapAt)
    {
        var leg = MathF.Max(gapAt, Across) + (MathF.Sqrt(2f) - 1f) * MathF.Min(gapAt, Across);
        return 2f * leg - 2f * Across;
    }

    /// <summary>
    ///     A gap far enough along that walking round through it is twice what climbing a table, a whole tile deep, costs:
    ///         a climber climbs any table it may rather than go there.
    /// </summary>
    private static int FarGap
    {
        get
        {
            var gap = Across;
            while (GetDetourExtra(gap) < 2f * PathfindingSystem.ClimbCost)
            {
                gap++;
            }

            return gap;
        }
    }

    /// <summary>
    ///     The plain table is worth climbing rather than the walk round; the glass one is not climbed at all, unless the
    ///         switch is on, and with no way round there is then no way.
    /// </summary>
    [TestCase(PlainTable, true, false, ExpectedResult = "over")]
    [TestCase(GlassTable, true, false, ExpectedResult = "round")]
    [TestCase(GlassTable, true, true, ExpectedResult = "over")]
    [TestCase(GlassTable, false, false, ExpectedResult = "none")]
    [TestCase(GlassTable, false, true, ExpectedResult = "over")]
    [TestCase(ReinforcedGlassTable, true, false, ExpectedResult = "over")]
    public async Task<string> TestClimberPathsOverGlassTables(string table, bool gap, bool climbBreakables)
    {
        var (entManager, gridUid, npcUid, _) = await SetUp(table, gap, climbBreakables, "KsSquadTestMobSyndicate");

        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Pair.Server.WaitPost(() => pathTask = entManager.System<PathfindingSystem>().GetPath(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates,
            new EntityCoordinates(gridUid, new Vector2(0.5f, Across + 0.5f)),
            0.5f,
            default,
            entManager.System<PathfindingSystem>().GetFlags(npcUid)));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        var path = await pathTask;
        if (path.Result != PathResult.Path)
            return "none";

        foreach (var poly in path.Path)
        {
            var center = poly.Box.Center;
            if (center.Y > 0f && center.Y < 1f && center.X > 0f && center.X < 1f)
                return "over";
        }

        return "round";
    }

    /// <summary>
    ///     Steering does what the path says: with the switch on, a climber with no way round climbs onto the glass table,
    ///         which shatters under it; with it off, it leaves the table alone and stays its side.
    /// </summary>
    /// <remarks>
    ///     Not that it then gets anywhere: the table leaves a frame, which nothing climbs, so it gets no further this way.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestSteeringClimbsGlassTableOnlyIfAllowed(bool climbBreakables)
    {
        var (entManager, gridUid, npcUid, tableUid) = await SetUp(GlassTable, gap: false, climbBreakables, "KsGlassTableTestClimber");

        await Pair.Server.WaitPost(() =>
        {
            var steeringComponent = entManager.System<NPCSteeringSystem>().Register(npcUid,
                new EntityCoordinates(gridUid, new Vector2(0.5f, Across + 0.5f)));
            steeringComponent.Flags = entManager.System<PathfindingSystem>().GetFlags(npcUid);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var maxY = float.MinValue;
        var shattered = false;

        // A climb takes a few seconds; this is several times that.
        for (var i = 0; i < 600 && !shattered; i += 4)
        {
            await Pair.RunTicksSync(4);
            await Pair.Server.WaitPost(() =>
            {
                maxY = MathF.Max(maxY, entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.Y);
                shattered = entManager.Deleted(tableUid);
            });
        }

        if (climbBreakables)
        {
            Assert.That(shattered, "it climbs onto the glass table, which shatters under it");
        }
        else
        {
            Assert.That(shattered, Is.False, "it never climbs onto the glass table");
            Assert.That(maxY, Is.LessThan(0f), "it stays its side of the row");
        }
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid NpcUid, EntityUid TableUid)> SetUp(
        string table,
        bool gap,
        bool climbBreakables,
        string mob)
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, npcUid = default, tableUid = default;

        var gapAt = gap ? FarGap : (int?) null;
        // The gap is two tiles, and the row runs to the edge of the grid.
        var half = gap ? FarGap + 1 : Across + 1;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-half, -half), new Vector2i(half, half)).Owner;

            for (var along = -half; along <= half; along++)
            {
                if (along >= gapAt && along <= gapAt + 1)
                    continue;

                if (along == 0)
                    tableUid = SpawnAt(entManager, table, gridUid, along, 0);
                else
                    SpawnAt(entManager, "WallSolid", gridUid, along, 0);
            }

            npcUid = SpawnAt(entManager, mob, gridUid, 0, -Across);
            var blackboard = entManager.GetComponent<HTNComponent>(npcUid).Blackboard;
            blackboard.SetValue(NPCBlackboard.NavClimb, true);
            blackboard.SetValue(NPCBlackboard.NavClimbBreakables, climbBreakables);
        });

        await WaitForNavmeshSettled(Pair, gridUid);
        return (entManager, gridUid, npcUid, tableUid);
    }
}
