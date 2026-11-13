#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.KillCredit;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._KS14.Construction;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Who is paid for a field commander unit's death: damage shares, turret owners, and whoever pressed the signaller.
///         Plus the wave status the HUD reads.
/// </summary>
[TestFixture]
[TestOf(typeof(KsKillCreditSystem))]
public sealed class KsFieldCommanderCreditTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderCreditRule";
    private readonly EntProtoId _defender = "KsTestFieldCommanderCreditDefender";
    private readonly EntProtoId _turret = "KsTestFieldCommanderCreditTurret";
    private readonly EntProtoId _bullet = "KsTestFieldCommanderCreditBullet";
    private readonly EntProtoId _signaller = "KsTestFieldCommanderCreditSignaller";
    private readonly EntProtoId _charge = "KsTestFieldCommanderCreditCharge";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderCreditDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFieldCommanderCreditUnit
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable

- type: entity
  id: KsTestFieldCommanderCreditTurret
  components:
  - type: KsCreditOwner

- type: entity
  id: KsTestFieldCommanderCreditBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 0

- type: entity
  id: KsTestFieldCommanderCreditSignaller
  components:
  - type: Signaller
  - type: DeviceLinkSource
    ports:
    - Pressed

- type: entity
  id: KsTestFieldCommanderCreditCharge
  components:
  - type: Explosive
    explosionType: Default
    totalIntensity: 40
    intensitySlope: 10
    maxIntensity: 20
  - type: ExplodeOnTrigger
  - type: TriggerOnSignal

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderCreditRule
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
    briefingDelay: 1
    pointsPerCost: 25
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 3
    - name: ks-fieldcommander-phase-probing
      start: 300
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 4
    assets:
    - entity: KsTestFieldCommanderCreditUnit
      cost: 4
";

    private async Task<(TestMapData Map, EntityUid RuleUid, EntityUid PlayerBody, EntityUid UnitUid)> SetUpUnit()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid playerBody = default;
        EntityUid unitUid = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 16);

            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            playerBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, playerBody);

            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _commanderRule).CommanderUid;
        });

        // Lets the rule find its objective and prepare the defenders already there.
        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 1);
            ruleSystem.ForceWave((ruleUid, ruleComponent));

            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out unitUid, out _), Is.True, "the wave should have sent a unit");
        });

        return (testMap, ruleUid, playerBody, unitUid);
    }

    private int GetKillPoints(EntityUid ruleUid)
    {
        var ruleComponent = Pair.Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
        return KsFieldCommanderRuleSystem.GetKillPoints(ruleComponent, ruleComponent.Assets[0]);
    }

    private int GetPoints(EntityUid ruleUid, NetUserId userId)
    {
        var ruleComponent = Pair.Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
        return ruleComponent.Points.GetValueOrDefault(userId);
    }

    private void Hurt(EntityUid unitUid, EntityUid? origin, int amount)
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(amount) } };
        Pair.Server.System<DamageableSystem>().ChangeDamage(unitUid, damage, ignoreResistances: true, origin: origin);
    }

    /// <summary>
    ///     Two defenders each hurt a unit and one of them lands the killing blow: the kill is split by damage dealt,
    ///         not paid to whoever shot last.
    /// </summary>
    [Test]
    public async Task KillIsSplitByDamageShare()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var dummySession = await server.AddDummySession();
        EntityUid otherBody = default;
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            otherBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(dummySession, otherBody);
            killPoints = GetKillPoints(ruleUid);
            Assert.That(killPoints, Is.GreaterThan(0));

            // 7 of the 10 it takes from the player, the last 3 (and the kill) from the other defender.
            Hurt(unitUid, playerBody, 7);
            Hurt(unitUid, otherBody, 3);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
        {
            var playerPoints = GetPoints(ruleUid, Pair.Player!.UserId);
            var otherPoints = GetPoints(ruleUid, dummySession.UserId);
            Assert.That(playerPoints, Is.EqualTo((int)Math.Round(killPoints * 0.7f)), "70% of the damage is 70% of the kill");
            Assert.That(otherPoints, Is.EqualTo((int)Math.Round(killPoints * 0.3f)), "30% of the damage is 30% of the kill");
            Assert.That(playerPoints + otherPoints, Is.EqualTo(killPoints), "the split should add up to the kill's value");
        });
    }

    /// <summary>
    ///     A turret's bullets carry the turret as their shooter, and a turret answers to whoever set it up.
    /// </summary>
    [Test]
    public async Task TurretKillsPayItsOwner()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);

            var turretUid = server.EntMan.SpawnAtPosition(_turret, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));

            // Unpacked from a flatpack, which is how a turret comes to be owned.
            var flatpackUid = server.EntMan.SpawnEntity(null, new EntityCoordinates(testMap.Grid, 4.5f, 1.5f));
            server.EntMan.AddComponent<FlatpackComponent>(flatpackUid);
            var unpackedEvent = new KsFlatpackUnpackedEvent(turretUid, playerBody);
            server.EntMan.EventBus.RaiseLocalEvent(flatpackUid, ref unpackedEvent);
            Assert.That(server.EntMan.GetComponent<KsCreditOwnerComponent>(turretUid).OwnerUid, Is.EqualTo(playerBody),
                "unpacking should name the unpacker as the turret's owner");

            var bulletUid = server.EntMan.SpawnAtPosition(_bullet, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            server.System<SharedProjectileSystem>().SetShooter(bulletUid, server.EntMan.GetComponent<ProjectileComponent>(bulletUid), turretUid);

            Hurt(unitUid, bulletUid, 10);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPoints(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret's owner should be paid for its kill"));
    }

    /// <summary>
    ///     A signal names the signaller that sent it; whatever it sets off is credited to the person who pressed it.
    /// </summary>
    [Test]
    public async Task SignallerExplosionPaysThePresser()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);

            var unitCoordinates = server.EntMan.GetComponent<TransformComponent>(unitUid).Coordinates;
            var chargeUid = server.EntMan.SpawnAtPosition(_charge, unitCoordinates);
            var signallerUid = server.EntMan.SpawnAtPosition(_signaller, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));

            server.System<DeviceLinkSystem>().SaveLinks(null, signallerUid, chargeUid, [("Pressed", "Trigger")]);

            var useEvent = new UseInHandEvent(playerBody);
            server.EntMan.EventBus.RaiseLocalEvent(signallerUid, useEvent);
            Assert.That(useEvent.Handled, Is.True);
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead),
                "the charge should have killed the unit");
            Assert.That(GetPoints(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the presser should be paid for the explosion");
            Assert.That(server.System<DeviceLinkSystem>().KsSignalUser, Is.Null, "the presser is only remembered while the signal is delivered");
        });
    }

    /// <summary>
    ///     Sending a wave counts it and schedules the next on the map, and the client is told.
    /// </summary>
    [Test]
    public async Task WaveStatusIsOnTheMapAndReachesTheClient()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var (testMap, ruleUid, _, _) = await SetUpUnit();
        var map = testMap.MapUid;

        await Pair.RunTicksSync(3);

        TimeSpan? nextWaveTime = null;
        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var statusComponent = server.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(map);
            Assert.That(ruleComponent.WavesSent, Is.GreaterThan(0));
            Assert.That(statusComponent.Wave, Is.EqualTo(ruleComponent.WavesSent));
            Assert.That(statusComponent.NextWaveTime, Is.EqualTo(ruleComponent.NextWaveTime));
            nextWaveTime = statusComponent.NextWaveTime;
        });

        // Another wave moves both.
        await server.WaitPost(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            server.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, ruleComponent));
        });

        await Pair.RunTicksSync(5);

        var clientMap = Pair.ToClientUid(map);
        await client.WaitAssertion(() =>
        {
            var statusComponent = client.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(clientMap);
            Assert.That(statusComponent.Wave, Is.EqualTo(2), "the client should know a second wave was sent");
            Assert.That(statusComponent.NextWaveTime, Is.GreaterThan(nextWaveTime), "and when the one after is due");
        });
    }
}
