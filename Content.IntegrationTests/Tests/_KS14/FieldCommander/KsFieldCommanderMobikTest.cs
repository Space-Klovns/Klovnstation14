#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.FieldCommander.Admin;
using Content.Server._KS14.GameTicking.Rules;
using Content.Server.Administration.Managers;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The ending: once the uplink is taken out and the commander's last units are dealt with, the commander himself is
///         announced and sent in, once, as a mobik. Killing him pays every defender standing and lets the round end, or
///         the vote on endless mode open. A commander with no mobik ends as it always did, a pause holds his pod, and
///         only an admin can send him early.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderMobikTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Nanotrasen = "Nanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcMobikRule";
    private readonly EntProtoId _voteRule = "KsTestFcMobikVoteRule";
    private readonly EntProtoId _noMobikRule = "KsTestFcMobikNoneRule";
    private readonly EntProtoId _earlyRule = "KsTestFcMobikEarlyRule";
    private readonly EntProtoId _defender = "KsTestFcMobikDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcMobikDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcMobikUnit
  components:
  - type: MobState

- type: entity
  id: KsTestFcMobikConscript
  components:
  - type: MobState

- type: entity
  parent: KsFieldCommandUplink
  id: KsTestFcMobikUplink
  components:
  - type: TimedDespawn
    lifetime: 1000

# The final phase lands the uplink at once and sends no waves of its own: the one wave is forced by the test.
- type: entity
  parent: BaseGameRule
  id: KsTestFcMobikRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 14
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: KsTestFcMobikUplink
    uplinkPoints: 0
    briefingAnnouncement: null
    endlessVote: false
    phases:
    - name: ks-fieldcommander-phase-final
      start: 0
      budget: 1
      deployUplink: true
    assets:
    - entity: KsTestFcMobikUnit
    mobik:
      unit:
        entity: KsTestFcMobikConscript
        killPoints: 0
      delay: 2
      bonusPoints: 70

- type: entity
  parent: KsTestFcMobikRule
  id: KsTestFcMobikVoteRule
  components:
  - type: KsFieldCommanderRule
    endlessVote: true
    endlessVoteDuration: 600

- type: entity
  parent: BaseGameRule
  id: KsTestFcMobikNoneRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 14
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: KsTestFcMobikUplink
    uplinkPoints: 0
    briefingAnnouncement: null
    endlessVote: false
    phases:
    - name: ks-fieldcommander-phase-final
      start: 0
      budget: 1
      deployUplink: true
    assets:
    - entity: KsTestFcMobikUnit

# No uplink and no waves: for sending him early, by hand.
- type: entity
  parent: BaseGameRule
  id: KsTestFcMobikEarlyRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 14
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: null
    briefingAnnouncement: null
    endlessVote: false
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
    mobik:
      unit:
        entity: KsTestFcMobikConscript
      delay: 2
";

    private EntityUid _ruleUid;
    private EntityUid _defenderUid;
    private Entity<ScenarioRuleComponent> _scenario;

    private KsFieldCommanderRuleComponent RuleComponent => Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(_ruleUid);

    private Entity<KsFieldCommanderRuleComponent> Rule => (_ruleUid, RuleComponent);

    private KsFieldCommanderRuleSystem RuleSystem => Server.System<KsFieldCommanderRuleSystem>();

    /// <summary>
    ///     A floor around the defenders' objective, with the test player as its one defender, and the rule started.
    /// </summary>
    private async Task Start(EntProtoId rule)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: 20);
            Server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            _defenderUid = Server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _defenderUid);

            var (scenarioUid, commanderUid) = KsFieldCommanderTestHelpers.StartRules(Server, rule);
            _ruleUid = commanderUid;
            _scenario = (scenarioUid, Server.EntMan.GetComponent<ScenarioRuleComponent>(scenarioUid));
        });

        await Pair.RunTicksSync(5);
    }

    /// <summary>
    ///     Sends the one wave, then takes the uplink out with that wave's unit still alive. Returns the unit.
    /// </summary>
    private async Task<EntityUid> SendWaveAndTakeTheUplink()
    {
        EntityUid unitUid = default;
        await Server.WaitPost(() =>
        {
            Assert.That(RuleComponent.UplinkUid, Is.Not.Null, "the final phase should have landed the uplink");

            RuleSystem.ForceWave(Rule);
            var units = LivingUnits(mobik: false);
            Assert.That(units, Has.Count.EqualTo(1), "the forced wave should have sent its one unit");
            unitUid = units[0];

            Server.System<TriggerSystem>().Trigger(RuleComponent.UplinkUid!.Value, _defenderUid, key: "win");
        });

        await Pair.RunTicksSync(2);
        return unitUid;
    }

    private List<EntityUid> LivingUnits(bool mobik)
    {
        var units = new List<EntityUid>();
        var mobStateSystem = Server.System<MobStateSystem>();
        var query = Server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.Mobik == mobik && !mobStateSystem.IsDead(unitUid))
                units.Add(unitUid);
        }

        return units;
    }

    private void Kill(EntityUid uid)
    {
        Server.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
    }

    [Test]
    public async Task HeComesOnceTheLastWaveIsClearedAndOnlyOnce()
    {
        await Start(_rule);
        var unitUid = await SendWaveAndTakeTheUplink();

        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.AwaitingClear),
                "taking the uplink should hold the round for the ending");
            Assert.That(_scenario.Comp.PendingObjectiveFactions, Does.Contain(Syndicate), "the round should be held open");
            Assert.That(_scenario.Comp.WinType, Is.Not.EqualTo(ScenarioWinType.Objective), "nor should it have ended over the uplink");
            Assert.That(RuleComponent.NextWaveTime, Is.EqualTo(TimeSpan.MaxValue), "no more waves should be due");
        });

        // Well past his delay, with the wave's unit still alive: he must not come yet.
        await RunSeconds((float)RuleComponent.Mobik!.Delay.TotalSeconds + 2f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.AwaitingClear),
                "he should wait while the last wave still has a unit alive");
            Assert.That(LivingUnits(mobik: true), Is.Empty);
            Kill(unitUid);
        });

        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Announced),
                "the last unit dead, he should be announced");
            Assert.That(LivingUnits(mobik: true), Is.Empty, "he should come only after his delay");
        });

        await RunSeconds((float)RuleComponent.Mobik!.Delay.TotalSeconds + 1f);
        EntityUid mobikUid = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Arrived));
            var mobiks = LivingUnits(mobik: true);
            Assert.That(mobiks, Has.Count.EqualTo(1), "exactly one mobik should have been sent");
            mobikUid = mobiks[0];
            Assert.That(RuleComponent.MobikUid, Is.EqualTo(mobikUid));
            Assert.That(Server.EntMan.GetComponent<ScenarioFactionMemberComponent>(mobikUid).Id, Is.EqualTo(Syndicate),
                "he fights for the commander's side");
            Assert.That(Server.EntMan.HasComponent<Content.Server.Ghost.Roles.Components.GhostRoleComponent>(mobikUid), Is.False,
                "he is never a ghost role");
            Assert.That(RuleSystem.TrySendMobik(Rule), Is.False, "he cannot be sent a second time");
            Kill(mobikUid);
        });

        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Done));
            Assert.That(RuleSystem.TrySendMobik(Rule), Is.False, "nor once it is over");
            Assert.That(LivingUnits(mobik: true), Is.Empty);
        });
    }

    [Test]
    public async Task KillingHimPaysAndEndsTheRound([Values] bool endlessVote)
    {
        await Start(endlessVote ? _voteRule : _rule);
        var unitUid = await SendWaveAndTakeTheUplink();
        await Server.WaitPost(() => Kill(unitUid));
        await RunSeconds(4f);

        var pointsBefore = 0;
        await Server.WaitPost(() =>
        {
            var mobiks = LivingUnits(mobik: true);
            Assert.That(mobiks, Has.Count.EqualTo(1), "he should have been sent");
            pointsBefore = RuleSystem.GetPoints(RuleComponent, Pair.Player!.UserId);
            Kill(mobiks[0]);

            Assert.That(RuleSystem.GetPoints(RuleComponent, Pair.Player!.UserId), Is.EqualTo(pointsBefore + RuleComponent.Mobik!.BonusPoints),
                "every defender standing should be paid the bonus for him");
        });

        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikKilled, Is.True);

            if (endlessVote)
            {
                Assert.That(RuleComponent.EndlessVoteRunning, Is.True, "his death should open the vote on endless mode");
                Assert.That(_scenario.Comp.PendingObjectiveFactions, Does.Contain(Syndicate), "held open while the vote runs");
                Assert.That(_scenario.Comp.WinType, Is.Not.EqualTo(ScenarioWinType.Objective));

                // Not left running for whatever comes after.
                foreach (var vote in Server.ResolveDependency<Content.Server.Voting.Managers.IVoteManager>().ActiveVotes.ToList())
                    vote.Cancel();
                return;
            }

            Assert.That(_scenario.Comp.PendingObjectiveFactions, Does.Not.Contain(Syndicate));
            Assert.That(_scenario.Comp.WinningFactionId, Is.EqualTo(Nanotrasen), "his death should win the defenders the round");
            Assert.That(_scenario.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));
        });
    }

    [Test]
    public async Task WithoutAMobikTheRoundEndsOverTheUplink()
    {
        await Start(_noMobikRule);
        await SendWaveAndTakeTheUplink();

        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.None));
            Assert.That(_scenario.Comp.WinningFactionId, Is.EqualTo(Nanotrasen), "taking the uplink wins the defenders the round");
            Assert.That(_scenario.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective),
                "with no mobik, the round should end over the uplink at once, its last unit alive or not");
            Assert.That(RuleSystem.TrySendMobik(Rule), Is.False, "there is nobody to send");
            Assert.That(LivingUnits(mobik: true), Is.Empty);
        });
    }

    [Test]
    public async Task PauseHoldsHisArrival()
    {
        await Start(_earlyRule);
        var consoleHost = Server.ResolveDependency<IConsoleHost>();

        var arrivalBefore = TimeSpan.Zero;
        var pausedAt = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            consoleHost.ExecuteCommand("ks_fcmobik");
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Announced), "the command should announce him");
            arrivalBefore = RuleComponent.MobikArrivalTime!.Value;

            pausedAt = Server.Timing.CurTime;
            consoleHost.ExecuteCommand("ks_fcpause on");
        });

        await RunSeconds((float)RuleComponent.Mobik!.Delay.TotalSeconds + 2f);

        await Server.WaitPost(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Announced), "his pod should wait while paused");
            Assert.That(LivingUnits(mobik: true), Is.Empty);

            var delta = Server.Timing.CurTime - pausedAt;
            consoleHost.ExecuteCommand("ks_fcpause off");
            Assert.That(RuleComponent.MobikArrivalTime, Is.EqualTo(arrivalBefore + delta), "his arrival should move on by the pause");
        });

        await RunSeconds((float)RuleComponent.Mobik!.Delay.TotalSeconds + 1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Arrived), "resumed, he should arrive");
            Assert.That(LivingUnits(mobik: true), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task TheAdminWindowSendsHimOnlyForAnAdmin()
    {
        await Start(_earlyRule);
        var ruleNetEntity = Server.EntMan.GetNetEntity(_ruleUid);

        await Server.WaitAssertion(() =>
        {
            var adminManager = Server.ResolveDependency<IAdminManager>();
            var session = Pair.Player!;
            if (adminManager.IsAdmin(session))
                adminManager.DeAdmin(session);

            Assert.That(adminManager.HasAdminFlag(session, KsFieldCommanderAdminEui.RequiredFlags), Is.False);
            Server.System<KsFieldCommanderAdminEuiSystem>().OpenEui(session).HandleMessage(new KsFieldCommanderAdminSendMobikMessage(ruleNetEntity));
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.None), "a non-admin sent the mobik");

            adminManager.PromoteHost(session);
            if (!adminManager.IsAdmin(session))
                adminManager.ReAdmin(session);

            Assert.That(adminManager.HasAdminFlag(session, KsFieldCommanderAdminEui.RequiredFlags), Is.True);
            Server.System<KsFieldCommanderAdminEuiSystem>().OpenEui(session).HandleMessage(new KsFieldCommanderAdminSendMobikMessage(ruleNetEntity));
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Announced), "an admin's window should send him");
        });

        await RunSeconds((float)RuleComponent.Mobik!.Delay.TotalSeconds + 1f);
        await Server.WaitAssertion(() =>
            Assert.That(LivingUnits(mobik: true), Has.Count.EqualTo(1), "his pod should have brought him"));
    }
}
