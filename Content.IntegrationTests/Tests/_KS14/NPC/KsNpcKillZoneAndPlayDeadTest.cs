#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.PlayDead;
using Content.Server._KS14.NPC.KillZones;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.PlayDead;
using Content.Server.NPC;
using Content.Shared._KS14.NPC;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Kill zones (<see cref="NpcKillZoneSystem"/>): where NPCs' own go down, and for whom and how long it counts. And
///         playing dead (<see cref="NpcPlayDeadSystem"/>): when a rattled NPC left on its own lies down, and when it gets
///         back up.
/// </summary>
public sealed class KsNpcKillZoneAndPlayDeadTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string MarkingMob = "KsKillZoneTestMarkingMob";
    private const string FakerMob = "KsPlayDeadTestFaker";
    private const string SquadFakerMob = "KsPlayDeadTestSquadFaker";
    private const string Caution = "KsPlayDeadTestCaution";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsKillZoneTestMarkingMob
  components:
  - type: NpcKillZoneOnDown
    reach: 2
    duration: 1s

- type: npcMeter
  id: KsPlayDeadTestCaution
  max: 100
  decayPerSecond: 0

- type: entity
  parent: KsSquadTestMobLoner
  id: KsPlayDeadTestFaker
  components:
  - type: StandingState
  - type: NpcSquadMember
    canLead: false
  - type: NpcPlayDead
    meter: KsPlayDeadTestCaution
    threshold: 95
    chance: 1
    maxDuration: 120

- type: entity
  parent: KsPlayDeadTestFaker
  id: KsPlayDeadTestSquadFaker
  components:
  - type: NpcSquadMember
    canLead: true
";

    /// <summary>
    ///     An NPC going down marks a kill zone where it fell: dangerous at the centre, less towards its edge, safe past
    ///         it and behind a wall however close, only to its own side, and only for a while. Going critical and then
    ///         dying in one spot is one zone, not two.
    /// </summary>
    [Test]
    public async Task TestGoingDownMarksKillZone()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var killZoneSystem = entManager.System<NpcKillZoneSystem>();
        var mobStateSystem = entManager.System<MobStateSystem>();
        EntityUid allyUid = default, enemyUid = default;

        // A wall one tile east of where the victim will fall, top to bottom.
        await Pair.Server.WaitPost(() =>
        {
            for (var y = -6; y <= 6; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 1, y);
            }
        });

        await Pair.RunTicksSync(60); // for the navmesh to see it

        await Pair.Server.WaitAssertion(() =>
        {
            var victimUid = SpawnAt(entManager, MarkingMob, gridUid, 0, 0);
            allyUid = SpawnAt(entManager, SyndicateMob, gridUid, 4, 4);
            enemyUid = SpawnAt(entManager, NanoTrasenMob, gridUid, -4, -4);

            mobStateSystem.ChangeMobState(victimUid, MobState.Critical);
            mobStateSystem.ChangeMobState(victimUid, MobState.Dead);

            var centre = new EntityCoordinates(gridUid, new System.Numerics.Vector2(0.5f, 0.5f));
            var nearby = new EntityCoordinates(gridUid, new System.Numerics.Vector2(-1.5f, 0.5f));
            var outside = new EntityCoordinates(gridUid, new System.Numerics.Vector2(-2.5f, 0.5f));
            var behindWall = new EntityCoordinates(gridUid, new System.Numerics.Vector2(2.5f, 0.5f));

            Assert.Multiple(() =>
            {
                Assert.That(killZoneSystem.GetDanger(allyUid, centre), Is.EqualTo(1f).Within(0.01f), "where an ally fell is dangerous");
                Assert.That(killZoneSystem.GetDanger(allyUid, nearby), Is.GreaterThan(0f).And.LessThan(1f), "less so towards the edge");
                Assert.That(killZoneSystem.GetDanger(allyUid, outside), Is.Zero, "past the zone's edge is not");
                Assert.That(killZoneSystem.GetDanger(allyUid, behindWall), Is.Zero, "a zone does not reach through a wall, however close");
                Assert.That(killZoneSystem.GetDanger(enemyUid, centre), Is.Zero, "the other side has nothing to fear there");

                var zones = new List<(Vector2i, IReadOnlyDictionary<Vector2i, float>, float)>();
                killZoneSystem.GetZones(gridUid, zones);
                Assert.That(zones, Has.Count.EqualTo(1), "critical and then dead in one spot is one zone");
            });
        });

        await Pair.RunTicksSync(45); // past the 1s it lasts

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(killZoneSystem.GetDanger(allyUid, new EntityCoordinates(gridUid, new System.Numerics.Vector2(0.5f, 0.5f))), Is.Zero,
                "a kill zone is only avoided for a while");
        });
    }

    /// <summary>
    ///     A rattled NPC on its own plays dead, and gets up the moment a hostile comes into view.
    /// </summary>
    [Test]
    public async Task TestRattledLonerPlaysDeadUntilSomeoneShows()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var playDeadSystem = entManager.System<NpcPlayDeadSystem>();
        var meterSystem = entManager.System<NpcMeterSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var fakerUid = SpawnAt(entManager, FakerMob, gridUid, 0, 0);

            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), Is.False, "a calm NPC does not play dead");

            meterSystem.Set(fakerUid, Caution, 100f);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), "a rattled NPC on its own should play dead");

            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
            var now = IoCManager.Resolve<IGameTiming>().CurTime;
            entManager.System<NpcPerceptionSystem>().SetContact(fakerUid, hostileUid, new NpcContact(NpcContactState.Visible,
                now, now, now, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: false));

            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), Is.False, "it should get up as soon as a hostile is in sight");
        });
    }

    /// <summary>
    ///     The chance is rolled once per spell of high caution. One that played dead and got up for a hostile does not
    ///         lie straight back down once it is gone, however rattled it still is; only after calming down and being
    ///         rattled again does it get another roll.
    /// </summary>
    [Test]
    public async Task TestPlayDeadIsRolledOncePerSpell()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var playDeadSystem = entManager.System<NpcPlayDeadSystem>();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var fakerUid = SpawnAt(entManager, FakerMob, gridUid, 0, 0);
            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);

            meterSystem.Set(fakerUid, Caution, 100f);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid));

            SeeHostile(entManager, perceptionSystem, fakerUid, hostileUid);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), Is.False, "up for the hostile");

            // Gone again, and still just as rattled.
            perceptionSystem.ForgetContact(fakerUid, hostileUid);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), Is.False, "this spell's roll has been had");

            meterSystem.Set(fakerUid, Caution, 0f);
            playDeadSystem.UpdateNow(fakerUid);
            meterSystem.Set(fakerUid, Caution, 100f);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), "calmed down and rattled again, it gets a fresh roll");
        });
    }

    /// <summary>
    ///     Only an NPC with nobody left plays dead: one still in a squad of two does not, however rattled.
    /// </summary>
    [Test]
    public async Task TestNotAloneDoesNotPlayDead()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var playDeadSystem = entManager.System<NpcPlayDeadSystem>();
        var meterSystem = entManager.System<NpcMeterSystem>();
        EntityUid firstUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, SquadFakerMob, gridUid, 0, 0);
            SpawnAt(entManager, SquadFakerMob, gridUid, 1, 0);
        });

        await Pair.RunTicksSync(90); // for the squad to form

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<Content.Server._KS14.NPC.Squad.NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity) &&
                squadEntity.Value.Comp.Members.Count == 2, "the two should be in one squad");

            meterSystem.Set(firstUid, Caution, 100f);
            playDeadSystem.UpdateNow(firstUid);
            Assert.That(playDeadSystem.IsPlayingDead(firstUid), Is.False, "not with a squadmate still standing");
        });
    }

    /// <summary>
    ///     With a hostile in plain view, it does not drop - it would only be up again at once - and the roll waits for
    ///         when nobody is looking.
    /// </summary>
    [Test]
    public async Task TestPlayDeadWaitsUntilNobodyIsInSight()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var playDeadSystem = entManager.System<NpcPlayDeadSystem>();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            var fakerUid = SpawnAt(entManager, FakerMob, gridUid, 0, 0);
            var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);

            SeeHostile(entManager, perceptionSystem, fakerUid, hostileUid);
            meterSystem.Set(fakerUid, Caution, 100f);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), Is.False, "not while a hostile is watching");

            perceptionSystem.ForgetContact(fakerUid, hostileUid);
            playDeadSystem.UpdateNow(fakerUid);
            Assert.That(playDeadSystem.IsPlayingDead(fakerUid), "once nobody is in sight, it gets its roll");
        });
    }

    /// <summary>
    ///     A tactical position search that avoids kill zones picks somewhere outside one, where it would otherwise have
    ///         stood in the middle of it. Each run is on a fresh grid, so the first run's claim cannot decide the second.
    /// </summary>
    [TestCase(1f, true)]
    [TestCase(0f, false)]
    public async Task TestTacticalPositionAvoidsKillZone(float avoidance, bool expectOutside)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var tacticalPositionOperator = new Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            KillZoneAvoidance = avoidance,
            AvoidFireLanes = false,
            // Clearly prefers being near: without avoidance, the spot at the zone's very centre wins.
            DistanceCurve = new Content.Server.NPC.Queries.Curves.QuadraticCurve { Slope = -0.5f, Exponent = 1f, YOffset = 1f },
        };
        EntityUid mobUid = default;

        await Pair.Server.WaitPost(() => mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0));
        await Pair.RunTicksSync(90); // for the navmesh to build

        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;
        var origin = default(EntityCoordinates);

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(tacticalPositionOperator, oneOff: true);

            origin = new EntityCoordinates(gridUid, new System.Numerics.Vector2(0.5f, 0.5f));
            entManager.System<NpcKillZoneSystem>().AddZone(mobUid, origin, reach: 3, System.TimeSpan.FromMinutes(1),
                new HashSet<Robust.Shared.Prototypes.ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype>> { "Syndicate" });

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, mobUid);
            blackboard.SetValue("KsTestReference", origin);
            planTask = tacticalPositionOperator.Plan(blackboard, default);
        });

        for (var i = 0; i < 120 && !planTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(planTask.IsCompletedSuccessfully, "the position search never finished");
        var (valid, effects) = await planTask;
        Assert.That(valid, "a position should be found on open floor");

        await Pair.Server.WaitAssertion(() =>
        {
            var chosen = (EntityCoordinates) effects![tacticalPositionOperator.KeyCoordinates];
            var danger = entManager.System<NpcKillZoneSystem>().GetDanger(mobUid, chosen);

            if (expectOutside)
                Assert.That(danger, Is.Zero, $"a kill-zone-shy search picked {chosen.Position}, inside the zone");
            else
                Assert.That(danger, Is.GreaterThan(0f), $"without avoidance, the nearest spots - in the zone - should win, not {chosen.Position}");
        });
    }

    private static void SeeHostile(IEntityManager entManager, NpcPerceptionSystem perceptionSystem, EntityUid observerUid, EntityUid hostileUid)
    {
        var now = IoCManager.Resolve<IGameTiming>().CurTime;
        perceptionSystem.SetContact(observerUid, hostileUid, new NpcContact(NpcContactState.Visible,
            now, now, now, entManager.GetComponent<TransformComponent>(hostileUid).Coordinates, default, null, Reacted: false));
    }

    /// <summary>
    ///     Playing dead lies the NPC down, and gets it up when it stops - unless it has died for real meanwhile. That
    ///         last is the engine's doing (standing refuses the dead), pinned here because playing dead relies on it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestPlayDeadOperatorLiesDownAndGetsUp(bool diesMeanwhile)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var standingStateSystem = entManager.System<StandingStateSystem>();
        var playDeadOperator = new PlayDeadOperator();

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(playDeadOperator, oneOff: true);

            var fakerUid = SpawnAt(entManager, FakerMob, gridUid, 0, 0);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, fakerUid);

            playDeadOperator.Startup(blackboard);
            Assert.That(standingStateSystem.IsDown(fakerUid), "it should lie down");

            if (diesMeanwhile)
                entManager.System<MobStateSystem>().ChangeMobState(fakerUid, MobState.Dead);

            playDeadOperator.TaskShutdown(blackboard, Content.Server.NPC.HTN.HTNOperatorStatus.Failed);
            Assert.That(standingStateSystem.IsDown(fakerUid), Is.EqualTo(diesMeanwhile),
                diesMeanwhile ? "a corpse is not stood back up" : "it should get up again");
        });
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
        });

        return (entManager, gridUid);
    }
}
