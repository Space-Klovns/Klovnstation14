#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.KillCredit;
using Content.Server._KS14.Mortar;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Content.Server.Administration.Logs;
using Content.Server.NPC;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Projectiles;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Whose an explosion is - in the admin log, for kill credit and on the Field Commander's damage ledger - when what set it
///         off is not who fired it: a tank's shell, whose hit names what it hit; a mortar shell, whose landing names nobody; a
///         drone that blows itself up. A defender who shells another with a captured mortar is charged with it; an enemy's
///         shell is nobody's on the defenders' side.
/// </summary>
[TestFixture]
[TestOf(typeof(KsKillCreditSystem))]
[TestOf(typeof(KsMortarSystem))]
public sealed class KsFieldCommanderExplosionAttributionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, AdminLogsEnabled = true, DummyTicker = false };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcBlameRule";
    private readonly EntProtoId _defender = "KsTestFcBlameDefender";
    private readonly EntProtoId _enemy = "KsTestFcBlameEnemy";
    private readonly EntProtoId _shell = "KsTestFcBlameTankShell";
    private readonly EntProtoId _mortar = "KsTestFcBlameMortar";
    private readonly EntProtoId _crew = "KsTestFcBlameMortarCrew";
    private readonly EntProtoId _drone = "KsTestFcBlameDrone";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcBlameDefender
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
  - type: NpcFactionMember
    factions:
    - NanoTrasen

- type: entity
  id: KsTestFcBlameEnemy
  components:
  - type: NpcFactionMember
    factions:
    - Syndicate

- type: entity
  id: KsTestFcBlameTankShell
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 0
  - type: ExplodeOnTrigger
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 5
    maxIntensity: 5

- type: entity
  parent: SupplyPod
  id: KsTestFcBlameMortarShell
  components:
  - type: SupplyPod
    height: 10
    fallDuration: 0.5s
    fallSoundDelay: 0s
  - type: TriggerOnSupplyPodLanded
  - type: ExplosionOnTrigger
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 5
    maxTileIntensity: 5
    maxTileBreak: 0
  - type: DeleteOnTrigger

- type: entity
  id: KsTestFcBlameMortar
  components:
  - type: Transform
    anchored: true
  - type: KsMortar
    shell: KsTestFcBlameMortarShell
    rounds: 3
    interval: 0.5
    manualCooldown: 1
    minRange: 3
    maxRange: 30
    clusterRadius: 3
    spread: 0
    idleTime: 60
    flightTime: 0.5
    emptyTime: 60
    abandonedFactions:
    - NanoTrasen

- type: entity
  id: KsTestFcBlameMortarCrew
  components:
  - type: MobState
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: KsMortarCrew
    mortar: KsTestFcBlameMortar
    setUpTime: 0.2

- type: entity
  id: KsTestFcBlameDrone
  components:
  - type: TimerTrigger
    delay: 0.2
  - type: Explosive
    explosionType: Default
    totalIntensity: 10
    intensitySlope: 5
    maxIntensity: 5
  - type: ExplodeOnTrigger
    keysIn:
    - timer

- type: entity
  parent: BaseGameRule
  id: KsTestFcBlameRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      budget: 5
    assets:
    - entity: KsTestFcBlameEnemy
";

    // Where things stand: the player's body by the objective, the defender a shell lands on well clear of it, and a mortar
    //     ten tiles east of that defender, out of its range of the player.
    private static readonly Vector2 PlayerPosition = new(-20.5f, 0.5f);
    private static readonly Vector2 VictimPosition = new(8.5f, 0.5f);

    private EntityUid _playerBody;
    private EntityUid _victimUid;

    private async Task SetUp()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: 24);
            SEntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, PlayerPosition.X, -4.5f));

            _playerBody = SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, PlayerPosition));
            Name(_playerBody, "player");
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _playerBody);

            _victimUid = SEntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, VictimPosition));
            Name(_victimUid, "victim");

            KsFieldCommanderTestHelpers.StartRules(Server, _rule);
        });

        await Pair.RunTicksSync(3);
    }

    /// <summary>
    ///     Gives <paramref name="uid"/> a name nothing else in the round has, to find its log line by and to tell it apart in one.
    /// </summary>
    private string Name(EntityUid uid, string what)
    {
        var name = $"blame-{what}-{Guid.NewGuid():N}";
        SEntMan.System<MetaDataSystem>().SetEntityName(uid, name);
        return name;
    }

    private string NameOf(EntityUid uid) => SEntMan.GetComponent<MetaDataComponent>(uid).EntityName;

    /// <summary>
    ///     Runs until the victim has taken damage, up to a cap well past any of these blasts.
    /// </summary>
    private async Task WaitForVictimHurt()
    {
        var ticks = (int) (10f * (float) Server.ResolveDependency<IGameTiming>().TickRate);
        for (var tick = 0; tick < ticks; tick += 5)
        {
            await Pair.RunTicksSync(5);

            var hurt = false;
            await Server.WaitPost(() => hurt = SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(_victimUid) > 0);
            if (hurt)
            {
                // The rest of the blast, which is dealt over a few ticks.
                await Pair.RunTicksSync(10);
                return;
            }
        }

        Assert.Fail("the test needs the blast to hurt the defender it lands on");
    }

    /// <summary>
    ///     The admin log's explosion line for <paramref name="explosiveName"/>.
    /// </summary>
    private async Task<string> ExplosionLog(string explosiveName)
    {
        var adminLogManager = Server.ResolveDependency<IAdminLogManager>();
        await PoolManager.WaitUntil(Server, async () =>
            (await adminLogManager.CurrentRoundLogs(new LogFilter { Search = explosiveName, Types = [LogType.Explosion] })).Count > 0);

        return (await adminLogManager.CurrentRoundLogs(new LogFilter { Search = explosiveName, Types = [LogType.Explosion] })).Single().Message;
    }

    private void AssertNotOnDefendersSide()
    {
        var ledgerComponent = SEntMan.GetComponentOrNull<KsFieldCommanderDamageLedgerComponent>(_victimUid);
        Assert.That(ledgerComponent, Is.Not.Null, "the defender's damage should be on its ledger");
        Assert.That(ledgerComponent!.Damage, Is.Empty, "an enemy's blast should be charged to no defender");
        Assert.That(ledgerComponent.OtherDamage, Is.GreaterThan(0f), "but counted as the enemy's");
    }

    /// <summary>
    ///     A tank's shell hits the player and goes off on the defender beside: its hit names the player, and the blast used to
    ///         be the player's in the admin log. It is the turret's, and no defender's on the ledger.
    /// </summary>
    [Test]
    public async Task ATankShellIsTheTurretsNotWhatItHit()
    {
        await SetUp();
        string shellName = "", turretName = "";

        await Server.WaitPost(() =>
        {
            var turretUid = SEntMan.SpawnAtPosition(_enemy, new EntityCoordinates(Transform(_victimUid).ParentUid, VictimPosition + new Vector2(0f, 12f)));
            turretName = Name(turretUid, "turret");

            var shellUid = SEntMan.SpawnAtPosition(_shell, Transform(_victimUid).Coordinates);
            shellName = Name(shellUid, "shell");
            SEntMan.System<SharedProjectileSystem>().SetShooter(shellUid, SEntMan.GetComponent<ProjectileComponent>(shellUid), turretUid);

            // As its collision does: the user is what it hit.
            SEntMan.System<TriggerSystem>().Trigger(shellUid, user: _playerBody);
        });

        await WaitForVictimHurt();
        var message = await ExplosionLog(shellName);

        await Server.WaitAssertion(() =>
        {
            Assert.That(message, Does.Contain(turretName), $"the turret that fired it should be blamed: {message}");
            Assert.That(message, Does.Not.Contain(NameOf(_playerBody)), $"not the player it hit: {message}");
            AssertNotOnDefendersSide();
        });
    }

    /// <summary>
    ///     The player fires a captured mortar onto another defender: that is the player's friendly fire, in the log and on the
    ///         ledger. Its landing names nobody, and it used to be nobody's.
    /// </summary>
    [Test]
    public async Task ACapturedMortarShellIsWhoeverFiredIt()
    {
        await SetUp();
        EntityUid mortarUid = default;

        await Server.WaitPost(() =>
        {
            mortarUid = SEntMan.SpawnAtPosition(_mortar, new EntityCoordinates(Transform(_victimUid).ParentUid, VictimPosition + new Vector2(10f, 0f)));
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(SEntMan.System<KsMortarSystem>().TryFireAt(mortar, _playerBody, Angle.FromDegrees(270), 10f), "the test needs the player firing it, west onto the defender");
        });

        var shellName = await NameTheShell();
        await WaitForVictimHurt();
        var message = await ExplosionLog(shellName);

        await Server.WaitAssertion(() =>
        {
            Assert.That(message, Does.Contain(NameOf(_playerBody) + " "), $"the player who fired it should be blamed: {message}");

            var ledgerComponent = SEntMan.GetComponentOrNull<KsFieldCommanderDamageLedgerComponent>(_victimUid);
            Assert.That(ledgerComponent?.Damage.GetValueOrDefault(Pair.Player!.UserId), Is.GreaterThan(0f),
                "shelling another defender should be charged to the player as friendly fire");
        });
    }

    /// <summary>
    ///     An enemy crewman shells the defender: the crewman's, and no defender's.
    /// </summary>
    [Test]
    public async Task AnEnemyMortarShellIsTheCrewmans()
    {
        await SetUp();
        string crewName = "";

        await Server.WaitPost(() =>
        {
            var crewUid = SEntMan.SpawnAtPosition(_crew, new EntityCoordinates(Transform(_victimUid).ParentUid, VictimPosition + new Vector2(10f, 0f)));
            crewName = Name(crewUid, "crew");
        });

        var shellName = await NameTheShell();
        await WaitForVictimHurt();
        var message = await ExplosionLog(shellName);

        await Server.WaitAssertion(() =>
        {
            Assert.That(message, Does.Contain(crewName + " "), $"the crewman who fired it should be blamed: {message}");
            AssertNotOnDefendersSide();
        });
    }

    /// <summary>
    ///     A drone arms itself beside the defender, as its last task does: its own blast, and no defender's.
    /// </summary>
    [Test]
    public async Task ADroneIsItsOwnBlast()
    {
        await SetUp();
        string droneName = "";

        await Server.WaitPost(() =>
        {
            var droneUid = SEntMan.SpawnAtPosition(_drone, Transform(_victimUid).Coordinates);
            droneName = Name(droneUid, "drone");

            var detonateOperator = new KsDetonateOperator();
            SEntMan.EntitySysManager.DependencyCollection.InjectDependencies(detonateOperator);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, droneUid);
            detonateOperator.Startup(blackboard);
        });

        await WaitForVictimHurt();
        var message = await ExplosionLog(droneName);

        await Server.WaitAssertion(() =>
        {
            Assert.That(message, Does.StartWith(droneName + " "), $"the drone should be blamed for its own blast: {message}");
            Assert.That(message, Does.Contain("caused"), $"as having set it off: {message}");
            AssertNotOnDefendersSide();
        });
    }

    /// <summary>
    ///     Waits for the mortar's shell to be on its way and names it.
    /// </summary>
    private async Task<string> NameTheShell()
    {
        var ticks = (int) (10f * (float) Server.ResolveDependency<IGameTiming>().TickRate);
        string? shellName = null;

        for (var tick = 0; tick < ticks && shellName == null; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var query = SEntMan.AllEntityQueryEnumerator<Content.Shared._KS14.SupplyPod.SupplyPodComponent>();
                if (query.MoveNext(out var shellUid, out _))
                    shellName = Name(shellUid, "mortar-shell");
            });
        }

        Assert.That(shellName, Is.Not.Null, "the mortar should have fired");
        return shellName!;
    }

    private TransformComponent Transform(EntityUid uid) => SEntMan.GetComponent<TransformComponent>(uid);
}
