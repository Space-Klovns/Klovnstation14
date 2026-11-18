#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Points for reviving and healing other defenders, less the medic's own share of the harm, and none for healing
///         oneself.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderMedicalPointsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcMedicalRule";
    private readonly EntProtoId _defender = "KsTestFcMedicalDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcMedicalDefender
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
  parent: BaseGameRule
  id: KsTestFcMedicalRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    revivePoints: 40
    reviveCooldown: 600
    healPointsPerDamage: 0.5
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 1000
";

    private async Task<(EntityUid RuleUid, EntityUid Medic, EntityUid Patient)> SetUp()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid medic = default;
        EntityUid patient = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 4);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            medic = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            patient = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, medic);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);
        return (ruleUid, medic, patient);
    }

    private void Damage(EntityUid targetUid, float amount, EntityUid? originUid)
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(amount) } };
        Pair.Server.System<DamageableSystem>().ChangeDamage(targetUid, damage, ignoreResistances: true, origin: originUid);
    }

    private int GetPoints(EntityUid ruleUid)
    {
        var server = Pair.Server;
        return server.System<KsFieldCommanderRuleSystem>()
            .GetPoints(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid), Pair.Player!.UserId);
    }

    /// <summary>
    ///     Healing another defender pays per point healed; healing oneself pays nothing. With <paramref name="fault"/>,
    ///         the medic dealt half of what the patient took, and is paid half.
    /// </summary>
    [Test]
    public async Task HealingPaysLessTheHealersShare([Values] bool fault)
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient) = await SetUp();

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            Damage(patient, 40f, originUid: null);
            if (fault)
                Damage(patient, 40f, originUid: medic);

            Damage(patient, -20f, originUid: medic);
            var share = fault ? 0.5f : 0f;
            var expected = (int)MathF.Floor(20f * component.HealPointsPerDamage * (1f - share));
            Assert.That(expected, Is.GreaterThan(0), "precondition: the heal is worth something");
            Assert.That(GetPoints(ruleUid), Is.EqualTo(expected), "healing another defender should pay, less the healer's share");

            // Their own wounds are their own business.
            Damage(medic, 40f, originUid: null);
            Damage(medic, -20f, originUid: medic);
            Assert.That(GetPoints(ruleUid), Is.EqualTo(expected), "healing oneself should pay nothing");
        });
    }

    /// <summary>
    ///     Reviving a dead defender pays, less the reviver's share of what killed them, and only once per cooldown.
    /// </summary>
    [Test]
    public async Task RevivingPaysLessTheReviversShareOncePerCooldown([Values] bool fault)
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient) = await SetUp();

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var mobStateSystem = server.System<MobStateSystem>();

            Damage(patient, fault ? 50f : 100f, originUid: null);
            if (fault)
                Damage(patient, 50f, originUid: medic);

            Assert.That(mobStateSystem.IsDead(patient), Is.True);
            var afterDeath = GetPoints(ruleUid);

            mobStateSystem.ChangeMobState(patient, MobState.Critical, origin: medic);
            var share = fault ? 0.5f : 0f;
            var expected = (int)MathF.Round(component.RevivePoints * (1f - share));
            Assert.That(GetPoints(ruleUid) - afterDeath, Is.EqualTo(expected),
                "bringing a defender back should pay, less the reviver's share of their death");

            // Straight back down and up again: within the cooldown, so nothing more.
            mobStateSystem.ChangeMobState(patient, MobState.Dead);
            var afterSecondDeath = GetPoints(ruleUid);
            mobStateSystem.ChangeMobState(patient, MobState.Critical, origin: medic);
            Assert.That(GetPoints(ruleUid), Is.EqualTo(afterSecondDeath), "a second revive within the cooldown should pay nothing");
        });
    }
}
