#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The rule system's admin editing API checks everything it is given, as an admin window's messages come from a
///         client: non-finite numbers refused, others clamped, prototypes looked up, assets disabled rather than
///         removed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderAdminEditTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcAdminEditRule";
    private readonly EntProtoId _unit = "KsTestFcAdminEditUnit";
    private readonly EntProtoId _abstractUnit = "KsTestFcAdminEditAbstractUnit";
    private readonly EntProtoId _missing = "KsTestFcAdminEditNoSuchPrototype";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcAdminEditUnit
  components:
  - type: MobState

- type: entity
  abstract: true
  id: KsTestFcAdminEditAbstractUnit

- type: entity
  parent: BaseGameRule
  id: KsTestFcAdminEditRule
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
    assets:
    - entity: KsTestFcAdminEditUnit
    - entity: KsTestFcAdminEditUnit
";

    [Test]
    public async Task EditsAreCheckedClampedAndAssetsOnlyDisabled()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var rule = new Entity<KsFieldCommanderRuleComponent>(ruleUid, component);
            system.SetPhase((ruleUid, component, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 0);

            // Numbers.
            var cost = component.Assets[0].Cost;
            Assert.That(system.TryEditAsset(rule, 0, new KsFieldCommanderAssetEdit(Cost: float.NaN)), Is.False, "NaN is refused");
            Assert.That(system.TryEditAsset(rule, 0, new KsFieldCommanderAssetEdit(Weight: float.PositiveInfinity)), Is.False, "infinity is refused");
            Assert.That(component.Assets[0].Cost, Is.EqualTo(cost), "a refused edit changes nothing");

            Assert.That(system.TryEditAsset(rule, 0, new KsFieldCommanderAssetEdit(Cost: 1e30f, Count: -5, MinPhase: 99, MaxAlive: 1000)), Is.True);
            Assert.That(component.Assets[0].Cost, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedCost), "a huge cost is clamped");
            Assert.That(component.Assets[0].Count, Is.EqualTo(1), "a count below one is clamped");
            Assert.That(component.Assets[0].MinPhase, Is.EqualTo(component.Phases.Count - 1), "the min phase is kept to the phases there are");
            Assert.That(component.Assets[0].MaxAlive, Is.EqualTo(component.HardMaxAlive), "max alive is kept to the hard cap");
            Assert.That(system.TryEditAsset(rule, 99, new KsFieldCommanderAssetEdit(Cost: 1f)), Is.False, "an unknown asset is refused");

            // Prototypes.
            Assert.That(system.TryEditAsset(rule, 1, new KsFieldCommanderAssetEdit(Entity: _missing)), Is.False, "an unknown prototype is refused");
            Assert.That(system.TryEditAsset(rule, 1, new KsFieldCommanderAssetEdit(Entity: _abstractUnit)), Is.False, "an abstract prototype is refused");
            Assert.That(system.TryAddAsset(rule, new KsFieldCommanderAssetEdit(Cost: 1f)), Is.EqualTo(-1), "an added asset has to send something");

            var count = component.Assets.Count;
            Assert.That(system.TryAddAsset(rule, new KsFieldCommanderAssetEdit(Entity: _unit, Cost: 2f)), Is.EqualTo(count), "an asset is added at the end");
            Assert.That(component.Assets[count].Cost, Is.EqualTo(2f));

            // Disabling: never bought or queued again, but still there.
            Assert.That(system.TrySetAssetDisabled(rule, 1, true), Is.True);
            Assert.That(component.Assets, Has.Count.EqualTo(count + 1), "a disabled asset is not removed");
            var purchases = new List<int>();
            for (var i = 0; i < 30; i++)
                system.BuyAssets(component, 1f, 10, new Dictionary<int, int>(), purchases);
            Assert.That(purchases, Does.Not.Contain(1), "a disabled asset is never bought");
            Assert.That(system.TryQueueWave(rule, new List<(int, int)> { (1, 1) }, null), Is.False, "nor queued");

            // Phases.
            Assert.That(system.TryEditPhase(rule, 0, start: null, waveInterval: null, budget: float.NegativeInfinity), Is.False);
            Assert.That(system.TryEditPhase(rule, 0, start: component.Phases[1].Start + System.TimeSpan.FromSeconds(1), waveInterval: null, budget: null), Is.False,
                "a start past the next phase's is refused");
            Assert.That(system.TryEditPhase(rule, 1, start: null, waveInterval: System.TimeSpan.FromDays(30), budget: -3f), Is.True);
            Assert.That(component.Phases[1].WaveInterval, Is.EqualTo(KsFieldCommanderRuleSystem.MaxEditedTime), "a huge interval is clamped");
            Assert.That(component.Phases[1].Budget, Is.Zero, "a negative budget is clamped");

            // Status.
            system.SetPaused(rule, true);
            var status = system.GetAdminStatus((ruleUid, component, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)));
            Assert.That(status.Paused, Is.True);
            Assert.That(status.PhaseIndex, Is.EqualTo(0));
            Assert.That(status.PhaseName, Is.EqualTo(component.Phases[0].Name));
        });
    }
}
