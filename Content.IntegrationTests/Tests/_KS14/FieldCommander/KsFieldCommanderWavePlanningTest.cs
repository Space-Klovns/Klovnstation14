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
    private readonly EntProtoId _doctrineRule = "KsTestFcPlanningDoctrineRule";
    private readonly EntProtoId _hoardRule = "KsTestFcPlanningHoardRule";
    private readonly EntProtoId _queueRule = "KsTestFcPlanningQueueRule";
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

- type: entity
  parent: KsTestFcPlanningBaseRule
  id: KsTestFcPlanningDoctrineRule
  components:
  - type: KsFieldCommanderRule
    doctrines:
    - name: ks-fieldcommander-doctrine-swarm
      tagWeights:
        heavy: 0
        light: 2
    assets:
    - entity: KsTestFcPlanningUnit
      tags:
      - light
    - entity: KsTestFcPlanningUnit
      tags:
      - heavy

- type: entity
  parent: KsTestFcPlanningBaseRule
  id: KsTestFcPlanningHoardRule
  components:
  - type: KsFieldCommanderRule
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 10
      budgetPerExtraDefender: 0
    - name: ks-fieldcommander-phase-siege
      start: 2000
    doctrines:
    - name: ks-fieldcommander-doctrine-hoard
      budgetMultiplier: 0.5
    # Not yet available, however much it outweighs the other.
    - name: ks-fieldcommander-doctrine-standard
      weight: 1000
      minPhase: 1
    assets:
    - entity: KsTestFcPlanningUnit

- type: entity
  parent: KsTestFcPlanningBaseRule
  id: KsTestFcPlanningQueueRule
  components:
  - type: KsFieldCommanderRule
    assets:
    - entity: KsTestFcPlanningUnit
    # Never bought by the budget.
    - entity: KsTestFcPlanningUnit
      weight: 0
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

    [Test]
    public async Task DoctrineReweightsTheAssetsByTag()
    {
        var server = Pair.Server;
        var ruleUid = await SetUp(_doctrineRule);

        await server.WaitAssertion(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var rule = GetRule(ruleUid);
            system.SetPhase(rule, 0);
            var component = rule.Comp1;
            Assert.That(system.PickDoctrine(component), Is.EqualTo(0));

            var withDoctrine = new List<int>();
            var without = new List<int>();
            for (var i = 0; i < 50; i++)
            {
                system.BuyAssets(component, 1f, 10, new Dictionary<int, int>(), withDoctrine, component.Doctrines[0]);
                system.BuyAssets(component, 1f, 10, new Dictionary<int, int>(), without);
            }

            Assert.That(withDoctrine, Has.Count.EqualTo(50));
            Assert.That(withDoctrine, Does.Not.Contain(1), "the doctrine weighs the heavy tag at nothing");
            Assert.That(without, Does.Contain(1), "without the doctrine, both assets are bought");
            Assert.That(KsFieldCommanderRuleSystem.GetAssetWeight(component.Assets[0], component.Doctrines[0]),
                Is.EqualTo(component.Assets[0].Weight * 2f), "the light tag is weighed double");
        });
    }

    [Test]
    public async Task HoardDoctrineCarriesWhatItDoesNotSpend()
    {
        var server = Pair.Server;
        var ruleUid = await SetUp(_hoardRule);

        await server.WaitAssertion(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var rule = GetRule(ruleUid);
            system.SetPhase(rule, 0);

            var phase = rule.Comp1.Phases[0];
            var doctrine = rule.Comp1.Doctrines[0];
            var budget = KsFieldCommanderRuleSystem.GetWaveBudget(phase, 1, rule.Comp1.AdaptiveMultiplier);
            var spent = KsFieldCommanderRuleSystem.GetSpendableBudget(budget, doctrine);
            Assert.That(spent, Is.LessThan(budget), "precondition: the doctrine spends less than the budget");

            var sent = SendWave(ruleUid, new HashSet<EntityUid>());

            Assert.That(rule.Comp1.DoctrineIndex, Is.EqualTo(0), "the only doctrine available in the first phase should be picked");
            Assert.That(sent, Has.Count.EqualTo((int)(spent / rule.Comp1.Assets[0].Cost)), "only the doctrine's share should be spent");
            Assert.That(rule.Comp1.CarriedBudget, Is.EqualTo(budget - spent).Within(0.001f),
                "what it held back should be carried to the next wave");
        });
    }

    [Test]
    public async Task QueuedWaveIsSentInPlaceOfTheBudgetsAndOnlyOnce()
    {
        var server = Pair.Server;
        var ruleUid = await SetUp(_queueRule);
        const string announcement = "  { $literal } is not a locale id  ";

        await server.WaitAssertion(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var rule = GetRule(ruleUid);
            system.SetPhase(rule, 0);
            var entity = new Entity<KsFieldCommanderRuleComponent>(ruleUid, rule.Comp1);

            Assert.That(system.TryQueueWave(entity, new List<(int, int)> { (7, 1) }, null), Is.False, "an unknown asset is refused");
            Assert.That(system.TryQueueWave(entity, new List<(int, int)>(), null), Is.False, "an empty wave is refused");
            Assert.That(rule.Comp1.QueuedWaves, Is.Empty);

            Assert.That(system.TryQueueWave(entity, new List<(int, int)> { (1, 3) }, announcement), Is.True);
            Assert.That(rule.Comp1.QueuedWaves[0].Announcement, Is.EqualTo(announcement.Trim()),
                "what the admin typed is kept as it is, only trimmed");

            var carried = rule.Comp1.CarriedBudget;
            var seen = new HashSet<EntityUid>();
            var queued = SendWave(ruleUid, seen);
            Assert.That(queued, Is.EqualTo(new[] { 1, 1, 1 }), "the queued wave should be sent as written, and nothing else");
            Assert.That(rule.Comp1.QueuedWaves, Is.Empty, "a queued wave is sent once");
            Assert.That(rule.Comp1.CarriedBudget, Is.EqualTo(carried), "a queued wave neither spends nor saves");

            var next = SendWave(ruleUid, seen);
            Assert.That(next, Is.Not.Empty);
            Assert.That(next, Does.Not.Contain(1), "the wave after is the budget's again");
        });
    }
}
