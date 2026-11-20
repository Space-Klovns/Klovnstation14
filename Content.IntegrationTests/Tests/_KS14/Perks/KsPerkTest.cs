#nullable enable
using System.Collections.Generic;
using Content.Client._KS14.Perks.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._KS14.FieldCommander;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.Perks;
using Content.Shared.Actions.Components;
using Content.Shared.Alert;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Speech;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     Perks: what one grants is taken back exactly, the picker can't be closed or fooled, and a field commander
///         defender's pick follows them to every later body.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedKsPerkSystem))]
[TestOf(typeof(SharedKsPerkChoiceSystem))]
public sealed class KsPerkTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _objective = "KsTestPerkObjective";
    private readonly EntProtoId _scenarioRule = "KsTestPerkScenarioRule";
    private readonly EntProtoId _body = "KsTestPerkBody";
    private readonly EntProtoId _defender = "KsTestPerkDefender";
    private readonly EntProtoId _commanderRule = "KsTestPerkCommanderRule";
    private readonly EntProtoId _newEffect = "KsTestPerkEffectNew";
    private readonly EntProtoId _timedEffect = "KsTestPerkEffectTimed";
    private readonly ProtoId<AlertPrototype> _alert = "KsTestPerkAlert";
    private readonly ProtoId<KsPerkPrototype> _grantsPerk = "KsTestPerkGrants";
    private readonly ProtoId<KsPerkPrototype> _perkA = "KsTestPerkA";
    private readonly ProtoId<KsPerkPrototype> _perkB = "KsTestPerkB";
    private readonly ProtoId<KsPerkPrototype> _missingPerk = "KsTestPerkThatDoesNotExist";

    [TestPrototypes]
    private const string Prototypes = @"
- type: alert
  id: KsTestPerkAlert
  icons:
  - sprite: _KS14/Interface/Perks/perks.rsi
    state: damage
  name: ks-perk-damage-name
  description: ks-perk-damage-desc

- type: entity
  id: KsTestPerkEffectNew
  components:
  - type: StatusEffect

- type: entity
  id: KsTestPerkEffectTimed
  components:
  - type: StatusEffect

- type: entity
  id: KsTestPerkAction
  components:
  - type: Action
  - type: InstantAction
    event: !type:ScreamActionEvent

- type: entity
  id: KsTestPerkItem
  components:
  - type: Item

- type: ksPerk
  id: KsTestPerkGrants
  name: ks-perk-damage-name
  description: ks-perk-damage-desc
  icon:
    sprite: _KS14/Interface/Perks/perks.rsi
    state: damage
  alert: KsTestPerkAlert
  components:
  - type: Speech
  - type: PointLight
  statusEffects:
  - KsTestPerkEffectNew
  - KsTestPerkEffectTimed
  actions:
  - KsTestPerkAction
  items:
  - KsTestPerkItem

- type: ksPerk
  id: KsTestPerkA
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon:
    sprite: _KS14/Interface/Perks/perks.rsi
    state: tank

- type: ksPerk
  id: KsTestPerkB
  name: ks-perk-support-name
  description: ks-perk-support-desc
  icon:
    sprite: _KS14/Interface/Perks/perks.rsi
    state: support

- type: entity
  id: KsTestPerkBody
  components:
  - type: Hands
  - type: StatusEffectContainer
  - type: Alerts
  - type: Speech

- type: entity
  parent: KsTestPerkBody
  id: KsTestPerkDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestPerkObjective
  components:
  - type: ScenarioObjective
    factionId: Nanotrasen

- type: entity
  id: KsTestPerkScenarioRule
  components:
  - type: GameRule
    minPlayers: 0
  - type: ScenarioRule

- type: entity
  id: KsTestPerkCommanderRule
  components:
  - type: GameRule
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
    perkOptions:
    - KsTestPerkA
    - KsTestPerkB
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 1000
";

    /// <summary>
    ///     A perk's items are for its holder using the body: they go when the holder dies or the player leaves for
    ///         another body, so nothing is left lying about for anyone, and come back when the body is revived with
    ///         its player in it, or the player returns. A body revived with nobody in it gets none.
    /// </summary>
    [Test]
    public async Task PerkItemsGoWithDeathOrThePlayerAndComeBackWithThem()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var perkSystem = server.System<SharedKsPerkSystem>();
        var mindSystem = server.System<Content.Server.Mind.MindSystem>();
        var mobStateSystem = server.System<Content.Shared.Mobs.Systems.MobStateSystem>();
        EntityUid bodyUid = default;
        EntityUid otherBodyUid = default;
        EntityUid mindUid = default;
        EntityUid itemUid = default;

        List<EntityUid> Items() => new(entMan.GetComponent<KsPerkHolderComponent>(bodyUid).Grants[_grantsPerk].Items);

        await server.WaitPost(() =>
        {
            bodyUid = entMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            otherBodyUid = entMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            mindUid = mindSystem.CreateMind(null).Owner;
            mindSystem.TransferTo(mindUid, bodyUid);
            Assert.That(perkSystem.TryAddPerk(bodyUid, _grantsPerk), "the test perk should be given");
            itemUid = Items()[0];
            mobStateSystem.ChangeMobState(bodyUid, Content.Shared.Mobs.MobState.Dead);
        });
        await Pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.Deleted(itemUid), "the perk's item should go when its holder dies");
            Assert.That(Items(), Is.Empty);
            mobStateSystem.ChangeMobState(bodyUid, Content.Shared.Mobs.MobState.Alive);
            Assert.That(Items(), Has.Count.EqualTo(1), "a body revived with its player in it should get the item back");
            itemUid = Items()[0];

            mindSystem.TransferTo(mindUid, otherBodyUid);
        });
        await Pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.Deleted(itemUid), "the item should go when the player leaves the body for another");
            mobStateSystem.ChangeMobState(bodyUid, Content.Shared.Mobs.MobState.Dead);
            mobStateSystem.ChangeMobState(bodyUid, Content.Shared.Mobs.MobState.Alive);
            Assert.That(Items(), Is.Empty, "a body revived with nobody in it should get no item");

            mindSystem.TransferTo(mindUid, bodyUid);
            Assert.That(Items(), Has.Count.EqualTo(1), "the item should come back when the player returns to the body");
            Assert.That(perkSystem.HasPerk(bodyUid, _grantsPerk), "the perk itself stays all along");
        });
    }

    /// <summary>
    ///     Everything a perk adds goes when it does, and what the body had before (a component, a timed status effect)
    ///         is left as it was.
    /// </summary>
    [Test]
    public async Task GrantThenRemovalRestoresEverything()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var perkSystem = server.System<SharedKsPerkSystem>();
        var statusEffectsSystem = server.System<StatusEffectsSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid bodyUid = default;
        SpeechComponent? speechBefore = null;
        TimeSpan timedEndBefore = default;
        var actionUids = new List<EntityUid>();
        var itemUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            bodyUid = server.EntMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            speechBefore = server.EntMan.GetComponent<SpeechComponent>(bodyUid);
            Assert.That(statusEffectsSystem.TrySetStatusEffectDuration(bodyUid, _timedEffect, TimeSpan.FromSeconds(100)), Is.True);
        });

        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(statusEffectsSystem.TryGetStatusEffect(bodyUid, _timedEffect, out var timedUid), Is.True);
            timedEndBefore = server.EntMan.GetComponent<StatusEffectComponent>(timedUid!.Value).EndEffectTime!.Value;

            Assert.That(perkSystem.TryAddPerk(bodyUid, _grantsPerk), Is.True);
            Assert.That(perkSystem.TryAddPerk(bodyUid, _grantsPerk), Is.False, "a perk already held was given again");
            Assert.That(perkSystem.HasPerk(bodyUid, _grantsPerk), Is.True);

            var holderComponent = server.EntMan.GetComponent<KsPerkHolderComponent>(bodyUid);
            var grant = holderComponent.Grants[_grantsPerk];
            actionUids.AddRange(grant.Actions);
            itemUids.AddRange(grant.Items);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<PointLightComponent>(bodyUid), Is.True, "the perk's new component wasn't added");
                Assert.That(server.EntMan.GetComponent<SpeechComponent>(bodyUid), Is.SameAs(speechBefore), "a component the body had was replaced");
                Assert.That(grant.Components, Is.EquivalentTo(new[] { "PointLight" }), "the grant should record only the component it added");

                Assert.That(statusEffectsSystem.TryGetStatusEffect(bodyUid, _newEffect, out var newUid), Is.True);
                Assert.That(server.EntMan.GetComponent<StatusEffectComponent>(newUid!.Value).EndEffectTime, Is.Null, "the perk's status effect isn't permanent");
                statusEffectsSystem.TryGetStatusEffect(bodyUid, _timedEffect, out var timedUid2);
                Assert.That(server.EntMan.GetComponent<StatusEffectComponent>(timedUid2!.Value).EndEffectTime, Is.Null, "the running status effect wasn't made permanent");

                Assert.That(actionUids, Has.Count.EqualTo(1));
                Assert.That(server.EntMan.GetComponent<ActionsComponent>(bodyUid).Actions, Does.Contain(actionUids[0]));
                Assert.That(itemUids, Has.Count.EqualTo(1));
                Assert.That(server.EntMan.EntityExists(itemUids[0]), Is.True);

                Assert.That(server.System<AlertsSystem>().IsShowingAlert(bodyUid, _alert), Is.True, "the perk's alert isn't shown");
            });
        });

        // The client hears which perks the body holds.
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            var clientBodyUid = ToClientUid(bodyUid);
            Assert.That(Client.System<SharedKsPerkSystem>().HasPerk(clientBodyUid, _grantsPerk), Is.True, "the client doesn't know the body holds the perk");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(perkSystem.RemovePerk(bodyUid, _grantsPerk), Is.True);
            Assert.That(perkSystem.RemovePerk(bodyUid, _grantsPerk), Is.False, "a perk not held was removed");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(perkSystem.HasPerk(bodyUid, _grantsPerk), Is.False);
                Assert.That(server.EntMan.HasComponent<PointLightComponent>(bodyUid), Is.False, "the perk's component stayed");
                Assert.That(server.EntMan.GetComponent<SpeechComponent>(bodyUid), Is.SameAs(speechBefore), "a component the body had before was taken");

                Assert.That(statusEffectsSystem.HasStatusEffect(bodyUid, _newEffect), Is.False, "the perk's status effect stayed");
                Assert.That(statusEffectsSystem.TryGetStatusEffect(bodyUid, _timedEffect, out var timedUid), Is.True, "the status effect running before was taken");
                Assert.That(server.EntMan.GetComponent<StatusEffectComponent>(timedUid!.Value).EndEffectTime, Is.EqualTo(timedEndBefore),
                    "the status effect running before didn't go back to its own end time");
                Assert.That(timedEndBefore, Is.GreaterThan(timing.CurTime));

                Assert.That(server.EntMan.Deleted(actionUids[0]), Is.True, "the perk's action stayed");
                Assert.That(server.EntMan.GetComponent<ActionsComponent>(bodyUid).Actions, Does.Not.Contain(actionUids[0]));
                Assert.That(server.EntMan.Deleted(itemUids[0]), Is.True, "the perk's item stayed");

                Assert.That(server.System<AlertsSystem>().IsShowingAlert(bodyUid, _alert), Is.False, "the perk's alert stayed");
            });
        });
    }

    /// <summary>
    ///     Spawns a body for the player and opens the picker on it.
    /// </summary>
    private async Task<EntityUid> OpenPicker()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid bodyUid = default;

        await server.WaitPost(() =>
        {
            bodyUid = server.EntMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, bodyUid);
            Assert.That(server.System<SharedKsPerkChoiceSystem>().RequestChoice(bodyUid, new[] { _perkA, _perkB }), Is.True);
        });

        await Pair.RunTicksSync(5);
        AssertPickerOpen(bodyUid);
        return bodyUid;
    }

    private void AssertPickerOpen(EntityUid bodyUid)
    {
        Assert.That(Pair.Server.System<SharedUserInterfaceSystem>().IsUiOpen(bodyUid, KsPerkChoiceUiKey.Key, bodyUid), Is.True,
            "the picker isn't open on the server");
    }

    private async Task SendFromClient(EntityUid bodyUid, ProtoId<KsPerkPrototype> perk)
    {
        await Client.WaitPost(() =>
        {
            Assert.That(Client.System<SharedUserInterfaceSystem>().TryGetOpenUi(ToClientUid(bodyUid), KsPerkChoiceUiKey.Key, out var bui), Is.True,
                "the picker isn't open on the client");
            bui!.SendMessage(new KsPerkChoiceSelectMessage(perk));
        });

        await Pair.RunTicksSync(5);
    }

    /// <summary>
    ///     The window refuses to close, and if the interface closes anyway the server opens it again.
    /// </summary>
    [Test]
    public async Task ClosingThePickerReopensIt()
    {
        var bodyUid = await OpenPicker();

        // The window itself: escape, close-all and click-away all end in Close().
        await Client.WaitAssertion(() =>
        {
            var menu = new KsPerkChoiceMenu();
            menu.OpenCentered();
            menu.Close();
            Assert.That(menu.IsOpen, Is.True, "the picker window closed while locked");
            menu.Locked = false;
            menu.Close();
            Assert.That(menu.IsOpen, Is.False);
        });

        // The interface, closed from the client.
        await Client.WaitPost(() =>
        {
            Assert.That(Client.System<SharedUserInterfaceSystem>().TryGetOpenUi(ToClientUid(bodyUid), KsPerkChoiceUiKey.Key, out var bui), Is.True);
            bui!.Close();
        });

        await Pair.RunTicksSync(5);
        AssertPickerOpen(bodyUid);
        await Client.WaitAssertion(() =>
            Assert.That(Client.System<SharedUserInterfaceSystem>().IsUiOpen(ToClientUid(bodyUid), KsPerkChoiceUiKey.Key), Is.True,
                "the picker didn't come back on the client"));

        // And closed from the server.
        await Pair.Server.WaitPost(() => Pair.Server.System<SharedUserInterfaceSystem>().CloseUi(bodyUid, KsPerkChoiceUiKey.Key, bodyUid));
        await Pair.RunTicksSync(5);
        AssertPickerOpen(bodyUid);

        await Pair.Server.WaitPost(() => Pair.Server.EntMan.DeleteEntity(bodyUid));
    }

    /// <summary>
    ///     A perk that wasn't offered, or doesn't exist, is refused and the picker stays; an offered one is taken.
    /// </summary>
    [Test]
    public async Task InvalidPerkIsRejected()
    {
        var server = Pair.Server;
        var choiceSystem = server.System<SharedKsPerkChoiceSystem>();
        var perkSystem = server.System<SharedKsPerkSystem>();
        var bodyUid = await OpenPicker();

        foreach (var perk in new[] { _grantsPerk, _missingPerk })
        {
            await SendFromClient(bodyUid, perk);
            await server.WaitAssertion(() =>
            {
                Assert.That(choiceSystem.IsChoosing(bodyUid), Is.True, $"picking {perk} ended the choice");
                Assert.That(perkSystem.HasPerk(bodyUid, perk), Is.False, $"{perk} was given");
                AssertPickerOpen(bodyUid);
            });
        }

        await SendFromClient(bodyUid, _perkB);
        await server.WaitAssertion(() =>
        {
            Assert.That(choiceSystem.IsChoosing(bodyUid), Is.False);
            Assert.That(perkSystem.HasPerk(bodyUid, _perkB), Is.True, "the offered perk wasn't given");
            Assert.That(server.System<SharedUserInterfaceSystem>().IsUiOpen(bodyUid, KsPerkChoiceUiKey.Key), Is.False, "the picker stayed open");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(bodyUid));
    }

    /// <summary>
    ///     A field commander defender is asked once; their pick is kept for the round and given to the next body the
    ///         ticker spawns them in, without asking again.
    /// </summary>
    [Test]
    public async Task FieldCommanderChoiceIsReappliedOnTheNextSpawn()
    {
        var server = Pair.Server;
        var choiceSystem = server.System<SharedKsPerkChoiceSystem>();
        var perkSystem = server.System<SharedKsPerkSystem>();
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid firstBody = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 8);
            server.EntMan.SpawnAtPosition(_objective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            firstBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, firstBody);
            // The scenario rule first, as the field commander preset starts them.
            var ticker = server.System<Content.Server.GameTicking.GameTicker>();
            Assert.That(ticker.StartGameRule(_scenarioRule, out _), Is.True);
            Assert.That(ticker.StartGameRule(_commanderRule, out ruleUid), Is.True);
        });

        // The rule prepares the defenders already there, and asks.
        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(choiceSystem.IsChoosing(firstBody), Is.True, "the defender wasn't asked for a perk"));

        await SendFromClient(firstBody, _perkA);
        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.ChosenPerks[Pair.Player!.UserId], Is.EqualTo(_perkA), "the pick wasn't stored");
            Assert.That(perkSystem.HasPerk(firstBody, _perkA), Is.True);
        });

        EntityUid secondBody = default;
        await server.WaitPost(() =>
        {
            secondBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, -2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, secondBody);
            server.EntMan.EventBus.RaiseLocalEvent(secondBody,
                new PlayerSpawnCompleteEvent(secondBody, Pair.Player!, null, lateJoin: true, silent: true, 0, EntityUid.Invalid, new HumanoidCharacterProfile()),
                broadcast: true);
        });

        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(perkSystem.HasPerk(secondBody, _perkA), Is.True, "the stored pick wasn't given to the new body");
            Assert.That(choiceSystem.IsChoosing(secondBody), Is.False, "the defender was asked again");
            Assert.That(server.System<SharedUserInterfaceSystem>().IsUiOpen(secondBody, KsPerkChoiceUiKey.Key), Is.False, "the picker opened again");
        });

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(Pair.Player!, null);
            server.System<Content.Server.GameTicking.GameTicker>().EndGameRule(ruleUid);
        });
    }
}
