#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     What goes into a wave besides the weighted pick: guaranteed assets, the wave's doctrine, and waves queued by
///         hand.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderWavePlanningTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _guaranteedRule = "KsTestFcPlanningGuaranteedRule";
    private readonly EntProtoId _defender = "KsTestFcPlanningDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcPlanningDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcPlanningUnit
  components:
  - type: MobState

- type: entity
  abstract: true
  parent: BaseGameRule
  id: KsTestFcPlanningBaseRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 12
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    maxAlive: 30
    hardMaxAlive: 30
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 1
      budgetPerExtraDefender: 0
    - name: ks-fieldcommander-phase-siege
      start: 2000
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 1
      budgetPerExtraDefender: 0

- type: entity
  parent: KsTestFcPlanningBaseRule
  id: KsTestFcPlanningGuaranteedRule
  components:
  - type: KsFieldCommanderRule
    assets:
    # 0: what the budget buys
    - entity: KsTestFcPlanningUnit
      cost: 1
    # 1: never bought, only guaranteed every second wave, and dearer than any wave's budget
    - entity: KsTestFcPlanningUnit
      cost: 5
      weight: 0
      maxAlive: 1
      guaranteedEveryWaves: 2
    # 2: never bought, only guaranteed as each phase from the second on begins
    - entity: KsTestFcPlanningUnit
      weight: 0
      minPhase: 1
      guaranteedOnPhaseEnter: true
";

    private async Task<EntityUid> SetUp(EntProtoId commanderRule)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 16);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, commanderRule).CommanderUid;
        });

        await Pair.RunTicksSync(3);
        return ruleUid;
    }

    private Entity<KsFieldCommanderRuleComponent, GameRuleComponent> GetRule(EntityUid ruleUid)
    {
        var entMan = Pair.Server.EntMan;
        return (ruleUid, entMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid), entMan.GetComponent<GameRuleComponent>(ruleUid));
    }

    /// <summary>
    ///     Forces a wave and returns the asset index of every unit it sent, leaving units already in the field alone.
    /// </summary>
    private List<int> SendWave(EntityUid ruleUid, HashSet<EntityUid> seen)
    {
        var rule = GetRule(ruleUid);
        Pair.Server.System<KsFieldCommanderRuleSystem>().ForceWave((rule.Owner, rule.Comp1));

        var sent = new List<int>();
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (seen.Add(unitUid))
                sent.Add(unitComponent.AssetIndex);
        }

        return sent;
    }

    [Test]
    public async Task GuaranteedAssetsComeWhenDueAndKeepToTheirCap()
    {
        var server = Pair.Server;
        var ruleUid = await SetUp(_guaranteedRule);

        await server.WaitAssertion(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var seen = new HashSet<EntityUid>();
            system.SetPhase(GetRule(ruleUid), 0);

            var wave1 = SendWave(ruleUid, seen);
            Assert.That(wave1, Does.Not.Contain(1), "the every-second-wave asset is not due on the first");
            Assert.That(wave1, Does.Not.Contain(2), "the phase asset is not available before its phase");

            var wave2 = SendWave(ruleUid, seen);
            Assert.That(wave2.Count(index => index == 1), Is.EqualTo(1),
                "the every-second-wave asset should come on the second, though the budget could never buy it");
            Assert.That(GetRule(ruleUid).Comp1.CarriedBudget, Is.Zero, "its cost should have been charged to the wave");

            system.SetPhase(GetRule(ruleUid), 1);
            var wave3 = SendWave(ruleUid, seen);
            Assert.That(wave3.Count(index => index == 2), Is.EqualTo(1), "the phase asset should come with the phase's first wave");
            Assert.That(wave3, Does.Not.Contain(1));

            // The guaranteed one from the second wave is still alive, and only one may be.
            var wave4 = SendWave(ruleUid, seen);
            Assert.That(wave4, Does.Not.Contain(2), "the phase asset comes only with the phase's first wave");
            Assert.That(wave4, Does.Not.Contain(1), "a guaranteed asset still keeps to its alive cap");
        });
    }
}
