#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.GameTicking.Rules;
using Content.Server.GameTicking;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Destructible;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Scenario;

/// <summary>
///     The winner of a scenario objective is the faction that took it, never the faction that owned it, for
///         every faction, and the round end summary names that faction.
/// </summary>
[TestFixture]
[TestOf(typeof(ScenarioSystem))]
public sealed class KsScenarioWinnerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId ScenarioRule = "Scenario";
    private static readonly EntProtoId SyndicateObjective = "KsScenarioObjectiveSyndie";
    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    private static readonly ProtoId<ScenarioFactionPrototype> Nanotrasen = "Nanotrasen";
    private static readonly ProtoId<ScenarioFactionPrototype> Syndicate = "Syndicate";
    private static readonly ProtoId<ScenarioFactionPrototype> Scavengers = "Scavengers";
    private static readonly ProtoId<ScenarioFactionPrototype> Redacted = "Redacted";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _scavengerObjective = "KsTestScenarioObjectiveScavengers";
    private readonly EntProtoId _redactedObjective = "KsTestScenarioObjectiveRedacted";
    private readonly EntProtoId _nanotrasenMember = "KsTestScenarioMemberNanotrasen";
    private readonly EntProtoId _syndicateMember = "KsTestScenarioMemberSyndicate";
    private readonly EntProtoId _scavengerMember = "KsTestScenarioMemberScavengers";
    private readonly EntProtoId _redactedMember = "KsTestScenarioMemberRedacted";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsScenarioObjectiveSyndie
  id: KsTestScenarioObjectiveScavengers
  components:
  - type: ScenarioObjective
    factionId: Scavengers

- type: entity
  parent: KsScenarioObjectiveSyndie
  id: KsTestScenarioObjectiveRedacted
  components:
  - type: ScenarioObjective
    factionId: Redacted

- type: entity
  id: KsTestScenarioMemberNanotrasen
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestScenarioMemberSyndicate
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Syndicate

- type: entity
  id: KsTestScenarioMemberScavengers
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Scavengers

- type: entity
  id: KsTestScenarioMemberRedacted
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Redacted
";

    /// <summary>
    ///     A user from <paramref name="capturer"/> captures the lone objective owned by <paramref name="owner"/>.
    ///         With a single objective on the map, nothing else can name the winner.
    /// </summary>
    [TestCase("Syndicate", "Nanotrasen")]
    [TestCase("Nanotrasen", "Syndicate")]
    [TestCase("Scavengers", "Syndicate")]
    [TestCase("Redacted", "Nanotrasen")]
    [TestCase("Syndicate", "Scavengers")]
    [TestCase("Nanotrasen", "Redacted")]
    public async Task CapturingFactionWins(string ownerName, string capturerName)
    {
        var server = Pair.Server;
        var owner = new ProtoId<ScenarioFactionPrototype>(ownerName);
        var capturer = new ProtoId<ScenarioFactionPrototype>(capturerName);

        var map = await Pair.CreateTestMap();
        Entity<ScenarioRuleComponent>? rule = null;

        await server.WaitPost(() =>
        {
            var ticker = server.EntMan.System<GameTicker>();
            Assert.That(ticker.StartGameRule(ScenarioRule, out var ruleUid), Is.True);
            rule = (ruleUid, server.EntMan.GetComponent<ScenarioRuleComponent>(ruleUid));

            var objectiveUid = server.EntMan.SpawnEntity(ObjectiveFor(owner), map.GridCoords);
            var userUid = server.EntMan.SpawnEntity(MemberFor(capturer), map.GridCoords);

            server.EntMan.System<TriggerSystem>().Trigger(objectiveUid, userUid, key: "win");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(capturer),
                $"{capturerName} captured {ownerName}'s objective, so {capturerName} should have won");
            Assert.That(rule.Value.Comp.WinType, Is.EqualTo(ScenarioWinType.Objective));

            var loc = server.ResolveDependency<ILocalizationManager>();
            var textEvent = new RoundEndTextAppendEvent();
            server.EntMan.EventBus.RaiseEvent(Robust.Shared.GameObjects.EventSource.Local, textEvent);

            var capturerPrototype = server.ProtoMan.Index(capturer);
            Assert.That(textEvent.Text, Does.Contain(loc.GetString(capturerPrototype.VictoryLocId)));
            Assert.That(textEvent.Text, Does.Contain(loc.GetString(capturerPrototype.WinTypeLocIds[ScenarioWinType.Objective])));
        });
    }

    /// <summary>
    ///     A destroyed objective has no user to look at, so the single living enemy faction is the winner.
    /// </summary>
    [Test]
    public async Task DestroyedObjectiveIsWonByTheOnlyLivingEnemy()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        Entity<ScenarioRuleComponent>? rule = null;

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.System<GameTicker>().StartGameRule(ScenarioRule, out var ruleUid), Is.True);
            rule = (ruleUid, server.EntMan.GetComponent<ScenarioRuleComponent>(ruleUid));

            var objectiveUid = server.EntMan.SpawnEntity(SyndicateObjective, map.GridCoords);
            server.EntMan.SpawnEntity(_nanotrasenMember, map.GridCoords);

            var destruction = new DestructionEventArgs();
            server.EntMan.EventBus.RaiseLocalEvent(objectiveUid, destruction, broadcast: false);
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Nanotrasen),
                "the owner of a destroyed objective must not be named the winner"));
    }

    /// <summary>
    ///     An objective that nobody took before its timer ran out was defended: its owner wins.
    /// </summary>
    [Test]
    public async Task DefendedObjectiveIsWonByItsOwner()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        Entity<ScenarioRuleComponent>? rule = null;

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.System<GameTicker>().StartGameRule(ScenarioRule, out var ruleUid), Is.True);
            rule = (ruleUid, server.EntMan.GetComponent<ScenarioRuleComponent>(ruleUid));

            var objectiveUid = server.EntMan.SpawnEntity(NanotrasenObjective, map.GridCoords);

            var timeout = new TimedDespawnEvent();
            server.EntMan.EventBus.RaiseLocalEvent(objectiveUid, ref timeout, broadcast: false);
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Nanotrasen)));
    }

    /// <summary>
    ///     An objective destroyed by an unknown party, with several enemy factions alive, cannot name a capturer.
    ///         That must not wipe the winner an earlier defence decided.
    /// </summary>
    [Test]
    public async Task UnresolvedCapturerKeepsDecidedWinner()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        Entity<ScenarioRuleComponent>? rule = null;
        EntityUid syndicateObjectiveUid = default;

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.System<GameTicker>().StartGameRule(ScenarioRule, out var ruleUid), Is.True);
            rule = (ruleUid, server.EntMan.GetComponent<ScenarioRuleComponent>(ruleUid));

            var nanotrasenObjectiveUid = server.EntMan.SpawnEntity(NanotrasenObjective, map.GridCoords);
            syndicateObjectiveUid = server.EntMan.SpawnEntity(SyndicateObjective, map.GridCoords);
            server.EntMan.SpawnEntity(_nanotrasenMember, map.GridCoords);
            server.EntMan.SpawnEntity(_scavengerMember, map.GridCoords);
            server.EntMan.SpawnEntity(_redactedMember, map.GridCoords);

            var timeout = new TimedDespawnEvent();
            server.EntMan.EventBus.RaiseLocalEvent(nanotrasenObjectiveUid, ref timeout, broadcast: false);
        });

        await server.WaitAssertion(() => Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Nanotrasen)));

        await server.WaitPost(() =>
            server.EntMan.EventBus.RaiseLocalEvent(syndicateObjectiveUid, new DestructionEventArgs(), broadcast: false));

        await server.WaitAssertion(() => Assert.That(rule!.Value.Comp.WinningFactionId, Is.EqualTo(Nanotrasen),
            "an unresolved capturer must not clear the decided winner"));
    }

    /// <summary>
    ///     Deleting objectives (round cleanup) is not a capture: it must not mark the round as objective-won.
    /// </summary>
    [Test]
    public async Task DeletingObjectiveIsNotACapture()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        Entity<ScenarioRuleComponent>? rule = null;

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.System<GameTicker>().StartGameRule(ScenarioRule, out var ruleUid), Is.True);
            rule = (ruleUid, server.EntMan.GetComponent<ScenarioRuleComponent>(ruleUid));

            var objectiveUid = server.EntMan.SpawnEntity(SyndicateObjective, map.GridCoords);
            server.EntMan.DeleteEntity(objectiveUid);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(rule!.Value.Comp.WinType, Is.EqualTo(ScenarioWinType.Decimation));
            Assert.That(rule.Value.Comp.WinningFactionId, Is.Null);
        });
    }

    /// <summary>
    ///     The opening round-end line names every faction in play.
    /// </summary>
    [Test]
    public async Task SkirmishLineListsFactionsInPlay()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.System<GameTicker>().StartGameRule(ScenarioRule, out _), Is.True);
            server.EntMan.SpawnEntity(NanotrasenObjective, map.GridCoords);
            server.EntMan.SpawnEntity(SyndicateObjective, map.GridCoords);
            server.EntMan.SpawnEntity(_scavengerMember, map.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var loc = server.ResolveDependency<ILocalizationManager>();
            var textEvent = new RoundEndTextAppendEvent();
            server.EntMan.EventBus.RaiseEvent(Robust.Shared.GameObjects.EventSource.Local, textEvent);

            foreach (var faction in new[] { Nanotrasen, Syndicate, Scavengers })
                Assert.That(textEvent.Text, Does.Contain(loc.GetString(server.ProtoMan.Index(faction).NameLocId)));
        });
    }

    private EntProtoId ObjectiveFor(ProtoId<ScenarioFactionPrototype> faction)
    {
        if (faction == Syndicate)
            return SyndicateObjective;

        if (faction == Nanotrasen)
            return NanotrasenObjective;

        if (faction == Scavengers)
            return _scavengerObjective;

        Assert.That(faction, Is.EqualTo(Redacted));
        return _redactedObjective;
    }

    private EntProtoId MemberFor(ProtoId<ScenarioFactionPrototype> faction)
    {
        if (faction == Syndicate)
            return _syndicateMember;

        if (faction == Nanotrasen)
            return _nanotrasenMember;

        if (faction == Scavengers)
            return _scavengerMember;

        Assert.That(faction, Is.EqualTo(Redacted));
        return _redactedMember;
    }
}
