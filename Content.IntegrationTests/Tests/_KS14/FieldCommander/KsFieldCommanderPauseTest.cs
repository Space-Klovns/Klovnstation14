#nullable enable
using System.Linq;
using Content.Client._KS14.FieldCommander;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.FieldCommander;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Pausing stops the commander's clock: nothing it scheduled comes due, the uplink's countdown holds, the HUD says
///         so, and on resuming every timer has moved on by exactly the pause.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderPauseTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestFcPauseRule";
    private readonly EntProtoId _defender = "KsTestFcPauseDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFcPauseDefender
  components:
  - type: MobState
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFcPauseUnit
  components:
  - type: MobState

- type: entity
  parent: KsFieldCommandUplink
  id: KsTestFcPauseUplink
  components:
  - type: TimedDespawn
    lifetime: 3

- type: entity
  parent: BaseGameRule
  id: KsTestFcPauseRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 14
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: KsTestFcPauseUplink
    briefingAnnouncement: null
    endlessVote: false
    phases:
    - name: ks-fieldcommander-phase-probing
      start: 0
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 1
    - name: ks-fieldcommander-phase-siege
      start: 2
    assets:
    - entity: KsTestFcPauseUnit
";

    private readonly record struct Timers(
        TimeSpan NextWave,
        TimeSpan NextOrder,
        TimeSpan Bombardment,
        TimeSpan Intel,
        TimeSpan UnitProgress,
        TimeSpan? NudgeHeld,
        TimeSpan? Damaged,
        TimeSpan TimeOffset);

    private static Timers Read(KsFieldCommanderRuleComponent component, KsFieldCommanderUnitComponent unitComponent)
    {
        return new Timers(component.NextWaveTime,
            component.NextOrderTime,
            component.UplinkBombardmentTime!.Value,
            component.PendingIntel.Min(intel => intel.At),
            unitComponent.LastProgressTime,
            unitComponent.NudgeHeldSince,
            unitComponent.LastDamagedTime,
            component.TimeOffset);
    }

    [Test]
    public async Task PauseHoldsTheCommandersClockAndShiftsEveryTimer()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var consoleHost = server.ResolveDependency<IConsoleHost>();
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 20);
            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            var body = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, body);
            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, _rule).CommanderUid;
        });

        await Pair.RunTicksSync(5);

        Timers before = default;
        EntityUid unitUid = default;
        EntityUid uplinkUid = default;
        var pausedAt = TimeSpan.Zero;

        await server.WaitPost(() =>
        {
            var system = server.System<KsFieldCommanderRuleSystem>();
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(component.PhaseIndex, Is.EqualTo(0));

            system.ForceWave((ruleUid, component));
            system.ForceUplink((ruleUid, component));
            Assert.That(component.UplinkUid, Is.Not.Null, "the uplink should have landed");
            uplinkUid = component.UplinkUid!.Value;
            Assert.That(component.PendingIntel, Is.Not.Empty, "the wave and uplink should have queued intel");

            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out unitUid, out var unitComponent), Is.True, "the wave should have sent a unit");

            // A nudge held back and a hit just taken, staged as the stuck checks and the damage handler leave them:
            //     both are the rule system's to write, so set here by reflection.
            var now = server.Timing.CurTime;
            typeof(KsFieldCommanderUnitComponent).GetField(nameof(KsFieldCommanderUnitComponent.NudgeHeldSince))!.SetValue(unitComponent, (TimeSpan?)now);
            typeof(KsFieldCommanderUnitComponent).GetField(nameof(KsFieldCommanderUnitComponent.LastDamagedTime))!.SetValue(unitComponent, (TimeSpan?)now);

            before = Read(component, unitComponent!);
            pausedAt = server.Timing.CurTime;
            consoleHost.ExecuteCommand("ks_fcpause on");
            Assert.That(component.PausedAt, Is.EqualTo(pausedAt));
        });

        // Past the uplink's countdown, the intel's delay and the next phase's start.
        await RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(server.EntMan.Deleted(uplinkUid), Is.False, "the uplink's countdown should hold while paused");
            Assert.That(component.PhaseIndex, Is.EqualTo(0), "the timeline should stand still while paused");
            Assert.That(component.PendingIntel, Is.Not.Empty, "intel should wait while paused");
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(testMap.MapUid).Paused, Is.True,
                "the HUD's wave status should say the commander is paused");
        });

        await client.WaitAssertion(() =>
        {
            var expected = client.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>()
                .GetString("ks-fieldcommander-wave-status-paused", ("wave", 1));
            Assert.That(client.System<KsFieldCommanderPointsSystem>().Control?.WaveText, Is.EqualTo(expected),
                "the HUD should show the commander paused");
        });

        await server.WaitPost(() =>
        {
            var component = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var delta = server.Timing.CurTime - pausedAt;
            consoleHost.ExecuteCommand("ks_fcpause off");

            var after = Read(component, server.EntMan.GetComponent<KsFieldCommanderUnitComponent>(unitUid));
            Assert.That(component.PausedAt, Is.Null);
            Assert.That(after.NextWave, Is.EqualTo(before.NextWave + delta), "the next wave");
            Assert.That(after.NextOrder, Is.EqualTo(before.NextOrder + delta), "the next orders");
            Assert.That(after.Bombardment, Is.EqualTo(before.Bombardment + delta), "the uplink's bombardment");
            Assert.That(after.Intel, Is.EqualTo(before.Intel + delta), "pending intel");
            Assert.That(after.UnitProgress, Is.EqualTo(before.UnitProgress + delta), "a unit's stuck timer");
            Assert.That(after.NudgeHeld, Is.EqualTo(before.NudgeHeld + delta),
                "a held-back nudge's wait should not count the pause, or it is made in a defender's sight on resuming");
            Assert.That(after.Damaged, Is.EqualTo(before.Damaged + delta), "a unit's damage grace");
            Assert.That(after.TimeOffset, Is.EqualTo(before.TimeOffset - delta), "the timeline");
            Assert.That(server.EntMan.HasComponent<Robust.Shared.Spawners.TimedDespawnComponent>(uplinkUid), Is.True,
                "the uplink's countdown should be back");
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderWaveStatusComponent>(testMap.MapUid).Paused, Is.False);
        });

        // Resumed, the uplink's countdown runs out as it would have.
        await RunSeconds(4f);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.Deleted(uplinkUid), Is.True, "the uplink's countdown should run again once resumed"));
    }
}
