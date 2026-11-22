#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server.GameTicking;
using Content.Shared._KS14.RCD;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A commander set to block RCDs during waves blocks them on the battlefield's map for as long as one of its units
///         lives, the client is told so it can predict the refusal, and an admin can switch it in a running round.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderRcdBlockTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _blockingRule = "KsTestFcRcdBlockRule";
    private readonly EntProtoId _plainRule = "KsTestFcRcdPlainRule";
    private readonly EntProtoId _defender = "KsTestFcRcdBlockDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcRcdBlockDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcRcdBlockUnit
  components:
  - type: MobState

- type: entity
  abstract: true
  parent: BaseGameRule
  id: KsTestFcRcdBaseRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 14
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    briefingAnnouncement: null
    endlessVote: false
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 1
    assets:
    - entity: KsTestFcRcdBlockUnit

- type: entity
  parent: KsTestFcRcdBaseRule
  id: KsTestFcRcdPlainRule

- type: entity
  parent: KsTestFcRcdBaseRule
  id: KsTestFcRcdBlockRule
  components:
  - type: KsFieldCommanderRule
    blockRcdDuringWaves: true
";

    private async Task<(EntityUid RuleUid, TestMapData Map)> Start(EntProtoId rule)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 20);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var body = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, body);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);
        return (ruleUid, testMap);
    }

    private async Task ForceWave(EntityUid ruleUid)
    {
        var server = Pair.Server;
        await server.WaitPost(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            server.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, component));

            var unitQuery = server.EntMan.EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out _, out _), Is.True, "the wave should have sent a unit");
        });
    }

    private async Task AssertBlocked(TestMapData testMap, bool blocked, string because)
    {
        await Pair.RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Pair.Server.System<KsNoBuildZoneSystem>().IsBlocked(testMap.MapId, out _), Is.EqualTo(blocked),
                $"on the server: {because}");
        });

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(Pair.Client.System<KsNoBuildZoneSystem>().IsBlocked(testMap.MapId, out _), Is.EqualTo(blocked),
                $"on the client, which predicts with it: {because}");
        });
    }

    [Test]
    public async Task RcdsAreBlockedWhileAUnitOfTheWaveLives()
    {
        var server = Pair.Server;
        var (ruleUid, testMap) = await Start(_blockingRule);

        await AssertBlocked(testMap, blocked: false, "no wave has come yet");

        await ForceWave(ruleUid);
        await AssertBlocked(testMap, blocked: true, "a wave is on the field");

        await server.WaitPost(() =>
        {
            var mobStateSystem = server.System<MobStateSystem>();
            var unitQuery = server.EntMan.EntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            while (unitQuery.MoveNext(out var unitUid, out _))
                mobStateSystem.ChangeMobState(unitUid, MobState.Dead);
        });

        await AssertBlocked(testMap, blocked: false, "the last unit of the wave is dead");
    }

    [Test]
    public async Task AnAdminSwitchesTheBlockInARunningRound()
    {
        var server = Pair.Server;
        var consoleHost = server.ResolveDependency<IConsoleHost>();
        var (ruleUid, testMap) = await Start(_plainRule);

        await ForceWave(ruleUid);
        await AssertBlocked(testMap, blocked: false, "this commander was not set to block RCDs");

        await server.WaitPost(() => consoleHost.ExecuteCommand("ks_fcrcdblock true"));
        await AssertBlocked(testMap, blocked: true, "an admin switched the block on with a wave on the field");

        await server.WaitPost(() => consoleHost.ExecuteCommand("ks_fcrcdblock false"));
        await AssertBlocked(testMap, blocked: false, "an admin switched the block off again");
    }

    [Test]
    public async Task EndingTheRuleLiftsTheBlock()
    {
        var server = Pair.Server;
        var (ruleUid, testMap) = await Start(_blockingRule);

        await ForceWave(ruleUid);
        await AssertBlocked(testMap, blocked: true, "a wave is on the field");

        await server.WaitPost(() => server.System<GameTicker>().EndGameRule(ruleUid));
        await AssertBlocked(testMap, blocked: false, "the commander is gone, and takes its block with it");
    }
}
