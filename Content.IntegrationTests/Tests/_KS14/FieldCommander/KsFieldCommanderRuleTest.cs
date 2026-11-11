#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.GameTicking.Rules;
using Content.Server.GameTicking;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The AI field commander: waves that scale with the defenders, never sent or struck into the defenders' safe
///         zone, joined to the commander's side, and a round that is decided by its uplink rather than by the first
///         objective or the last unit to fall.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderRuleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId ScenarioRule = "KsScenarioFieldCommander";
    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Nanotrasen = "Nanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderRule";
    private readonly EntProtoId _uplinkRule = "KsTestFieldCommanderUplinkRule";
    private readonly EntProtoId _defender = "KsTestFieldCommanderDefender";
    private readonly EntProtoId _spawnPoint = "KsTestFieldCommanderSpawnPoint";
    private readonly EntProtoId _strikeMarker = "KsTestFieldCommanderStrike";
    private readonly EntProtoId _uplink = "KsTestFieldCommanderUplink";

    private const float SafeZoneRadius = 6f;
    private const int HalfExtent = 40;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFieldCommanderUnit
  components:
  - type: MobState

- type: entity
  id: KsTestFieldCommanderSpawnPoint
  components:
  - type: SpawnPoint

- type: entity
  id: KsTestFieldCommanderStrike
  components:
  - type: Transform

- type: entity
  parent: KsFieldCommandUplink
  id: KsTestFieldCommanderUplink
  components:
  - type: TimedDespawn
    lifetime: 3

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 12
    maxSpawnDistance: 22
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 12
    defenderSightRange: 4
    safeZoneRadius: 6
    strikeSafeMargin: 4
    maxAlive: 6
    maxAlivePerDefender: 1
    hardMaxAlive: 10
    strikePrototype: KsTestFieldCommanderStrike
    uplinkPrototype: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      waveInterval: 1
      firstWaveDelay: 0
      budget: 50
      strikeChance: 1
    assets:
    - entity: KsTestFieldCommanderUnit
      count: 2
      cost: 1
    - entity: KsTestFieldCommanderUnit
      cost: 1
      delivery: Pod

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderUplinkRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 12
    maxSpawnDistance: 30
    minDefenderDistance: 8
    defenderSightRange: 4
    safeZoneRadius: 6
    uplinkPrototype: KsTestFieldCommanderUplink
    phases:
    - name: ks-fieldcommander-phase-final
      start: 0
      deployUplink: true
";

    #region Scaling

    [Test]
    public void BudgetGrowsWithDefendersAndPhase()
    {
        var early = new KsFieldCommanderPhase { Budget = 3f, BudgetPerExtraDefender = 0.25f };
        var late = new KsFieldCommanderPhase { Budget = 12f, BudgetPerExtraDefender = 0.25f };

        Assert.That(KsFieldCommanderRuleSystem.GetWaveBudget(early, 0, 1f), Is.Zero, "nobody to send a wave against");
        Assert.That(KsFieldCommanderRuleSystem.GetWaveBudget(early, 1, 1f), Is.EqualTo(3f));
        Assert.That(KsFieldCommanderRuleSystem.GetWaveBudget(early, 9, 1f), Is.EqualTo(3f * 3f),
            "each defender past the first adds a quarter of the budget");
        Assert.That(KsFieldCommanderRuleSystem.GetWaveBudget(late, 4, 1f), Is.GreaterThan(KsFieldCommanderRuleSystem.GetWaveBudget(early, 4, 1f)));
        Assert.That(KsFieldCommanderRuleSystem.GetWaveBudget(early, 4, 1.2f), Is.GreaterThan(KsFieldCommanderRuleSystem.GetWaveBudget(early, 4, 1f)));
    }

    [Test]
    public async Task AliveCapAndAdaptiveMultiplierStayInBounds()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index(_commanderRule).TryComp<KsFieldCommanderRuleComponent>(out var component, server.EntMan.ComponentFactory), Is.True);

            // maxAlive 6, one more per defender, never more than 10.
            Assert.That(KsFieldCommanderRuleSystem.GetMaxAlive(component!, 2), Is.EqualTo(8));
            Assert.That(KsFieldCommanderRuleSystem.GetMaxAlive(component!, 40), Is.EqualTo(10), "the hard cap holds whatever the head count");

            Assert.That(KsFieldCommanderRuleSystem.GetNextAdaptiveMultiplier(component!, component!.MaxAdaptiveMultiplier, 5, 0),
                Is.EqualTo(component.MaxAdaptiveMultiplier));
            Assert.That(KsFieldCommanderRuleSystem.GetNextAdaptiveMultiplier(component, 1f, 5, 0), Is.GreaterThan(1f),
                "a wave that cost the defenders nothing should make the next one harder");
            Assert.That(KsFieldCommanderRuleSystem.GetNextAdaptiveMultiplier(component, 1f, 3, 2), Is.LessThan(1f),
                "heavy losses should ease off");
            Assert.That(KsFieldCommanderRuleSystem.GetNextAdaptiveMultiplier(component, component.MinAdaptiveMultiplier, 0, 10),
                Is.EqualTo(component.MinAdaptiveMultiplier));
        });
    }

    #endregion

    /// <summary>
    ///     A square of floor around the origin with the defenders' objective near the middle. Their spawn point, and so
    ///         the safe zone, sits well inside the ring units are sent in from, so that the zone itself is all that keeps
    ///         them out of it. One defender stands in the zone and another out in the open.
    /// </summary>
    private async Task<(TestMapData Map, EntityUid InsideDefender, EntityUid OutsideDefender, Vector2 SpawnPosition)> SetUpOutpost()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var spawnPosition = new Vector2(-13.5f, 0.5f);
        EntityUid insideDefender = default;
        EntityUid outsideDefender = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            for (var x = -HalfExtent; x <= HalfExtent; x++)
            {
                for (var y = -HalfExtent; y <= HalfExtent; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            server.EntMan.SpawnAtPosition(_spawnPoint, new EntityCoordinates(testMap.Grid, spawnPosition));
            insideDefender = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, -12.5f, 0.5f));
            outsideDefender = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 15.5f));
        });

        return (testMap, insideDefender, outsideDefender, spawnPosition);
    }

    [Test]
    public async Task WavesAreCappedJoinTheCommanderAndStayOutOfTheSafeZone()
    {
        var server = Pair.Server;
        var (testMap, _, _, spawnPosition) = await SetUpOutpost();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(ScenarioRule, out _), Is.True);
            Assert.That(ticker.StartGameRule(_commanderRule, out ruleUid), Is.True);
        });

        var tickRate = (float)server.ResolveDependency<IGameTiming>().TickRate;
        var podded = 0;
        var strikes = 0;

        // Every wave is cleared away before the next, so that each one is placed afresh: a single wave lands in the
        //      safe zone only now and then even with nothing keeping it out.
        for (var wave = 0; wave < 8; wave++)
        {
            await Pair.RunTicksSync((int)(1.1f * tickRate));

            await server.WaitPost(() =>
            {
                var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
                var transformSystem = server.System<SharedTransformSystem>();
                var containerSystem = server.System<SharedContainerSystem>();

                Assert.That(ruleComponent.SafeZones, Has.Count.EqualTo(1), "the spawn point should have made the one safe zone");

                var units = new List<EntityUid>();
                var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
                while (unitQuery.MoveNext(out var unitUid, out _))
                    units.Add(unitUid);

                // 6 base plus one for each of the two defenders, whatever the budget could buy.
                Assert.That(units, Has.Count.EqualTo(KsFieldCommanderRuleSystem.GetMaxAlive(ruleComponent, 2)),
                    "the budget is far more than the cap, so the cap is what should limit the field");

                foreach (var unitUid in units)
                {
                    Assert.That(server.EntMan.GetComponent<ScenarioFactionMemberComponent>(unitUid).Id, Is.EqualTo(Syndicate),
                        "what a unit captures has to count for the commander");

                    if (containerSystem.IsEntityInContainer(unitUid))
                        podded++;

                    var position = transformSystem.GetMapCoordinates(unitUid).Position;
                    Assert.That(Vector2.Distance(position, spawnPosition), Is.GreaterThanOrEqualTo(SafeZoneRadius),
                        $"{server.EntMan.ToPrettyString(unitUid)} was sent into the safe zone");

                    server.EntMan.DeleteEntity(unitUid);
                }

                var strikeQuery = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (strikeQuery.MoveNext(out var strikeUid, out var metaDataComponent, out _))
                {
                    if (metaDataComponent.EntityPrototype?.ID != _strikeMarker.Id)
                        continue;

                    strikes++;
                    var position = transformSystem.GetMapCoordinates(strikeUid).Position;
                    Assert.That(Vector2.Distance(position, spawnPosition), Is.GreaterThanOrEqualTo(SafeZoneRadius + ruleComponent.StrikeSafeMargin),
                        "a strike landed in or beside the safe zone");

                    server.EntMan.QueueDeleteEntity(strikeUid);
                }
            });
        }

        Assert.That(podded, Is.GreaterThan(0), "pod assets should arrive inside their pods");
        Assert.That(strikes, Is.GreaterThan(0), "the defender out in the open should have been struck");
    }

    [Test]
    public async Task OneLostObjectiveOrUnitDoesNotEndTheRound()
    {
        var server = Pair.Server;
        var (testMap, insideDefender, outsideDefender, _) = await SetUpOutpost();
        Entity<ScenarioRuleComponent>? rule = null;
        EntityUid unitUid = default;

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(ScenarioRule, out var scenarioUid), Is.True);
            rule = (scenarioUid, server.EntMan.GetComponent<ScenarioRuleComponent>(scenarioUid));

            // A second objective, so losing one leaves the defenders in the game.
            var objectiveUid = server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, -3.5f, 0.5f));

            unitUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));
            server.EntMan.GetComponent<ScenarioFactionMemberComponent>(unitUid).Id = Syndicate;

            server.System<TriggerSystem>().Trigger(objectiveUid, unitUid, key: "win");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(rule!.Value.Comp.WinType, Is.Not.EqualTo(ScenarioWinType.Objective),
                "one of two objectives was lost; the round should have gone on");

            // The commander's last unit dying is not a defeat either: there are always more.
            server.System<MobStateSystem>().ChangeMobState(unitUid, MobState.Dead);
            Assert.That(rule.Value.Comp.WinningFactionId, Is.EqualTo(Syndicate),
                "the capture should have been credited to the commander, and nothing since should have changed that");
        });

        await server.WaitAssertion(() =>
        {
            // Every defender dead: now the commander has won by decimation.
            var mobStateSystem = server.System<MobStateSystem>();
            mobStateSystem.ChangeMobState(insideDefender, MobState.Dead);
            mobStateSystem.ChangeMobState(outsideDefender, MobState.Dead);

            Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Syndicate));
            Assert.That(rule.Value.Comp.WinType, Is.EqualTo(ScenarioWinType.Decimation));
        });
    }

    [Test]
    public async Task UplinkDecidesTheRound([Values] bool hacked)
    {
        var server = Pair.Server;
        var (_, _, outsideDefender, _) = await SetUpOutpost();
        Entity<ScenarioRuleComponent>? rule = null;
        EntityUid commanderUid = default;

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(ScenarioRule, out var scenarioUid), Is.True);
            rule = (scenarioUid, server.EntMan.GetComponent<ScenarioRuleComponent>(scenarioUid));
            Assert.That(ticker.StartGameRule(_uplinkRule, out commanderUid), Is.True);
        });

        await Pair.RunTicksSync(5);

        EntityUid uplinkUid = default;
        await server.WaitAssertion(() =>
        {
            var commanderComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(commanderUid);
            Assert.That(commanderComponent.UplinkUid, Is.Not.Null, "the final phase should have landed the uplink");
            uplinkUid = commanderComponent.UplinkUid!.Value;
            Assert.That(rule!.Value.Comp.PendingObjectiveFactions, Does.Not.Contain(Syndicate),
                "once the uplink is down, it is the objective the commander holds");
            Assert.That(rule.Value.Comp.WinningFactionId, Is.Null);
        });

        if (hacked)
        {
            await server.WaitPost(() => server.System<TriggerSystem>().Trigger(uplinkUid, outsideDefender, key: "win"));
            await Pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Nanotrasen), "taking the uplink wins the defenders the round");
                Assert.That(rule.Value.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));
            });
            return;
        }

        // Left alone, it lasts until its bombardment.
        var tickRate = (float)server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(4f * tickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Syndicate), "an uplink that lasts its timer wins the commander the round");
            Assert.That(rule.Value.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));
        });
    }
}
