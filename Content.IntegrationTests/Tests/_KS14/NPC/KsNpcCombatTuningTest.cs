#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     What makes operatives more aggressive and harder to pin down: the stand-off a ranged NPC keeps can be capped,
///         and a dive gets an NPC into cover without hurling it into a wall.
/// </summary>
public sealed class KsNpcCombatTuningTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Diver = "KsCombatTuningTestDiver";
    private const string DiveAction = "KsCombatTuningTestDiveAction";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsCombatTuningTestDiver
  components:
  - type: Actions
  - type: JumpAbility
    action: KsCombatTuningTestDiveAction
    canCollide: false
    punishKnockdown: null

- type: entity
  parent: BaseAction
  id: KsCombatTuningTestDiveAction
  components:
  - type: Action
    useDelay: 6
  - type: TargetAction
  - type: WorldTargetAction
    event: !type:KsGravityJumpWorldEvent
";

    /// <summary>
    ///     A juke given a cap on its firing distance hands it to the steering that keeps the NPC at range; one
    ///         without leaves the NPC to keep whatever distance its gun suits.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestJukeCapsFiringDistance(bool capped)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var jukeOperator = new JukeOperator { MaxFiringDistanceKey = capped ? "MaxFiringDistance" : null };

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(jukeOperator, oneOff: true);
            var npcUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue("MaxFiringDistance", 6f);
            jukeOperator.Startup(blackboard);

            Assert.That(entManager.GetComponent<NPCJukeComponent>(npcUid).KsMaxFiringDistance, Is.EqualTo(capped ? 6f : null));
        });
    }

    /// <summary>
    ///     A dive goes towards where the NPC is heading, no further than it can jump, and stops short of anything in
    ///         the way. With a wall too close to get anywhere, or no dive to do, it does not dive at all.
    /// </summary>
    [TestCase(null, 3.5f, Description = "open floor: as far as a dive goes")]
    [TestCase(3, 1.9f, Description = "a wall three tiles off: short of it")]
    [TestCase(1, null, Description = "a wall right there: no dive")]
    public async Task TestDiveStopsShortOfWalls(int? wallTiles, float? expectedDistance)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var diveOperator = new DiveOperator { Id = DiveAction };
        EntityUid diverUid = default;

        await Pair.Server.WaitPost(() =>
        {
            diverUid = SpawnAt(entManager, Diver, gridUid, 0, 0);
            if (wallTiles is { } distance)
                SpawnAt(entManager, "WallSolid", gridUid, distance, 0);
        });

        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(diveOperator, oneOff: true);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, diverUid);
            blackboard.SetValue(diveOperator.DestinationKey, new EntityCoordinates(gridUid, new Vector2(10.5f, 0.5f)));

#pragma warning disable RA0004 // completes synchronously
            var (valid, effects) = diveOperator.Plan(blackboard, default).Result;
#pragma warning restore RA0004

            if (expectedDistance is not { } expected)
            {
                Assert.That(valid, Is.False, "with nowhere to dive to, it should not dive");
                return;
            }

            Assert.That(valid, "it should dive");
            var diveCoordinates = (EntityCoordinates) effects![diveOperator.Key];
            var diverCoordinates = entManager.GetComponent<TransformComponent>(diverUid).Coordinates;
            Assert.That(diveCoordinates.TryDistance(entManager, entManager.System<SharedTransformSystem>(), diverCoordinates, out var dived));
            Assert.That(dived, Is.EqualTo(expected).Within(0.1f));
        });
    }

    /// <summary>
    ///     An NPC that cannot dive - no dive action at all - does not plan one, so a dive in a shared retreat is
    ///         skipped by everyone but the NPCs given the action.
    /// </summary>
    [Test]
    public async Task TestNpcWithoutDiveDoesNotDive()
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var diveOperator = new DiveOperator { Id = DiveAction };

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(diveOperator, oneOff: true);
            var npcUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            blackboard.SetValue(diveOperator.DestinationKey, new EntityCoordinates(gridUid, new Vector2(10.5f, 0.5f)));

#pragma warning disable RA0004 // completes synchronously
            Assert.That(diveOperator.Plan(blackboard, default).Result.Valid, Is.False);
#pragma warning restore RA0004
        });
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpOpenGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(12, 5)).Owner;
        });

        return (entManager, gridUid);
    }
}
