#nullable enable
using System;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Client._KS14.FieldCommander;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.KillCredit;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._KS14.Construction;
using Content.Shared._KS14.DistressBeacon;
using Content.Shared.Inventory;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
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
    private static readonly EntProtoId Flatpack = "KsFlatpackBallisticTurretNanotrasenRequisition";
    private static readonly EntProtoId UnpackTool = "Multitool";

    /// <summary>
    ///     The most ticks to wait for an explosion to be dealt and to kill. Generous: it is only ever waited out by a
    ///         test that is failing.
    /// </summary>
    private const int ExplosionTickCap = 600;

    // Ids that exist only in the test prototypes below. Instance fields: the YAML linter checks only static ones.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderCreditRule";
    private readonly EntProtoId _defender = "KsTestFieldCommanderCreditDefender";
    private readonly EntProtoId _humanDefender = "KsTestFieldCommanderCreditHumanDefender";
    private readonly EntProtoId _turret = "KsTestFieldCommanderCreditTurret";
    private readonly EntProtoId _beamGun = "KsTestFieldCommanderCreditBeamGun";
    private readonly EntProtoId _bullet = "KsTestFieldCommanderCreditBullet";
    private readonly EntProtoId _signaller = "KsTestFieldCommanderCreditSignaller";
    private readonly EntProtoId _charge = "KsTestFieldCommanderCreditCharge";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderCreditDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  parent: MobHuman
  id: KsTestFieldCommanderCreditHumanDefender
  components:
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  parent: BaseItem
  id: KsTestFieldCommanderCreditBeamGun
  components:
  - type: Gun

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
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      body:
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        density: 50
        hard: true
        layer:
        - MobLayer

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

    /// <param name="playerPrototype">What the player is in as the rule starts, or the bare test defender.</param>
    private async Task<(TestMapData Map, EntityUid RuleUid, EntityUid PlayerBody, EntityUid UnitUid)> SetUpUnit(EntProtoId? playerPrototype = null)
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
            playerBody = server.EntMan.SpawnAtPosition(playerPrototype ?? _defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
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

    /// <summary>
    ///     How far a player's points are from what a defender starts with, whatever that is.
    /// </summary>
    private int GetPointsChange(EntityUid ruleUid, NetUserId userId)
    {
        var ruleComponent = Pair.Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
        return Pair.Server.System<KsFieldCommanderRuleSystem>().GetPoints(ruleComponent, userId) - ruleComponent.StartingPoints;
    }

    /// <summary>
    ///     Runs the server until <paramref name="uid"/> is dead, up to <see cref="ExplosionTickCap"/> ticks.
    /// </summary>
    private async Task RunUntilDead(EntityUid uid)
    {
        for (var i = 0; i < ExplosionTickCap; i++)
        {
            if (Pair.Server.EntMan.GetComponent<MobStateComponent>(uid).CurrentState == MobState.Dead)
                return;

            await Pair.RunTicksSync(1);
        }
    }

    /// <summary>
    ///     A turret that answers to whoever is in <paramref name="ownerBody"/>, as one unpacked by them does.
    /// </summary>
    private EntityUid SpawnOwnedTurret(TestMapData testMap, EntityUid ownerBody)
    {
        var server = Pair.Server;
        var turretUid = server.EntMan.SpawnAtPosition(_turret, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));

        // Unpacked from a flatpack, which is how a turret comes to be owned.
        var flatpackUid = server.EntMan.SpawnEntity(null, new EntityCoordinates(testMap.Grid, 4.5f, 1.5f));
        server.EntMan.AddComponent<FlatpackComponent>(flatpackUid);
        var unpackedEvent = new KsFlatpackUnpackedEvent(turretUid, ownerBody);
        server.EntMan.EventBus.RaiseLocalEvent(flatpackUid, ref unpackedEvent);
        return turretUid;
    }

    /// <summary>
    ///     A bullet whose shooter is <paramref name="shooterUid"/>, as its gun would have made it.
    /// </summary>
    private EntityUid SpawnBullet(TestMapData testMap, EntityUid shooterUid)
    {
        var server = Pair.Server;
        var bulletUid = server.EntMan.SpawnAtPosition(_bullet, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
        server.System<SharedProjectileSystem>().SetShooter(bulletUid, server.EntMan.GetComponent<ProjectileComponent>(bulletUid), shooterUid);
        return bulletUid;
    }

    private void Hurt(EntityUid unitUid, EntityUid? origin, int amount)
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(amount) } };
        Pair.Server.System<DamageableSystem>().ChangeDamage(unitUid, damage, ignoreResistances: true, origin: origin);
    }

    /// <summary>
    ///     Two defenders each hurt a unit: the kill is split by damage dealt, and the one who dealt the most has the
    ///         kill where the other assisted, whoever landed the last shot.
    /// </summary>
    [TestCase(7, true)]
    [TestCase(7, false)]
    [TestCase(3, true)]
    [TestCase(3, false)]
    public async Task KillIsSplitByDamageShare(int localDamage, bool localHitsLast)
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var dummySession = await server.AddDummySession();
        var otherDamage = 10 - localDamage;
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            var otherBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(dummySession, otherBody);
            killPoints = GetKillPoints(ruleUid);
            Assert.That(killPoints, Is.GreaterThan(0));

            // It takes 10 to kill.
            Hurt(unitUid, localHitsLast ? otherBody : playerBody, localHitsLast ? otherDamage : localDamage);
            Hurt(unitUid, localHitsLast ? playerBody : otherBody, localHitsLast ? localDamage : otherDamage);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
        {
            var playerPoints = GetPointsChange(ruleUid, Pair.Player!.UserId);
            var otherPoints = GetPointsChange(ruleUid, dummySession.UserId);
            Assert.That(playerPoints, Is.EqualTo((int)Math.Round(killPoints * localDamage / 10f)), "a share of the damage is the same share of the kill");
            Assert.That(otherPoints, Is.EqualTo((int)Math.Round(killPoints * otherDamage / 10f)));
            Assert.That(playerPoints + otherPoints, Is.EqualTo(killPoints), "the split should add up to the kill's value");
        });

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
            Assert.That(Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason.Id,
                Is.EqualTo(localDamage > otherDamage ? "ks-fieldcommander-points-kill" : "ks-fieldcommander-points-assist"),
                "the top contributor has the kill, whoever shot last"));
    }

    /// <summary>
    ///     What was killed reaches the client as something it can name in its own language: the unit's prototype, not
    ///         its name as the server reads it. A player's character, whose name is their own, is sent as it is.
    /// </summary>
    [Test]
    public async Task KillReasonNamesThePrototypeForTheClientToTranslate()
    {
        var server = Pair.Server;
        var (testMap, _, playerBody, unitUid) = await SetUpUnit();
        const string CharacterName = "Urist McTest";
        var unitPrototypeId = string.Empty;

        await server.WaitPost(() =>
        {
            unitPrototypeId = server.EntMan.GetComponent<MetaDataComponent>(unitUid).EntityPrototype!.ID;
            Hurt(unitUid, playerBody, 10);
        });

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
        {
            var reason = Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason;
            Assert.That(reason.Arguments!["name"], Is.EqualTo(new KsPopupPrototypeName(unitPrototypeId)),
                "a unit should be named by its prototype, for the client to resolve, not by text the server formatted");
        });

        await server.WaitPost(() =>
        {
            var victimUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            server.System<MetaDataSystem>().SetEntityName(victimUid, CharacterName);
            Hurt(victimUid, playerBody, 10);
        });

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
        {
            var reason = Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason;
            Assert.That(reason.Arguments!["name"], Is.EqualTo(CharacterName), "a character's own name is sent as it is");
            Assert.That(reason.Format(Pair.Client.EntMan), Does.Contain(CharacterName));
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

            var turretUid = SpawnOwnedTurret(testMap, playerBody);
            Assert.That(server.EntMan.GetComponent<KsCreditOwnerComponent>(turretUid).OwnerUserId, Is.EqualTo(Pair.Player!.UserId),
                "unpacking should name the unpacker as the turret's owner");

            Hurt(unitUid, SpawnBullet(testMap, turretUid), 10);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret's owner should be paid for its kill"));
    }

    /// <summary>
    ///     A unit a turret puts down rarely dies to a bullet: it dies to what it takes while down, bleeding or
    ///         suffocating, which nobody dealt. That must not turn the turret's kill into its owner's assist.
    /// </summary>
    [Test]
    public async Task TurretKillFinishedByNobodyIsTheOwnersKill()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);

            var turretUid = SpawnOwnedTurret(testMap, playerBody);
            var bulletUid = SpawnBullet(testMap, turretUid);

            // It takes 10 to kill: the turret does most of it, and nobody finishes it off.
            Hurt(unitUid, bulletUid, 8);
            Hurt(unitUid, origin: null, 2);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret's owner should have the whole kill"));

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
            Assert.That(Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason.Id, Is.EqualTo("ks-fieldcommander-points-kill"),
                "a turret's kill finished by nobody should still be its owner's kill, not an assist"));
    }

    /// <summary>
    ///     Unpacks a real requisition flatpack with a multitool, as a player does, and returns the turret it made.
    /// </summary>
    private EntityUid UnpackTurret(TestMapData testMap, EntityUid playerBody)
    {
        var server = Pair.Server;
        var coordinates = new EntityCoordinates(testMap.Grid, 6.5f, 0.5f);
        var flatpackUid = server.EntMan.SpawnAtPosition(Flatpack, coordinates);
        var toolUid = server.EntMan.SpawnAtPosition(UnpackTool, coordinates);
        Assert.That(server.System<SharedInteractionSystem>().InteractUsing(playerBody, toolUid, flatpackUid, coordinates, checkCanUse: false),
            Is.True, "the tool should have unpacked the flatpack");

        var turretQuery = server.EntMan.AllEntityQueryEnumerator<GunComponent, KsCreditOwnerComponent>();
        Assert.That(turretQuery.MoveNext(out var turretUid, out _, out var creditOwnerComponent), Is.True, "unpacking should have made a turret that answers to someone");
        Assert.That(creditOwnerComponent!.OwnerUserId, Is.EqualTo(Pair.Player!.UserId), "the player who unpacked it, not the body they did it in");
        return turretUid;
    }

    /// <summary>
    ///     The turret a player actually has: a requisition flatpack, unpacked with a multitool, shooting a unit with its
    ///         own gun. Its bullets carry the turret, and the turret answers to whoever unpacked it.
    /// </summary>
    [Test]
    public async Task UnpackedTurretKillPaysItsOwnerAsAKill()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;
        EntityUid turretUid = default;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            turretUid = UnpackTurret(testMap, playerBody);

            // Straight in front of the turret.
            server.System<SharedTransformSystem>().SetCoordinates(unitUid, new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));
        });

        // Fire for real until the unit drops, up to a generous cap.
        for (var i = 0; i < 40 && server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState != MobState.Dead; i++)
        {
            await server.WaitPost(() =>
            {
                var gunComponent = server.EntMan.GetComponent<GunComponent>(turretUid);
                server.System<SharedGunSystem>().AttemptShoot(turretUid, (turretUid, gunComponent), new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));
            });
            await Pair.RunTicksSync(10);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead), "the turret should have killed the unit");
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret's owner should be paid for the whole kill");
        });

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
            Assert.That(Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason.Id, Is.EqualTo("ks-fieldcommander-points-kill"),
                "a turret's kill is its owner's kill, not an assist"));
    }

    /// <summary>
    ///     A disposable turret blows itself up on its last shot, and the explosion is dealt over the following ticks:
    ///         by then the turret is gone, but a unit it kills is still its owner's kill.
    /// </summary>
    [Test]
    public async Task UnpackedTurretExplosionPaysItsOwner()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;
        EntityUid turretUid = default;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            turretUid = UnpackTurret(testMap, playerBody);
            server.System<SharedTransformSystem>().SetCoordinates(unitUid, new EntityCoordinates(testMap.Grid, 7.5f, 0.5f));

            // What the gun raises when it is fired empty, which the disposable turret's explosion hangs on.
            var emptyGunShotEvent = new OnEmptyGunShotEvent(turretUid);
            server.EntMan.EventBus.RaiseLocalEvent(turretUid, ref emptyGunShotEvent);
        });

        await RunUntilDead(unitUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(turretUid), Is.True, "the turret should have blown itself up");
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead), "the explosion should have killed the unit");
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret's owner should be paid for what it blew up");
        });
    }

    /// <summary>
    ///     A bullet still in the air when its turret is gone is still the turret owner's.
    /// </summary>
    [Test]
    public async Task BulletOutlivingItsTurretPaysTheOwner()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            var turretUid = UnpackTurret(testMap, playerBody);

            var gunComponent = server.EntMan.GetComponent<GunComponent>(turretUid);
            server.System<SharedGunSystem>().AttemptShoot(turretUid, (turretUid, gunComponent), new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));

            var bulletUid = default(EntityUid);
            var projectileQuery = server.EntMan.AllEntityQueryEnumerator<ProjectileComponent>();
            while (projectileQuery.MoveNext(out var projectileUid, out var projectileComponent))
            {
                if (projectileComponent.Weapon == turretUid)
                    bulletUid = projectileUid;
            }

            Assert.That(bulletUid, Is.Not.EqualTo(default(EntityUid)), "the turret should have fired a bullet");
            server.EntMan.DeleteEntity(turretUid);

            Hurt(unitUid, bulletUid, 10);
            server.EntMan.DeleteEntity(bulletUid); // as a hit does, before the stale shooter is sent to anyone
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the bullet should still be the owner's"));
    }

    /// <summary>
    ///     A defender killed by two others and the enemy: the penalty is scaled down by the enemy's share of the damage,
    ///         then split between the two by what each dealt, and the one who dealt the most is the teamkiller where
    ///         the other only assisted.
    /// </summary>
    [TestCase(5, 2)]
    [TestCase(2, 5)]
    public async Task TeamkillPenaltyIsSplitByDamageShare(int localDamage, int otherDamage)
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, _) = await SetUpUnit();
        var dummySession = await server.AddDummySession();
        EntityUid victimUid = default;
        var fullPenalty = 0;
        var penalty = 0;
        const int LethalDamage = 10; // the test defender's death threshold
        var enemyDamage = LethalDamage - localDamage - otherDamage;

        await server.WaitPost(() =>
        {
            var otherBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(dummySession, otherBody);
            victimUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));

            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            fullPenalty = ruleComponent.TeamkillPoints;
            penalty = KsFieldCommanderRuleSystem.GetTeamkillPenalty(ruleComponent, (float)(localDamage + otherDamage) / LethalDamage);
            Assert.That(fullPenalty, Is.LessThan(0));
            Assert.That(enemyDamage, Is.GreaterThan(0), "the test needs the enemy to have dealt some of it");

            Hurt(victimUid, playerBody, localDamage);
            Hurt(victimUid, otherBody, otherDamage);
            Hurt(victimUid, null, enemyDamage); // not the defenders' doing: it shrinks the penalty they share
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(victimUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
        {
            var total = localDamage + otherDamage;
            var localShare = GetPointsChange(ruleUid, Pair.Player!.UserId);
            var otherShare = GetPointsChange(ruleUid, dummySession.UserId);
            Assert.That(penalty, Is.LessThan(0).And.GreaterThan(fullPenalty), "the enemy's share of the damage should have shrunk the penalty, not waived it");
            Assert.That(localShare + otherShare, Is.EqualTo(penalty), "the split should add up to the scaled penalty");

            // Whoever dealt the most takes up the rounding, so each share is within a point of its exact value.
            Assert.That(localShare, Is.EqualTo(penalty * (float)localDamage / total).Within(1f), "the local player's share of the penalty");
            Assert.That(otherShare, Is.EqualTo(penalty * (float)otherDamage / total).Within(1f), "the other defender's share of the penalty");
        });

        await Pair.RunTicksSync(3);
        await Pair.Client.WaitAssertion(() =>
            Assert.That(Pair.Client.System<KsFieldCommanderPointsSystem>().LastChange!.Reason.Id,
                Is.EqualTo(localDamage > otherDamage ? "ks-fieldcommander-points-teamkill" : "ks-fieldcommander-points-teamkill-assist"),
                "the top contributor is the teamkiller, the other assisted"));
    }

    /// <summary>
    ///     A defender is marked for the client, with the look of their going down from the rule, and the commander's units
    ///         are not.
    /// </summary>
    [Test]
    public async Task DefendersAreMarkedForTheClient()
    {
        var server = Pair.Server;
        var (_, ruleUid, playerBody, unitUid) = await SetUpUnit();

        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var defenderComponent = server.EntMan.GetComponent<KsFieldCommanderDefenderComponent>(playerBody);
            Assert.That(defenderComponent.Flashes[MobState.Critical].Color, Is.EqualTo(ruleComponent.DownFlashes[MobState.Critical].Color));
            Assert.That(defenderComponent.Flashes[MobState.Dead].Color, Is.EqualTo(ruleComponent.DownFlashes[MobState.Dead].Color));
            Assert.That(defenderComponent.Flashes[MobState.Dead].Color, Is.Not.EqualTo(defenderComponent.Flashes[MobState.Critical].Color), "dying has its own colour");
            Assert.That(server.EntMan.HasComponent<KsFieldCommanderDefenderComponent>(unitUid), Is.False);
        });
    }

    /// <summary>
    ///     A defender who was already in the round when the rule started is given a distress beacon as it readies them,
    ///         like one who spawns into it. A bot of theirs, with nobody in it, is not.
    /// </summary>
    [Test]
    public async Task DefenderAlreadyInTheRoundIsGivenABeacon()
    {
        var server = Pair.Server;
        var (testMap, _, playerBody, _) = await SetUpUnit(_humanDefender);

        await server.WaitAssertion(() =>
        {
            var inventorySystem = server.System<InventorySystem>();
            Assert.That(inventorySystem.TryGetSlotEntity(playerBody, KsDistressBeaconSystem.NeckSlot, out var neckUid), Is.True,
                "a defender in the round before the rule should have been given a beacon");
            Assert.That(server.EntMan.HasComponent<KsDistressBeaconComponent>(neckUid), Is.True);
        });
    }

    /// <summary>
    ///     A defender who dies to the enemy, the world, or their own hand costs no defender anything.
    /// </summary>
    [Test]
    public async Task SelfAndEnemyDamageCostsNothing()
    {
        var server = Pair.Server;
        var (_, ruleUid, playerBody, unitUid) = await SetUpUnit();

        // The player is the one who dies, and so the only defender there is to blame.
        await server.WaitPost(() =>
        {
            Hurt(playerBody, playerBody, 4); // their own doing
            Hurt(playerBody, unitUid, 3); // the enemy
            Hurt(playerBody, null, 3); // the world
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(playerBody).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(0), "nobody on the defenders' side hurt them"));
    }

    /// <summary>
    ///     Friendly fire that has healed over is forgotten: a defender hurt by their own side, patched up to full and
    ///         then killed by something else costs nobody anything. Left unhealed, the same hit is charged.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task HealedFriendlyFireIsNotChargedToALaterDeath(bool healed)
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, _) = await SetUpUnit();
        var penalty = 0;
        const int FriendlyDamage = 3;
        const int WorldDamage = 10;

        await server.WaitPost(() =>
        {
            var victimUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            penalty = KsFieldCommanderRuleSystem.GetTeamkillPenalty(ruleComponent, (float)FriendlyDamage / (FriendlyDamage + WorldDamage));
            Assert.That(penalty, Is.LessThan(0));

            Hurt(victimUid, playerBody, FriendlyDamage);
            if (healed)
                Hurt(victimUid, null, -FriendlyDamage);

            Hurt(victimUid, null, WorldDamage);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(victimUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(healed ? 0 : penalty),
                healed ? "the friendly fire had healed over before they died" : "the friendly fire was still on them as they died"));
    }

    /// <summary>
    ///     The penalty follows the defenders' share of the damage, but a teamkill is never free and never more than the
    ///         whole penalty.
    /// </summary>
    [Test]
    public async Task TeamkillPenaltyIsScaledAndHasAFloor()
    {
        var (_, ruleUid, _, _) = await SetUpUnit();

        await Pair.Server.WaitAssertion(() =>
        {
            var ruleComponent = Pair.Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var full = ruleComponent.TeamkillPoints;
            var minimum = ruleComponent.TeamkillMinimumPoints;
            Assert.That(minimum, Is.LessThan(0).And.GreaterThan(full), "the test needs a minimum between nothing and the whole penalty");

            Assert.That(KsFieldCommanderRuleSystem.GetTeamkillPenalty(ruleComponent, 1f), Is.EqualTo(full), "all their own side's doing");
            Assert.That(KsFieldCommanderRuleSystem.GetTeamkillPenalty(ruleComponent, 0.5f), Is.EqualTo((int)MathF.Round(full * 0.5f)), "half of it");
            Assert.That(KsFieldCommanderRuleSystem.GetTeamkillPenalty(ruleComponent, 0.0001f), Is.EqualTo(minimum), "a graze still costs the minimum");
        });
    }

    /// <summary>
    ///     A beam's damage names the gun it came from, not the shooter (<c>HitscanBasicDamageSystem</c>): a kill with a
    ///         gun in a player's hands pays the player. The same gun lying on the floor is nobody's.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task BeamKillPaysWhoeverHoldsTheGun(bool held)
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit(_humanDefender);
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            var gunUid = server.EntMan.SpawnAtPosition(_beamGun, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            if (held)
                Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(playerBody, gunUid, checkActionBlocker: false), Is.True, "the player should be holding the gun");

            Hurt(unitUid, gunUid, 10); // what a beam's hit passes as its origin
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(held ? killPoints : 0),
                held ? "a kill with a held beam gun should pay its holder" : "a gun nobody holds is nobody's"));
    }

    /// <summary>
    ///     A turret's hits are settled as they land: a unit it wounded that dies after the turret was destroyed is still
    ///         its owner's kill.
    /// </summary>
    [Test]
    public async Task TurretDestroyedBeforeTheKillStillPaysItsOwner()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);

            var turretUid = SpawnOwnedTurret(testMap, playerBody);
            var bulletUid = SpawnBullet(testMap, turretUid);
            Hurt(unitUid, bulletUid, 8);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.Not.EqualTo(MobState.Dead), "the test needs the unit to outlive the turret");

            server.EntMan.DeleteEntity(bulletUid);
            server.EntMan.DeleteEntity(turretUid);
        });

        await Pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            Hurt(unitUid, origin: null, 2); // it bleeds out, with the turret long gone
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the destroyed turret's owner should still be paid for its kill"));
    }

    /// <summary>
    ///     A turret answers to the player who set it up, not to the body they did it in: its owner, dead and back in a
    ///         new body, is still paid for what it kills.
    /// </summary>
    [Test]
    public async Task TurretPaysAnOwnerWhoRespawned()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;
        EntityUid turretUid = default;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            turretUid = SpawnOwnedTurret(testMap, playerBody);

            // The owner dies, their body is gone, and they come back in another.
            var newBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, -2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, newBody);
            server.EntMan.DeleteEntity(playerBody);
        });

        await Pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            Hurt(unitUid, SpawnBullet(testMap, turretUid), 10);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the turret should pay its owner in whatever body they are in now"));
    }

    /// <summary>
    ///     A turret whose owner is dead and has no body at all, a ghost, still pays them.
    /// </summary>
    [Test]
    public async Task TurretPaysAnOwnerWithNoBody()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody, unitUid) = await SetUpUnit();
        var killPoints = 0;

        await server.WaitPost(() =>
        {
            killPoints = GetKillPoints(ruleUid);
            var turretUid = SpawnOwnedTurret(testMap, playerBody);

            server.PlayerMan.SetAttachedEntity(Pair.Player!, null);
            server.EntMan.DeleteEntity(playerBody);

            Hurt(unitUid, SpawnBullet(testMap, turretUid), 10);
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead));
        });

        await server.WaitAssertion(() =>
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "a turret's owner is paid while they have no body"));
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

        await RunUntilDead(unitUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead),
                "the charge should have killed the unit");
            Assert.That(GetPointsChange(ruleUid, Pair.Player!.UserId), Is.EqualTo(killPoints), "the presser should be paid for the explosion");
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

        var nextWaveTime = TimeSpan.Zero;
        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var statusComponent = server.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(map);
            Assert.That(ruleComponent.WavesSent, Is.GreaterThan(0));
            Assert.That(statusComponent.Wave, Is.EqualTo(ruleComponent.WavesSent));
            Assert.That(statusComponent.NextWaveTime, Is.EqualTo(ruleComponent.NextWaveTime));
            Assert.That(statusComponent.NextWaveTime, Is.Not.Null, "a wave should be scheduled after the first");
            nextWaveTime = statusComponent.NextWaveTime!.Value;
        });

        // Another wave moves both.
        var wavesBefore = 0;
        var wavesAfter = 0;
        await server.WaitPost(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            wavesBefore = ruleComponent.WavesSent;
            server.System<KsFieldCommanderRuleSystem>().ForceWave((ruleUid, ruleComponent));
            wavesAfter = ruleComponent.WavesSent;
        });
        Assert.That(wavesAfter, Is.GreaterThan(wavesBefore), "forcing a wave should have counted one");

        await Pair.RunTicksSync(5);

        var clientMap = Pair.ToClientUid(map);
        await client.WaitAssertion(() =>
        {
            var statusComponent = client.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(clientMap);
            Assert.That(statusComponent.Wave, Is.EqualTo(wavesAfter), "the client should know another wave was sent");
            Assert.That(statusComponent.NextWaveTime, Is.GreaterThan(nextWaveTime), "and when the one after is due");
        });
    }
}
