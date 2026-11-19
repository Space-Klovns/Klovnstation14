#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     With <see cref="KsCCVars.FieldCommanderHealAttackerDamageOnly"/>, healing another defender pays only for the share
///         of their wounds the commander's side dealt: not the world's (barbed wire, a fall), nor a defender's.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderHealAttackerDamageTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcHealAttackerRule";
    private readonly EntProtoId _defender = "KsTestFcHealAttackerDefender";
    private readonly EntProtoId _attacker = "KsTestFcHealAttackerUnit";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcHealAttackerDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcHealAttackerUnit
  components:
  - type: ScenarioFactionMember
    id: Syndicate

- type: entity
  parent: BaseGameRule
  id: KsTestFcHealAttackerRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    faction: Syndicate
    defenderFaction: Nanotrasen
    healPointsPerDamage: 0.5
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 1000
";

    private async Task<(EntityUid RuleUid, EntityUid Medic, EntityUid Patient, EntityUid Attacker)> SetUp(bool attackerDamageOnly)
    {
        await OverrideCVar(Side.Server, KsCCVars.FieldCommanderHealAttackerDamageOnly, attackerDamageOnly);

        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid medic = default;
        EntityUid patient = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 4);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            medic = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            patient = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            attacker = server.EntMan.SpawnAtPosition(_attacker, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, medic);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);
        return (ruleUid, medic, patient, attacker);
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
    ///     A wound nobody on either side dealt, healed by another defender: paid in full with the cvar off, as it always
    ///         was, and not at all with it on.
    /// </summary>
    [Test]
    public async Task EnvironmentalDamagePaysOnlyWithTheCvarOff([Values] bool attackerDamageOnly)
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, _) = await SetUp(attackerDamageOnly);

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            Damage(patient, 40f, originUid: null);
            Damage(patient, -20f, originUid: medic);

            var full = (int)MathF.Floor(20f * component.HealPointsPerDamage);
            Assert.That(full, Is.GreaterThan(0), "precondition: the heal is worth something");

            if (attackerDamageOnly)
                Assert.That(GetPoints(ruleUid), Is.EqualTo(0), "healing a wound the world dealt should pay nothing when only the attackers' damage pays");
            else
                Assert.That(GetPoints(ruleUid), Is.EqualTo(full), "with the cvar off, healing a wound the world dealt should pay in full, as before");
        });
    }

    /// <summary>
    ///     A wound the commander's side dealt, healed by another defender, pays in full with the cvar on.
    /// </summary>
    [Test]
    public async Task AttackerDamagePaysInFull()
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, attacker) = await SetUp(attackerDamageOnly: true);

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            Damage(patient, 40f, originUid: attacker);
            Damage(patient, -20f, originUid: medic);

            var expected = (int)MathF.Floor(20f * component.HealPointsPerDamage);
            Assert.That(expected, Is.GreaterThan(0), "precondition: the heal is worth something");
            Assert.That(GetPoints(ruleUid), Is.EqualTo(expected), "healing a wound the attackers dealt should pay in full");
        });
    }

    /// <summary>
    ///     Wounds from the attackers, the world and another defender together, healed with the cvar on: only the
    ///         attackers' share pays.
    /// </summary>
    [Test]
    public async Task MixedDamagePaysItsAttackerShare()
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, attacker) = await SetUp(attackerDamageOnly: true);
        var otherSession = await server.AddDummySession();

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            var shooterUid = server.EntMan.SpawnAtPosition(_defender, server.EntMan.GetComponent<TransformComponent>(patient).Coordinates);
            server.PlayerMan.SetAttachedEntity(otherSession, shooterUid);

            Damage(patient, 20f, originUid: null);
            Damage(patient, 20f, originUid: shooterUid);
            Damage(patient, 40f, originUid: attacker);

            Damage(patient, -40f, originUid: medic);
            var expected = (int)MathF.Floor(40f * component.HealPointsPerDamage * (40f / 80f));
            Assert.That(expected, Is.GreaterThan(0), "precondition: the heal is worth something");
            Assert.That(GetPoints(ruleUid), Is.EqualTo(expected),
                "healing should pay only the attackers' share of the patient's damage, not the world's or a defender's");
        });
    }
}
