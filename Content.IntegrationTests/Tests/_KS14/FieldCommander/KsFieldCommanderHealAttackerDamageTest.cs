#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.CCVar;
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
///     With <see cref="KsCCVars.FieldCommanderHealAttackerDamageOnly"/>, healing or reviving another defender pays only
///         for the share of their wounds the commander's side dealt: not the world's (barbed wire, a fall), nor a
///         defender's. Off, both pay as they always did.
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
      100: Dead
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
    revivePoints: 40
    reviveCooldown: 600
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

    /// <summary>
    ///     Kills the patient with <paramref name="kills"/>, each an amount and who dealt it, brings them back as the
    ///         medic, and returns what the revive paid.
    /// </summary>
    private int KillAndRevive(EntityUid ruleUid, EntityUid medic, EntityUid patient, params (float Amount, EntityUid? Origin)[] kills)
    {
        var mobStateSystem = Pair.Server.System<MobStateSystem>();

        foreach (var (amount, origin) in kills)
            Damage(patient, amount, origin);

        Assert.That(mobStateSystem.IsDead(patient), Is.True, "precondition: the patient died");
        var afterDeath = GetPoints(ruleUid);
        mobStateSystem.ChangeMobState(patient, MobState.Critical, origin: medic);
        return GetPoints(ruleUid) - afterDeath;
    }

    /// <summary>
    ///     A defender killed half by another defender and half by the attackers, brought back, then healed: the heal is
    ///         paid against what killed them, half of it either way, not against a ledger the death wiped (in full with
    ///         the cvar off, nothing with it on). Killed again by the attackers, the old teamkill is not charged twice.
    /// </summary>
    [Test]
    public async Task HealingAfterAReviveIsPaidAgainstWhatKilledThem([Values] bool attackerDamageOnly)
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, attacker) = await SetUp(attackerDamageOnly);
        var otherSession = await server.AddDummySession();

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();

            var shooterUid = server.EntMan.SpawnAtPosition(_defender, server.EntMan.GetComponent<TransformComponent>(patient).Coordinates);
            server.PlayerMan.SetAttachedEntity(otherSession, shooterUid);

            KillAndRevive(ruleUid, medic, patient, (50f, shooterUid), (50f, attacker));
            var shooterAfterDeath = ruleSystem.GetPoints(component, otherSession.UserId);
            Assert.That(shooterAfterDeath, Is.LessThan(0), "precondition: the first death was charged as a teamkill");

            var before = GetPoints(ruleUid);
            Damage(patient, -20f, originUid: medic);
            var expected = (int)MathF.Floor(20f * component.HealPointsPerDamage * (50f / 100f));
            Assert.That(expected, Is.GreaterThan(0), "precondition: the heal is worth something");
            Assert.That(GetPoints(ruleUid) - before, Is.EqualTo(expected),
                "healing after a revive should be paid against what killed them, as before the death");

            Damage(patient, 40f, originUid: attacker);
            Assert.That(server.System<MobStateSystem>().IsDead(patient), Is.True, "precondition: the patient died again");
            Assert.That(ruleSystem.GetPoints(component, otherSession.UserId), Is.EqualTo(shooterAfterDeath),
                "a teamkill charged at the first death should not be charged again at the next");
        });
    }

    /// <summary>
    ///     A defender killed by the world, brought back by another: paid in full with the cvar off, as it always was,
    ///         and not at all with it on.
    /// </summary>
    [Test]
    public async Task ReviveAfterEnvironmentalDeathPaysOnlyWithTheCvarOff([Values] bool attackerDamageOnly)
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, _) = await SetUp(attackerDamageOnly);

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var paid = KillAndRevive(ruleUid, medic, patient, (100f, null));

            Assert.That(component.RevivePoints, Is.GreaterThan(0), "precondition: a revive is worth something");
            if (attackerDamageOnly)
                Assert.That(paid, Is.EqualTo(0), "reviving someone the world killed should pay nothing when only the attackers' damage pays");
            else
                Assert.That(paid, Is.EqualTo(component.RevivePoints), "with the cvar off, reviving someone the world killed should pay in full, as before");
        });
    }

    /// <summary>
    ///     A defender killed by the commander's side, brought back by another, pays in full with the cvar on.
    /// </summary>
    [Test]
    public async Task ReviveAfterAttackerDeathPaysInFull()
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, attacker) = await SetUp(attackerDamageOnly: true);

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var paid = KillAndRevive(ruleUid, medic, patient, (100f, attacker));

            Assert.That(component.RevivePoints, Is.GreaterThan(0), "precondition: a revive is worth something");
            Assert.That(paid, Is.EqualTo(component.RevivePoints), "reviving someone the attackers killed should pay in full");
        });
    }

    /// <summary>
    ///     A defender killed by the attackers, the world and another defender together, brought back with the cvar on:
    ///         only the attackers' share pays.
    /// </summary>
    [Test]
    public async Task ReviveAfterMixedDeathPaysItsAttackerShare()
    {
        var server = Pair.Server;
        var (ruleUid, medic, patient, attacker) = await SetUp(attackerDamageOnly: true);
        var otherSession = await server.AddDummySession();

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            var shooterUid = server.EntMan.SpawnAtPosition(_defender, server.EntMan.GetComponent<TransformComponent>(patient).Coordinates);
            server.PlayerMan.SetAttachedEntity(otherSession, shooterUid);

            var paid = KillAndRevive(ruleUid, medic, patient, (25f, null), (25f, shooterUid), (50f, attacker));
            var expected = (int)MathF.Round(component.RevivePoints * (50f / 100f));
            Assert.That(expected, Is.GreaterThan(0), "precondition: a revive is worth something");
            Assert.That(paid, Is.EqualTo(expected),
                "reviving should pay only the attackers' share of what killed them, not the world's or a defender's");
        });
    }
}
