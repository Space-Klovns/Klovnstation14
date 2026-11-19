#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
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
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private const string ScoringMob = "KsBreachScoreTestMobScoring";
    private const string PlainMob = "KsBreachScoreTestMobPlain";
    private const string TimedCharge = "KsBreachScoreTestCharge";

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

    [Test]
    public async Task ScoringKeepsAwayFromARecentBreach()
    {
        var site = await BlastSite(ScoringMob, rememberBreachAtBestWall: true);
        Assert.That(((Vector2) (site.Tile - BestWall)).Length(), Is.GreaterThan(3f),
            "a hole blown there lately is being watched: the next goes somewhere else");
    }

    /// <summary>
    ///     Sets the scene up, hears the noise, and runs the tactics until the squad is blowing its way in.
    /// </summary>
    private async Task<NpcChargeSite> BlastSite(string mob, bool rememberBreachAtBestWall)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tacticsSystem = entManager.System<NpcSquadTacticsSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default, firstUid = default, secondUid = default;

        await server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(entManager, server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-14, -14), new Vector2i(14, 14)).Owner;

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }

            firstUid = SpawnAt(entManager, mob, gridUid, -3, 3);
            secondUid = SpawnAt(entManager, mob, gridUid, -3, 2);
        });

        await Pair.RunTicksSync(150);

        NpcChargeSite? site = null;

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");
            var squadUid = squadEntity!.Value.Owner;

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(secondUid, "hand-charge", HandLocation.Right);
            var chargeUid = entManager.SpawnEntity(TimedCharge, entManager.GetComponent<TransformComponent>(secondUid).Coordinates);
            Assert.That(handsSystem.TryPickup(secondUid, chargeUid, "hand-charge"), "the test needs it holding the charge");

            if (rememberBreachAtBestWall)
                tacticsSystem.RecordBreach(gridUid, BestWall, server.ResolveDependency<IGameTiming>().CurTime);

            tacticsSystem.NoteDisturbance(firstUid, new EntityCoordinates(gridUid, new Vector2(Noise.X + 0.5f, Noise.Y + 0.5f)));

            for (var i = 0; i < 10 && site == null; i++)
            {
                tacticsSystem.UpdateNow();
                if (tacticsSystem.GetHunt(squadUid) is { Phase: NpcHuntPhase.Blast, Blast.Charge: { } charge })
                    site = charge.Pick.Site;
            }

            Assert.That(site, Is.Not.Null, $"the squad shut out of the room should blow its way in: {tacticsSystem.GetHunt(squadUid)?.Phase}");
            Assert.That(site!.Value.IsDoor, Is.False);
        });

        return site!.Value;
    }
}
