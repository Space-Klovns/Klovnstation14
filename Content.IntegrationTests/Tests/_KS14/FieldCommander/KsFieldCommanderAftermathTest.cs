#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.GameTicking.Rules;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Light.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The ending unit's aftermath: its steps are taken only once he is dead, in order and once each, each after its
///         delay. Lights out covers the defenders' map only, an announcement is sent, groups arrive well apart with nothing
///         said, and the round waits for them. With no steps, his death ends the round as before.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderAftermathTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";
    private static readonly EntProtoId Light = "Poweredlight";
    private static readonly ProtoId<ScenarioFactionPrototype> Nanotrasen = "Nanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";

    private const string AnnouncementText = "KsTestFcAftermathAnnouncementText";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcAftermathRule";
    private readonly EntProtoId _noStepsRule = "KsTestFcAftermathNoneRule";
    private readonly EntProtoId _defender = "KsTestFcAftermathDefender";

    // Step delays, in seconds, as the prototype below sets them.
    private const float AnnouncementDelay = 3f;
    private const float GroupsDelay = 3f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcAftermathDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcAftermathUnit
  components:
  - type: MobState

- type: entity
  parent: BaseGameRule
  id: KsTestFcAftermathNoneRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 8
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
        entity: KsTestFcAftermathUnit
      delay: 1
      announcement: null
      intel: null
      killedIntel: null

- type: entity
  parent: KsTestFcAftermathNoneRule
  id: KsTestFcAftermathRule
  components:
  - type: KsFieldCommanderRule
    mobik:
      unit:
        entity: KsTestFcAftermathUnit
      delay: 1
      announcement: null
      intel: null
      killedIntel: null
      aftermath:
      - lightsOut: true
      - delay: 3
        announcement:
          text:
            en-US: KsTestFcAftermathAnnouncementText
      - delay: 3
        groups:
        - units:
          - entity: KsTestFcAftermathUnit
            count: 2
        - units:
          - entity: KsTestFcAftermathUnit
            count: 2
";

    private EntityUid _ruleUid;
    private EntityUid _defenderUid;
    private EntityUid _lightUid;
    private EntityUid _otherMapLightUid;
    private Entity<ScenarioRuleComponent> _scenario;

    private KsFieldCommanderRuleComponent RuleComponent => Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(_ruleUid);

    private Entity<KsFieldCommanderRuleComponent> Rule => (_ruleUid, RuleComponent);

    /// <summary>
    ///     A floor around the defenders' objective with the test player its one defender and a light, a light on another
    ///         map, the rule started, and the ending unit sent and arrived.
    /// </summary>
    private async Task StartAndSendHim(EntProtoId rule)
    {
        var testMap = await Pair.CreateTestMap();
        var otherMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: 20);
            Server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            _defenderUid = Server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _defenderUid);
            _lightUid = Server.EntMan.SpawnAtPosition(Light, new EntityCoordinates(testMap.Grid, -2.5f, 0.5f));
            _otherMapLightUid = Server.EntMan.SpawnAtPosition(Light, new EntityCoordinates(otherMap.Grid, 0.5f, 0.5f));

            var (scenarioUid, commanderUid) = KsFieldCommanderTestHelpers.StartRules(Server, rule);
            _ruleUid = commanderUid;
            _scenario = (scenarioUid, Server.EntMan.GetComponent<ScenarioRuleComponent>(scenarioUid));
        });

        await Pair.RunTicksSync(5);
        await Server.WaitPost(() => Assert.That(Server.System<KsFieldCommanderRuleSystem>().TrySendMobik(Rule), Is.True));
        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Arrived));
            Assert.That(LightOn(_lightUid), Is.True, "nothing of the aftermath should come before his death");
            Assert.That(GroupUnits(), Is.Empty);
        });
    }

    private bool LightOn(EntityUid uid)
    {
        return Server.EntMan.GetComponent<PoweredLightComponent>(uid).On;
    }

    private List<(EntityUid Uid, int Group)> GroupUnits()
    {
        var units = new List<(EntityUid, int)>();
        var query = Server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
        while (query.MoveNext(out var unitUid, out var unitComponent))
        {
            if (unitComponent.AftermathStep >= 0)
                units.Add((unitUid, unitComponent.AftermathGroup));
        }

        return units;
    }

    private int CountChat(string? containing = null)
    {
        var history = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().History;
        return containing == null ? history.Count : history.Count(entry => entry.Msg.Message.Contains(containing));
    }

    private void Kill(EntityUid uid)
    {
        Server.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
    }

    [Test]
    public async Task StepsFollowHisDeathInOrderOnce()
    {
        await StartAndSendHim(_rule);

        // The client's chat history may hold what came before this test.
        var announcementsBefore = 0;
        await Client.WaitPost(() => announcementsBefore = CountChat(AnnouncementText));

        await Server.WaitPost(() => Kill(RuleComponent.MobikUid!.Value));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Aftermath));
            Assert.That(LightOn(_lightUid), Is.False, "the first step should put the defenders' lights out at once");
            Assert.That(LightOn(_otherMapLightUid), Is.True, "a light on another map should stay on");
            Assert.That(GroupUnits(), Is.Empty, "the groups come last");
        });
        await Client.WaitAssertion(() => Assert.That(CountChat(AnnouncementText), Is.EqualTo(announcementsBefore), "the announcement comes after its delay"));

        await RunSeconds(AnnouncementDelay + 0.5f);
        var chatAfterAnnouncement = 0;
        await Server.WaitAssertion(() => Assert.That(GroupUnits(), Is.Empty, "the groups come after their own delay"));
        await Client.WaitAssertion(() =>
        {
            Assert.That(CountChat(AnnouncementText), Is.EqualTo(announcementsBefore + 1), "the announcement should have been sent, once");
            chatAfterAnnouncement = CountChat();
        });

        await RunSeconds(GroupsDelay + 0.5f);
        List<(EntityUid Uid, int Group)> units = new();
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.AftermathClear));
            units = GroupUnits();
            Assert.That(units.Count(unit => unit.Group == 0), Is.EqualTo(2), "the first group, whole");
            Assert.That(units.Count(unit => unit.Group == 1), Is.EqualTo(2), "the second group, whole");

            // Each group stands together, and the two at bearings well apart around the defender.
            var transformSystem = Server.System<SharedTransformSystem>();
            var defender = transformSystem.GetMapCoordinates(_defenderUid).Position;
            var bearings = new Angle[2];
            for (var group = 0; group < 2; group++)
            {
                var position = transformSystem.GetMapCoordinates(units.First(unit => unit.Group == group).Uid).Position;
                bearings[group] = (position - defender).ToWorldAngle();
            }

            Assert.That(Math.Abs(Angle.ShortestDistance(bearings[0], bearings[1]).Degrees), Is.GreaterThanOrEqualTo(90),
                "the groups should come from well-separated sides");

            Assert.That(_scenario.Comp.PendingObjectiveFactions, Does.Contain(Syndicate), "the round should wait for the groups");
            Assert.That(_scenario.Comp.WinType, Is.Not.EqualTo(ScenarioWinType.Objective));
        });
        await Client.WaitAssertion(() =>
            Assert.That(CountChat(), Is.EqualTo(chatAfterAnnouncement), "nothing should be said of the groups"));

        // One left standing still holds the round.
        await Server.WaitPost(() =>
        {
            foreach (var (uid, _) in units.Skip(1))
                Kill(uid);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.AftermathClear), "one group unit is still alive");
            Kill(units[0].Uid);
        });

        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Done));
            Assert.That(_scenario.Comp.WinningFactionId, Is.EqualTo(Nanotrasen), "the groups dead, the defenders win");
            Assert.That(_scenario.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));
        });

        // Taken once: nothing comes again.
        await RunSeconds(AnnouncementDelay + GroupsDelay);
        await Server.WaitAssertion(() => Assert.That(GroupUnits(), Has.Count.EqualTo(4), "no step should be taken twice"));
        await Client.WaitAssertion(() => Assert.That(CountChat(AnnouncementText), Is.EqualTo(announcementsBefore + 1), "no step should be taken twice"));
    }

    [Test]
    public async Task WithoutStepsHisDeathEndsTheRound()
    {
        await StartAndSendHim(_noStepsRule);

        await Server.WaitPost(() => Kill(RuleComponent.MobikUid!.Value));
        await RunSeconds(AnnouncementDelay + GroupsDelay);

        await Server.WaitAssertion(() =>
        {
            Assert.That(RuleComponent.MobikStage, Is.EqualTo(KsFieldCommanderMobikStage.Done));
            Assert.That(LightOn(_lightUid), Is.True, "no step, no lights out");
            Assert.That(GroupUnits(), Is.Empty, "no step, nothing sent");
            Assert.That(_scenario.Comp.WinningFactionId, Is.EqualTo(Nanotrasen));
            Assert.That(_scenario.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));
        });
    }

    [Test]
    public async Task PauseHoldsTheSteps()
    {
        await StartAndSendHim(_rule);
        var consoleHost = Server.ResolveDependency<Robust.Shared.Console.IConsoleHost>();

        await Server.WaitPost(() => Kill(RuleComponent.MobikUid!.Value));
        await Pair.RunTicksSync(3);
        var dueBefore = TimeSpan.Zero;
        var pausedAt = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            dueBefore = RuleComponent.NextAftermathTime!.Value;
            pausedAt = Server.Timing.CurTime;
            consoleHost.ExecuteCommand("ks_fcpause on");
        });

        await RunSeconds(AnnouncementDelay + GroupsDelay + 1f);
        await Server.WaitPost(() =>
        {
            Assert.That(RuleComponent.AftermathStepIndex, Is.EqualTo(1), "no step should be taken while paused");
            Assert.That(GroupUnits(), Is.Empty);
            var delta = Server.Timing.CurTime - pausedAt;
            consoleHost.ExecuteCommand("ks_fcpause off");
            Assert.That(RuleComponent.NextAftermathTime, Is.EqualTo(dueBefore + delta), "the next step should move on by the pause");
        });

        await RunSeconds(AnnouncementDelay + GroupsDelay + 1f);
        await Server.WaitAssertion(() =>
            Assert.That(GroupUnits(), Has.Count.EqualTo(4), "resumed, the steps should carry on"));
    }
}
