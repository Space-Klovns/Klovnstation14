#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using System;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Which wall a squad shut out of a room blows (<see cref="NpcSquadTacticsSettings.BreachScoring"/>): with the scoring, the
///         one whose far side is nearest the noise, even a long walk round, and not one by a hole blown lately; without it, the
///         nearest walk, as before.
/// </summary>
/// <remarks>
///     A sealed room from (-1, -1) to (7, 6), walls all round and nothing inside but floor, the squad outside its west wall
///         and the noise against the inside of its east wall. Tactics are paused and driven with
///         <see cref="NpcSquadTacticsSystem.UpdateNow"/>.
/// </remarks>
[TestFixture]
[TestOf(typeof(NpcSquadTacticsSystem))]
public sealed class KsNpcBreachScoreTest : GameTest
{
    // Dirty: it leaves a squad mid-blast with tactics paused, which the next test on the pair should not inherit.
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private const string ScoringMob = "KsBreachScoreTestMobScoring";
    private const string PlainMob = "KsBreachScoreTestMobPlain";
    private const string TimedCharge = "KsBreachScoreTestCharge";
    private const string SightedMob = "KsBreachScoreTestMobSighted";
    private const string SightedByNoiseMob = "KsBreachScoreTestMobSightedByNoise";
    private const string UnchargedMob = "KsBreachScoreTestMobUncharged";

    private static readonly Vector2i Noise = new(6, 2);
    private static readonly Vector2i BestWall = new(7, 2);

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsBreachScoreTestMobPlain
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcSquadMember
    tactics:
      planning: false
      watchTime: 0s
      huntStartAge: 0.5s # a sighting a second old starts no hunt of its own: the noise does
      huntCharges: true
      maxStageDistance: 40
      planChargeMinPressure: 0 # a test map has no air in it

- type: entity
  parent: KsBreachScoreTestMobPlain
  id: KsBreachScoreTestMobScoring
  components:
  - type: NpcSquadMember
    tactics:
      breachScoring: true
      breachTargetWeight: 1
      breachWalkWeight: 0.05
      breachRecentPenalty: 10
      breachOpeningRange: 3
      breachMemoryTime: 1000s

# Goes by nothing but whether a hostile it knows of would see it set the charge off.
- type: entity
  parent: KsBreachScoreTestMobPlain
  id: KsBreachScoreTestMobSighted
  components:
  - type: NpcSquadMember
    tactics:
      breachScoring: true
      breachTargetWeight: 0
      breachWalkWeight: 0
      breachOpeningPenalty: 0
      breachRecentPenalty: 0
      breachSightPenalty: 50

# Goes by the far side's distance from the noise, and whether a hostile it knows of would see it set the charge off.
- type: entity
  parent: KsBreachScoreTestMobPlain
  id: KsBreachScoreTestMobSightedByNoise
  components:
  - type: NpcSquadMember
    tactics:
      breachScoring: true
      breachTargetWeight: 1
      breachWalkWeight: 0
      breachOpeningPenalty: 0
      breachRecentPenalty: 0
      breachSightPenalty: 50

# Leads, with nothing to do with charges in its settings.
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsBreachScoreTestMobUncharged
  components:
  - type: Hands
  - type: DoAfter
  - type: NpcSquadMember
    tactics:
      planning: false
      watchTime: 0s
      huntStartAge: 0.5s
      maxStageDistance: 40
      planChargeMinPressure: 0

- type: entity
  id: KsBreachScoreTestCharge
  components:
  - type: Item
  - type: Sticky
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 3
    maxIntensity: 10
  - type: TimerTrigger
    delay: 4
";

    [Test]
    public async Task ScoringBlowsTheWallNearestTheNoise()
    {
        var site = await BlastSite(ScoringMob, rememberBreachAtBestWall: false);
        Assert.That(site.Tile, Is.EqualTo(BestWall),
            "a breacher should blow the wall right by the noise, however far round it has to walk");
    }

    [Test]
    public async Task WithoutScoringTheNearestWalkIsBlown()
    {
        var site = await BlastSite(PlainMob, rememberBreachAtBestWall: false);
        Assert.That(site.Tile.X, Is.EqualTo(-1), "without the scoring, the wall right in front of the squad, as before");
    }

    /// <summary>
    ///     Tactics go by the leader's settings, and the one carrying the charge is there to breach while the leader is not: the
    ///         wall is still chosen the carrier's way. Breachers sent with an ordinary elite join its squad.
    /// </summary>
    [Test]
    public async Task ACarrierThatScoresIsFollowedWhoeverLeads()
    {
        var site = await BlastSite(ScoringMob, rememberBreachAtBestWall: false, leaderMob: UnchargedMob);
        Assert.That(site.Tile, Is.EqualTo(BestWall),
            "a breacher in a squad led by one that does not breach should still blow the wall by the noise");
    }

    /// <summary>
    ///     A squad that hears of the noise from much further off than any wall it could walk to with its charge is shut out of the
    ///         room for now, and holds just outside it, where it can blow its way in the next time it hears of it. It used to hold
    ///         where it stood, a long way off, with nothing else it could plan.
    /// </summary>
    [Test]
    public async Task AFarSquadShutOutHoldsOutsideTheRoom()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-130, -14), new Vector2i(14, 14)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }

            // Far enough off that no wall of the room is within a charge's walk, nor any floor outside it within a stager's.
            firstUid = SpawnAt(entManager, PlainMob, gridUid, -120, 3);
            secondUid = SpawnAt(entManager, PlainMob, gridUid, -120, 2);
        });

        await Pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            var squadUid = squadEntity!.Value.Owner;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(secondUid, "hand-charge", HandLocation.Right);
            var chargeUid = entManager.SpawnEntity(TimedCharge, entManager.GetComponent<TransformComponent>(secondUid).Coordinates);
            Assert.That(handsSystem.TryPickup(secondUid, chargeUid, "hand-charge"), "the test needs it holding the charge");

            tacticsSystem.NoteDisturbance(firstUid, new EntityCoordinates(gridUid, new Vector2(Noise.X + 0.5f, Noise.Y + 0.5f)));
            for (var i = 0; i < 5; i++)
            {
                tacticsSystem.UpdateNow();
            }

            var hunt = tacticsSystem.GetHunt(squadUid);
            Assert.That(hunt is { Phase: NpcHuntPhase.Exhausted, ShutOut: true }, $"the test needs it shut out from that far off: {hunt?.Phase}");
            Assert.That(hunt!.ShutOutCoordinates, Is.Not.Null, "it should be given somewhere to hold, not hold where it stands");

            // Just outside the west wall, the side nearest them, not anywhere near where they are.
            var spot = transformSystem.ToMapCoordinates(hunt.ShutOutCoordinates!.Value).Position;
            var room = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(3.5f, 2.5f))).Position;
            Assert.That(Vector2.Distance(spot, room), Is.LessThan(7f), $"it should hold just outside the room: {spot}");
            Assert.That(entManager.System<NpcSquadTacticsSystem>().TryGetOrder(firstUid, out var order) && order.Kind == NpcOrderKind.HoldArea,
                "and be sent there");
            Assert.That(order.Coordinates, Is.EqualTo(hunt.ShutOutCoordinates.Value));
        });
    }

    /// <summary>
    ///     As <see cref="AFarSquadShutOutHoldsOutsideTheRoom"/>, with the floor just outside the west wall, the nearest the squad
    ///         in a straight line, walled into a pocket of its own: nobody could get there, so the squad holds outside another
    ///         wall instead.
    /// </summary>
    [Test]
    public async Task AFarSquadShutOutHoldsOnlyWhereItCanGet()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-130, -14), new Vector2i(14, 14)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }

            // The pocket: the column of floor at x = -2 along the west wall, shut in by walls west of it and at both ends.
            for (var y = -2; y <= 7; y++)
                SpawnAt(entManager, TestWall, gridUid, -3, y);

            SpawnAt(entManager, TestWall, gridUid, -2, -2);
            SpawnAt(entManager, TestWall, gridUid, -2, 7);

            firstUid = SpawnAt(entManager, PlainMob, gridUid, -120, 3);
            secondUid = SpawnAt(entManager, PlainMob, gridUid, -120, 2);
        });

        await Pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            var squadUid = squadEntity!.Value.Owner;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(secondUid, "hand-charge", HandLocation.Right);
            var chargeUid = entManager.SpawnEntity(TimedCharge, entManager.GetComponent<TransformComponent>(secondUid).Coordinates);
            Assert.That(handsSystem.TryPickup(secondUid, chargeUid, "hand-charge"), "the test needs it holding the charge");

            tacticsSystem.NoteDisturbance(firstUid, new EntityCoordinates(gridUid, new Vector2(Noise.X + 0.5f, Noise.Y + 0.5f)));
            for (var i = 0; i < 5; i++)
            {
                tacticsSystem.UpdateNow();
            }

            var hunt = tacticsSystem.GetHunt(squadUid);
            Assert.That(hunt is { Phase: NpcHuntPhase.Exhausted, ShutOut: true }, $"the test needs it shut out from that far off: {hunt?.Phase}");
            Assert.That(hunt!.ShutOutCoordinates, Is.Not.Null, "it should be given somewhere to hold, not hold where it stands");

            var spot = transformSystem.ToMapCoordinates(hunt.ShutOutCoordinates!.Value).Position;
            var pocketX = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(-1.5f, 0.5f))).Position.X;
            var room = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(3.5f, 2.5f))).Position;
            Assert.That(spot.X, Is.GreaterThan(pocketX), $"it should not be sent into the walled-in pocket by the west wall: {spot}");
            Assert.That(Vector2.Distance(spot, room), Is.LessThan(7f), $"it should still hold just outside the room: {spot}");
        });
    }

    /// <summary>
    ///     A tall room whose west wall is the side nearest the noise and the one a known hostile out west watches, all of it,
    ///         and more of it than any fixed few candidates: every one of those is seen, and the squad blows a wall it would
    ///         not be seen setting off.
    /// </summary>
    [Test]
    public async Task ScoringChecksTheSightOfEveryCandidate()
    {
        var site = await BlastSite(SightedByNoiseMob, rememberBreachAtBestWall: false, watcher: true,
            roomMax: new Vector2i(11, 15), noise: new Vector2i(0, 7), squadY: 7);
        Assert.That(site.Tile.X, Is.Not.EqualTo(-1), "every spot along the west wall is in sight of the hostile out west");
    }

    /// <summary>
    ///     A charge a squad sets off is remembered on the grid, for the next squad's scoring to keep away from: the planter
    ///         sticks it on, it goes off, and the hole is on the grid's memory.
    /// </summary>
    [Test]
    public async Task ARealChargeGoingOffIsRemembered()
    {
        var gridUid = default(EntityUid);
        var remembered = false;
        var site = await BlastSite(ScoringMob, rememberBreachAtBestWall: false, afterPick: (entManager, grid, hunt, charge) =>
        {
            gridUid = grid;
            var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
            Assert.That(entManager.HasComponent<NpcBreachMemoryComponent>(grid), Is.False, "precondition: nothing blown here yet");

            // As the planter does, once it is at the wall; then it goes off.
            var chargeUid = charge.Pick.Charge.ChargeUid;
            entManager.System<Content.Shared.Sticky.Systems.StickySystem>().StickToEntity(
                (chargeUid, entManager.GetComponent<Content.Shared.Sticky.Components.StickyComponent>(chargeUid)),
                charge.Pick.Site.TargetUid,
                charge.Pick.PlanterUid);

            for (var i = 0; i < 5 && charge.Phase == NpcSquadChargePhase.Planting; i++)
                tacticsSystem.UpdateNow();

            Assert.That(charge.Phase, Is.EqualTo(NpcSquadChargePhase.Clearing), "precondition: planted, it should be cleared from");
            entManager.DeleteEntity(chargeUid);

            for (var i = 0; i < 5 && charge.Phase == NpcSquadChargePhase.Clearing; i++)
                tacticsSystem.UpdateNow();

            Assert.That(charge.Phase, Is.EqualTo(NpcSquadChargePhase.Blasted), "precondition: gone off");
            if (entManager.TryGetComponent<NpcBreachMemoryComponent>(grid, out var memoryComponent))
            {
                foreach (var (tile, _) in memoryComponent.Breaches)
                    remembered |= tile == charge.Pick.Site.Tile;
            }
        });

        Assert.That(remembered, Is.True, $"the hole blown at {site.Tile} should be remembered on the grid");
        Assert.That(gridUid, Is.Not.EqualTo(default(EntityUid)));
    }

    [Test]
    public async Task ScoringKeepsAwayFromARecentBreach()
    {
        var site = await BlastSite(ScoringMob, rememberBreachAtBestWall: true);
        Assert.That(((Vector2) (site.Tile - BestWall)).Length(), Is.GreaterThan(3f),
            "a hole blown there lately is being watched: the next goes somewhere else");
    }

    /// <summary>
    ///     A hostile the squad knows of, out west, sees the spots the west wall's charge would be set off from: walls of a
    ///         row north and south of the room hide the rest. With nothing else to choose by, the squad blows a wall it
    ///         would not be seen setting off.
    /// </summary>
    [Test]
    public async Task ScoringKeepsTheStandOffOutOfAKnownHostilesSight()
    {
        var site = await BlastSite(SightedMob, rememberBreachAtBestWall: false, watcher: true);
        Assert.That(site.Tile.X, Is.Not.EqualTo(-1), "the west wall's charge would be set off in sight of the hostile out west");
    }

    [Test]
    public async Task WithoutScoringTheWatchedWallIsStillBlown()
    {
        var site = await BlastSite(PlainMob, rememberBreachAtBestWall: false, watcher: true);
        Assert.That(site.Tile.X, Is.EqualTo(-1), "without the scoring, the wall in front of them, seen or not");
    }

    /// <summary>
    ///     Sets the scene up, hears the noise, and runs the tactics until the squad is blowing its way in.
    /// </summary>
    /// <param name="leaderMob">What the first of the two is, and leads, spawned before the second: by default the same.</param>
    /// <param name="roomMax">The room's north-east wall corner; its south-west one is (-1, -1).</param>
    /// <param name="noise">Inside the room, where the squad hears the hostile.</param>
    /// <param name="squadY">The row the squad stands on, west of the room, the second member a row south.</param>
    /// <param name="afterPick">Run on the game thread once the charge is picked, with the grid, the hunt and the charge.</param>
    private async Task<NpcChargeSite> BlastSite(string mob,
        bool rememberBreachAtBestWall,
        bool watcher = false,
        string? leaderMob = null,
        Vector2i? roomMax = null,
        Vector2i? noise = null,
        int squadY = 3,
        Action<IEntityManager, EntityUid, NpcHunt, NpcSquadCharge>? afterPick = null)
    {
        var max = roomMax ?? new Vector2i(7, 6);
        var noiseTile = noise ?? Noise;
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default, watcherUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-14, -14), new Vector2i(Math.Max(14, max.X + 7), Math.Max(14, max.Y + 7))).Owner;

            for (var x = -1; x <= max.X; x++)
            {
                for (var y = -1; y <= max.Y; y++)
                {
                    if (x > -1 && x < max.X && y > -1 && y < max.Y)
                        continue;

                    SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }

            if (watcher)
            {
                for (var x = -14; x <= Math.Max(14, max.X + 7); x++)
                {
                    SpawnAt(entManager, TestWall, gridUid, x, max.Y + 2);
                    SpawnAt(entManager, TestWall, gridUid, x, -3);
                }

                watcherUid = SpawnAt(entManager, NanoTrasenMob, gridUid, -10, squadY);
            }

            firstUid = SpawnAt(entManager, leaderMob ?? mob, gridUid, -3, squadY);
        });

        // The first one founds the squad, and so leads it.
        if (leaderMob != null)
            await Pair.RunTicksSync(60);

        await server.WaitPost(() => secondUid = SpawnAt(entManager, mob, gridUid, -3, squadY - 1));

        await Pair.RunTicksSync(150);

        NpcChargeSite? site = null;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            var squadUid = squadEntity!.Value.Owner;
            if (leaderMob != null)
                Assert.That(squadEntity.Value.Comp.Leader, Is.EqualTo(firstUid), "the test needs the first one leading");

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(secondUid, "hand-charge", HandLocation.Right);
            var chargeUid = entManager.SpawnEntity(TimedCharge, entManager.GetComponent<TransformComponent>(secondUid).Coordinates);
            Assert.That(handsSystem.TryPickup(secondUid, chargeUid, "hand-charge"), "the test needs it holding the charge");

            if (watcher)
            {
                // Seen a while ago, out west, and believed still there.
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                var seenAt = now - TimeSpan.FromSeconds(1);
                var believed = new EntityCoordinates(gridUid, new Vector2(-9.5f, squadY + 0.5f));
                foreach (var memberUid in new[] { firstUid, secondUid })
                {
                    entManager.System<NpcPerceptionSystem>().SetContact(memberUid, watcherUid,
                        new NpcContact(NpcContactState.Lost, seenAt, seenAt, seenAt, believed, default, null, Reacted: true, ReactAt: seenAt));
                }
            }

            if (rememberBreachAtBestWall)
                tacticsSystem.RecordBreach(gridUid, BestWall, server.ResolveDependency<IGameTiming>().CurTime);

            tacticsSystem.NoteDisturbance(firstUid, new EntityCoordinates(gridUid, new Vector2(noiseTile.X + 0.5f, noiseTile.Y + 0.5f)));

            NpcSquadCharge? picked = null;
            for (var i = 0; i < 10 && site == null; i++)
            {
                tacticsSystem.UpdateNow();
                if (tacticsSystem.GetHunt(squadUid) is { Phase: NpcHuntPhase.Blast, Blast.Charge: { } charge })
                {
                    site = charge.Pick.Site;
                    picked = charge;
                }
            }

            Assert.That(site, Is.Not.Null, $"the squad shut out of the room should blow its way in: {tacticsSystem.GetHunt(squadUid)?.Phase}");
            Assert.That(site!.Value.IsDoor, Is.False);

            afterPick?.Invoke(entManager, gridUid, tacticsSystem.GetHunt(squadUid)!, picked!);
        });

        return site!.Value;
    }
}
