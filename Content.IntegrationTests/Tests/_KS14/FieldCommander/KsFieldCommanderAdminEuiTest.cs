#nullable enable
using System.Collections.Generic;
using System.Linq;
using KsFieldCommanderAdminWindow = Content.Client._KS14.FieldCommander.Admin.KsFieldCommanderAdminWindow;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server._KS14.FieldCommander.Admin;
using Content.Server.Administration.Managers;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.GameTicking.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The field commander admin window's server side: only an admin's messages are acted on, each reaches the
///         commander it names, bad input changes nothing, and the state it sends carries what the tabs show.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderAdminEui))]
public sealed class KsFieldCommanderAdminEuiTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcAdminEuiRule";
    private readonly EntProtoId _unit = "KsTestFcAdminEuiUnit";
    private readonly EntProtoId _otherUnit = "KsTestFcAdminEuiOtherUnit";
    private readonly EntProtoId _missing = "KsTestFcAdminEuiNoSuchPrototype";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcAdminEuiUnit
  components:
  - type: MobState

- type: entity
  id: KsTestFcAdminEuiOtherUnit
  components:
  - type: MobState

- type: entity
  parent: BaseGameRule
  id: KsTestFcAdminEuiRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    hardMaxAlive: 20
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 1000
      waveInterval: 1000
      budget: 5
    - name: ks-fieldcommander-phase-siege
      start: 2000
      waveInterval: 500
      budget: 9
    assets:
    - entity: KsTestFcAdminEuiUnit
      count: 2
      cost: 3
      weight: 4
      minPhase: 1
      maxAlive: 5
      delivery: Pod
      tags: [ infantry, elite ]
      guaranteedEveryWaves: 3
      guaranteedOnPhaseEnter: true
    - entity: KsTestFcAdminEuiUnit
      disabled: true
";

    private EntityUid _ruleUid;

    private KsFieldCommanderRuleComponent RuleComponent => Server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(_ruleUid);

    private NetEntity RuleNetEntity => Server.EntMan.GetNetEntity(_ruleUid);

    /// <summary>
    ///     Starts the test commander in its first phase, with the test player an admin or not.
    /// </summary>
    private async Task StartCommander(bool admin)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            Server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            _ruleUid = KsFieldCommanderTestHelpers.StartRules(Server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var adminManager = Server.ResolveDependency<IAdminManager>();
            var session = Pair.Player!;
            if (admin)
            {
                adminManager.PromoteHost(session);
                if (!adminManager.IsAdmin(session))
                    adminManager.ReAdmin(session);
            }
            else if (adminManager.IsAdmin(session))
            {
                adminManager.DeAdmin(session);
            }

            Assert.That(adminManager.HasAdminFlag(session, KsFieldCommanderAdminEui.RequiredFlags), Is.EqualTo(admin));

            Server.System<KsFieldCommanderRuleSystem>().SetPhase(
                (_ruleUid, RuleComponent, Server.EntMan.GetComponent<GameRuleComponent>(_ruleUid)), 0);
        });

        await Pair.RunTicksSync(3);
    }

    /// <summary>
    ///     Handles a message as one arriving from the test player's window: opened for them, as the command opens it.
    /// </summary>
    private void Handle(KsFieldCommanderAdminEuiMessage message)
    {
        var eui = Server.System<KsFieldCommanderAdminEuiSystem>().OpenEui(Pair.Player!);
        eui.HandleMessage(message);
    }

    [Test]
    public async Task ANonAdminsMessagesAreIgnored()
    {
        await StartCommander(admin: false);

        await Server.WaitAssertion(() =>
        {
            var component = RuleComponent;
            var nextWave = component.NextWaveTime;
            var cost = component.Assets[0].Cost;
            var budget = component.Phases[0].Budget;
            var assets = component.Assets.Count;

            var messages = new KsFieldCommanderAdminEuiMessage[]
            {
                new KsFieldCommanderAdminSetPausedMessage(RuleNetEntity, true),
                new KsFieldCommanderAdminForceWaveMessage(RuleNetEntity),
                new KsFieldCommanderAdminSetPhaseMessage(RuleNetEntity, 1),
                new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 0, null, null, 99f),
                new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Cost: 77f)),
                new KsFieldCommanderAdminAddAssetMessage(RuleNetEntity, new KsFieldCommanderAdminAssetValues(Entity: _unit.Id)),
                new KsFieldCommanderAdminSetAssetDisabledMessage(RuleNetEntity, 0, true),
                new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, [new KsFieldCommanderAdminPurchase(0, 1)], "ignored", false),
            };

            foreach (var message in messages)
                Handle(message);

            Assert.Multiple(() =>
            {
                Assert.That(Server.System<KsFieldCommanderRuleSystem>().IsPaused(component), Is.False, "a non-admin paused the commander");
                Assert.That(component.NextWaveTime, Is.EqualTo(nextWave), "a non-admin forced a wave");
                Assert.That(component.PhaseIndex, Is.Zero, "a non-admin moved the phase");
                Assert.That(component.Phases[0].Budget, Is.EqualTo(budget), "a non-admin edited a phase");
                Assert.That(component.Assets[0].Cost, Is.EqualTo(cost), "a non-admin edited an asset");
                Assert.That(component.Assets, Has.Count.EqualTo(assets), "a non-admin added an asset");
                Assert.That(component.Assets[0].Disabled, Is.False, "a non-admin disabled an asset");
                Assert.That(component.QueuedWaves, Is.Empty, "a non-admin queued a wave");
            });

            Assert.That(Server.System<KsFieldCommanderAdminEuiSystem>().TryGetEui(Pair.Player!, out _), Is.False,
                "the window stays open for a player who is no admin");
        });
    }

    [Test]
    public async Task EachMessageChangesTheCommander()
    {
        await StartCommander(admin: true);

        await Server.WaitAssertion(() =>
        {
            var system = Server.System<KsFieldCommanderRuleSystem>();
            var component = RuleComponent;

            Handle(new KsFieldCommanderAdminSetPausedMessage(RuleNetEntity, true));
            Assert.That(system.IsPaused(component), Is.True, "pause");
            Handle(new KsFieldCommanderAdminSetPausedMessage(RuleNetEntity, false));
            Assert.That(system.IsPaused(component), Is.False, "unpause");

            var nextWave = component.NextWaveTime;
            Handle(new KsFieldCommanderAdminForceWaveMessage(RuleNetEntity));
            Assert.That(component.NextWaveTime, Is.Not.EqualTo(nextWave), "a forced wave counts the wait for the next from now");

            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 1, System.TimeSpan.FromSeconds(1500), System.TimeSpan.FromSeconds(42), 12.5f));
            Assert.That(component.Phases[1].Start, Is.EqualTo(System.TimeSpan.FromSeconds(1500)), "phase start");
            Assert.That(component.Phases[1].WaveInterval, Is.EqualTo(System.TimeSpan.FromSeconds(42)), "wave interval");
            Assert.That(component.Phases[1].Budget, Is.EqualTo(12.5f), "budget");

            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(
                Entity: _otherUnit.Id, Count: 3, Cost: 6f, Weight: 7f, MinPhase: 0, MaxAlive: 8, Delivery: (byte)KsFieldCommanderDelivery.Foot)));
            var asset = component.Assets[0];
            Assert.Multiple(() =>
            {
                Assert.That(asset.Entity, Is.EqualTo(_otherUnit), "prototype");
                Assert.That(asset.Count, Is.EqualTo(3), "count");
                Assert.That(asset.Cost, Is.EqualTo(6f), "cost");
                Assert.That(asset.Weight, Is.EqualTo(7f), "weight");
                Assert.That(asset.MinPhase, Is.Zero, "min phase");
                Assert.That(asset.MaxAlive, Is.EqualTo(8), "max alive");
                Assert.That(asset.Delivery, Is.EqualTo(KsFieldCommanderDelivery.Foot), "delivery");
            });

            var count = component.Assets.Count;
            Handle(new KsFieldCommanderAdminAddAssetMessage(RuleNetEntity, new KsFieldCommanderAdminAssetValues(Entity: _otherUnit.Id)));
            Assert.That(component.Assets, Has.Count.EqualTo(count + 1), "add");
            Assert.That(component.Assets[count].Entity, Is.EqualTo(_otherUnit));

            Handle(new KsFieldCommanderAdminSetAssetDisabledMessage(RuleNetEntity, 1, false));
            Assert.That(component.Assets[1].Disabled, Is.False, "enable");
            Handle(new KsFieldCommanderAdminSetAssetDisabledMessage(RuleNetEntity, 0, true));
            Assert.That(component.Assets[0].Disabled, Is.True, "disable");
            Assert.That(component.Assets, Has.Count.EqualTo(count + 1), "disabling removes nothing");

            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity,
                [new KsFieldCommanderAdminPurchase(1, 2), new KsFieldCommanderAdminPurchase(count, 1)], "  Here they come.  ", false));
            Assert.That(component.QueuedWaves, Has.Count.EqualTo(1), "queue");
            Assert.That(component.QueuedWaves[0].Announcement, Is.EqualTo("Here they come."));
            Assert.That(component.QueuedWaves[0].Purchases.Select(purchase => (purchase.Asset, purchase.Count)),
                Is.EqualTo(new[] { (1, 2), (count, 1) }));

            // No defenders here, so the wave cannot go and waits at the front, ahead of the one queued before.
            // A new interval, so that the forced wave's next one lands somewhere else than the last forced one's.
            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 0, null, System.TimeSpan.FromSeconds(77), null));
            nextWave = component.NextWaveTime;
            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, [new KsFieldCommanderAdminPurchase(1, 1)], "Now.", true));
            Assert.That(component.QueuedWaves, Has.Count.EqualTo(2), "send now");
            Assert.That(component.QueuedWaves[0].Announcement, Is.EqualTo("Now."), "a wave sent now goes ahead of the queue");
            Assert.That(component.NextWaveTime, Is.Not.EqualTo(nextWave), "a wave sent now is forced");

            Handle(new KsFieldCommanderAdminSetPhaseMessage(RuleNetEntity, 1));
            Assert.That(component.PhaseIndex, Is.EqualTo(1), "set phase");
        });
    }

    [Test]
    public async Task BadInputIsRefused()
    {
        await StartCommander(admin: true);

        await Server.WaitAssertion(() =>
        {
            var component = RuleComponent;
            var asset = component.Assets[0];
            var (entity, cost, weight) = (asset.Entity, asset.Cost, asset.Weight);
            var phase = component.Phases[0];
            var (start, interval, budget) = (phase.Start, phase.WaveInterval, phase.Budget);
            var assets = component.Assets.Count;

            // Unknown prototypes.
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Entity: _missing.Id)));
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Humanoid: _missing.Id)));
            Handle(new KsFieldCommanderAdminAddAssetMessage(RuleNetEntity, new KsFieldCommanderAdminAssetValues(Entity: _missing.Id)));
            Handle(new KsFieldCommanderAdminAddAssetMessage(RuleNetEntity, new KsFieldCommanderAdminAssetValues(Cost: 1f)));

            // Non-finite and out-of-range numbers.
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Cost: float.NaN)));
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Weight: float.PositiveInfinity)));
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Delivery: 200)));
            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 0, null, null, float.NegativeInfinity));
            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 0, component.Phases[1].Start + System.TimeSpan.FromSeconds(1), null, null));

            // Bad indices.
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 99, new KsFieldCommanderAdminAssetValues(Cost: 1f)));
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, -1, new KsFieldCommanderAdminAssetValues(Cost: 1f)));
            Handle(new KsFieldCommanderAdminSetAssetDisabledMessage(RuleNetEntity, 99, true));
            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 99, null, null, 1f));
            Handle(new KsFieldCommanderAdminSetPhaseMessage(RuleNetEntity, 99));
            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, [new KsFieldCommanderAdminPurchase(99, 1)], null, false));
            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, [new KsFieldCommanderAdminPurchase(1, 1)], null, false)); // disabled
            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, [], null, false));
            Handle(new KsFieldCommanderAdminQueueWaveMessage(RuleNetEntity, null!, null, false));

            // A message naming something that is not a running commander is not applied to the one that is.
            var notARule = Server.EntMan.GetNetEntity(Server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace));
            Handle(new KsFieldCommanderAdminEditAssetMessage(notARule, 0, new KsFieldCommanderAdminAssetValues(Cost: 9f)));
            Handle(new KsFieldCommanderAdminSetPausedMessage(notARule, true));
            Handle(new KsFieldCommanderAdminEditPhaseMessage(NetEntity.Invalid, 0, null, null, 3f));

            Assert.Multiple(() =>
            {
                Assert.That(asset.Entity, Is.EqualTo(entity), "an unknown prototype was taken");
                Assert.That(asset.Cost, Is.EqualTo(cost), "a bad cost was taken");
                Assert.That(asset.Weight, Is.EqualTo(weight), "a bad weight was taken");
                Assert.That(asset.Delivery, Is.EqualTo(KsFieldCommanderDelivery.Pod), "an undefined delivery was taken");
                Assert.That(phase.Start, Is.EqualTo(start), "a start out of order was taken");
                Assert.That(phase.WaveInterval, Is.EqualTo(interval));
                Assert.That(phase.Budget, Is.EqualTo(budget), "a bad budget was taken");
                Assert.That(component.Assets, Has.Count.EqualTo(assets), "a bad asset was added");
                Assert.That(component.Assets.Any(a => a.Disabled && a != component.Assets[1]), Is.False, "a bad index disabled something");
                Assert.That(component.PhaseIndex, Is.Zero, "a bad phase index moved the phase");
                Assert.That(component.QueuedWaves, Is.Empty, "a bad wave was queued");
                Assert.That(Server.System<KsFieldCommanderRuleSystem>().IsPaused(component), Is.False, "a stale commander was paused");
            });

            // Huge numbers are kept to the server's limits.
            Handle(new KsFieldCommanderAdminEditAssetMessage(RuleNetEntity, 0, new KsFieldCommanderAdminAssetValues(Count: int.MaxValue, Cost: 1e30f, MaxAlive: -4)));
            Assert.That(asset.Count, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedCount));
            Assert.That(asset.Cost, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedCost));
            Assert.That(asset.MaxAlive, Is.Zero);
            Handle(new KsFieldCommanderAdminEditPhaseMessage(RuleNetEntity, 1, null, System.TimeSpan.MaxValue, 1e30f));
            Assert.That(component.Phases[1].WaveInterval, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedTime));
            Assert.That(component.Phases[1].Budget, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedBudget));
        });
    }

    [Test]
    public async Task TheStateCarriesWhatTheTabsShow()
    {
        await StartCommander(admin: true);

        await Server.WaitPost(() =>
        {
            var component = RuleComponent;
            Assert.That(Server.System<KsFieldCommanderRuleSystem>().TryQueueWave((_ruleUid, component),
                new List<(int, int)> { (0, 2) }, "Queued."), Is.True);

            // Opened as an admin opens it.
            Server.ResolveDependency<IConsoleHost>().ExecuteCommand(Pair.Player!, "ks_fcadmin");
        });

        await Pair.RunTicksSync(10);

        KsFieldCommanderAdminEuiState? state = null;
        await Client.WaitAssertion(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<KsFieldCommanderAdminWindow>()
                .SingleOrDefault();
            Assert.That(window, Is.Not.Null, "ks_fcadmin opened no window on the client");
            state = window!.State;
            Assert.That(state, Is.Not.Null, "the window got no state");
        });

        await Server.WaitAssertion(() =>
        {
            var component = RuleComponent;
            Assert.That(state!.Rule, Is.EqualTo(RuleNetEntity));

            Assert.That(state.Status.PhaseIndex, Is.Zero);
            Assert.That(state.Status.PhaseName, Is.EqualTo(component.Phases[0].Name));
            Assert.That(state.Status.Paused, Is.False);
            Assert.That(state.Status.NextWaveIn, Is.Not.Null);
            Assert.That(state.Status.QueuedWaves, Has.Count.EqualTo(1), "the queue");
            Assert.That(state.Status.QueuedWaves[0].Announcement, Is.EqualTo("Queued."));
            Assert.That(state.Status.QueuedWaves[0].Purchases, Is.EqualTo(new[] { new KsFieldCommanderAdminPurchase(0, 2) }));

            Assert.That(state.Phases, Has.Count.EqualTo(component.Phases.Count));
            for (var i = 0; i < component.Phases.Count; i++)
            {
                Assert.That(state.Phases[i].Name, Is.EqualTo(component.Phases[i].Name));
                Assert.That(state.Phases[i].Start, Is.EqualTo(component.Phases[i].Start));
                Assert.That(state.Phases[i].WaveInterval, Is.EqualTo(component.Phases[i].WaveInterval));
                Assert.That(state.Phases[i].Budget, Is.EqualTo(component.Phases[i].Budget));
            }

            Assert.That(state.Assets, Has.Count.EqualTo(component.Assets.Count));
            for (var i = 0; i < component.Assets.Count; i++)
            {
                var asset = component.Assets[i];
                var data = state.Assets[i];
                Assert.Multiple(() =>
                {
                    Assert.That(data.Entity, Is.EqualTo(asset.Entity?.Id));
                    Assert.That(data.Humanoid, Is.EqualTo(asset.Humanoid?.Id));
                    Assert.That(data.Count, Is.EqualTo(asset.Count));
                    Assert.That(data.Cost, Is.EqualTo(asset.Cost));
                    Assert.That(data.Weight, Is.EqualTo(asset.Weight));
                    Assert.That(data.MinPhase, Is.EqualTo(asset.MinPhase));
                    Assert.That(data.MaxAlive, Is.EqualTo(asset.MaxAlive));
                    Assert.That(state.DeliveryNames[data.Delivery], Is.EqualTo(asset.Delivery.ToString()));
                    Assert.That(data.Tags, Is.EquivalentTo(asset.Tags));
                    Assert.That(data.GuaranteedEveryWaves, Is.EqualTo(asset.GuaranteedEveryWaves));
                    Assert.That(data.GuaranteedOnPhaseEnter, Is.EqualTo(asset.GuaranteedOnPhaseEnter));
                    Assert.That(data.Disabled, Is.EqualTo(asset.Disabled));
                    Assert.That(data.Living, Is.Zero);
                });
            }

            // So the test checks something other than defaults.
            Assert.That(state.Assets[0].Tags, Is.Not.Empty);
            Assert.That(state.Assets[0].GuaranteedOnPhaseEnter, Is.True);
            Assert.That(state.Assets[1].Disabled, Is.True);
            Assert.That(state.MaxCost, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedCost));
        });

        // A living unit is counted against its asset.
        await Server.WaitPost(() =>
        {
            var unitUid = Server.EntMan.SpawnEntity(_unit, MapCoordinates.Nullspace);
            var unitComponent = Server.EntMan.AddComponent<KsFieldCommanderUnitComponent>(unitUid);
            SetUnit(unitComponent, _ruleUid, 0);
            Server.System<KsFieldCommanderAdminEuiSystem>().OpenEui(Pair.Player!);
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<KsFieldCommanderAdminWindow>()
                .Single();
            Assert.That(window.State!.Assets[0].Living, Is.EqualTo(1), "a living unit is not counted");
            window.Close();
        });
    }

    /// <summary>
    ///     Makes a unit of the commander's, as a wave would: its fields are the rule system's to write, so by reflection.
    /// </summary>
    private static void SetUnit(KsFieldCommanderUnitComponent component, EntityUid ruleUid, int assetIndex)
    {
        typeof(KsFieldCommanderUnitComponent).GetField(nameof(KsFieldCommanderUnitComponent.RuleUid))!.SetValue(component, ruleUid);
        typeof(KsFieldCommanderUnitComponent).GetField(nameof(KsFieldCommanderUnitComponent.AssetIndex))!.SetValue(component, assetIndex);
    }
}
