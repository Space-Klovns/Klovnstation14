#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A unit that is destroyed rather than killed (the objective borg has a mob state but no thresholds to die by)
///         still pays its killer, and a unit that both dies and is destroyed by the same hit pays only once.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderDestroyedUnitTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _destroyedRule = "KsTestFcDestroyedRule";
    private readonly EntProtoId _bothRule = "KsTestFcDestroyedBothRule";
    private readonly EntProtoId _defender = "KsTestFcDestroyedDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcDestroyedDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcDestroyedBorg
  components:
  - type: MobState
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 10
      behaviors:
      - !type:DoActsBehavior
        acts: [ 'Destruction' ]

- type: entity
  parent: KsTestFcDestroyedBorg
  id: KsTestFcDestroyedMob
  components:
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead

- type: entity
  parent: BaseGameRule
  id: KsTestFcDestroyedRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 12
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: null
    briefingAnnouncement: null
    pointsPerCost: 25
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 2
    assets:
    - entity: KsTestFcDestroyedBorg
      cost: 2

- type: entity
  parent: KsTestFcDestroyedRule
  id: KsTestFcDestroyedBothRule
  components:
  - type: KsFieldCommanderRule
    assets:
    - entity: KsTestFcDestroyedMob
      cost: 2
";

    [Test]
    public async Task DestroyedUnitPaysItsKillerOnce([Values] bool alsoDies)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid playerBody = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 16);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            playerBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, playerBody);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, alsoDies ? _bothRule : _destroyedRule).CommanderUid;
        });

        await Pair.RunTicksSync(3);

        EntityUid unitUid = default;
        var expected = 0;
        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 0);
            ruleSystem.ForceWave((ruleUid, ruleComponent));

            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out unitUid, out _), Is.True, "the wave should have sent a unit");
            expected = KsFieldCommanderRuleSystem.GetKillPoints(ruleComponent, ruleComponent.Assets[0]);
            Assert.That(expected, Is.GreaterThan(0));

            // Far more than it takes, in one hit, so that dying and being destroyed come together.
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(50) } };
            server.System<DamageableSystem>().ChangeDamage(unitUid, damage, ignoreResistances: true, origin: playerBody);
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(server.EntMan.Deleted(unitUid), Is.True, "the unit should have been destroyed");
            Assert.That(server.System<KsFieldCommanderRuleSystem>().GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(expected),
                "destroying a unit should pay its kill, once");
            Assert.That(ruleComponent.UnitsKilled, Is.EqualTo(1), "the kill should be counted once");
        });
    }
}
