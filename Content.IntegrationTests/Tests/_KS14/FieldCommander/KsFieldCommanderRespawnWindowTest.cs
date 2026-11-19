#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.FieldCommander;
using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using ClientGhostRespawnSystem = Content.Client._KS14.GhostRespawn.GhostRespawnSystem;
using ServerGhostRespawnSystem = Content.Server._KS14.GhostRespawn.GhostRespawnSystem;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A field commander rule with a respawn window lets dead defenders respawn only shortly before the next wave,
///         and tells their ghosts when that is. While no wave is scheduled it lets them respawn or refuses, as the
///         window is set to.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRespawnWindowComponent))]
public sealed class KsFieldCommanderRespawnWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderRespawnWindowRule";
    private readonly EntProtoId _closedCommanderRule = "KsTestFieldCommanderRespawnWindowClosedRule";
    private readonly EntProtoId _mob = "KsTestFieldCommanderRespawnWindowMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderRespawnWindowMob
  components:
  - type: MindContainer
  - type: MobState

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderRespawnWindowRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    phases:
    # The next wave well after the window.
    - name: ks-fieldcommander-phase-probing
      start: 0
      waveInterval: 1000
      firstWaveDelay: 600
    # The next wave inside it.
    - name: ks-fieldcommander-phase-pressure
      start: 1
      waveInterval: 1000
      firstWaveDelay: 10
    # The next wave just past it, so the window opens a moment later.
    - name: ks-fieldcommander-phase-siege
      start: 2
      waveInterval: 1000
      firstWaveDelay: 32
    # No waves at all.
    - name: ks-fieldcommander-phase-final
      start: 3
      waveInterval: 0
  - type: KsFieldCommanderRespawnWindow
    window: 30

- type: entity
  parent: KsTestFieldCommanderRespawnWindowRule
  id: KsTestFieldCommanderRespawnWindowClosedRule
  components:
  - type: KsFieldCommanderRespawnWindow
    openWhileNoWaveScheduled: false
";

    /// <summary>
    ///     Checks the dead defender's respawn with no wave scheduled: allowed if <paramref name="openWithoutWave"/>,
    ///         else refused with no time to give; on the server, and in what its ghost was told.
    /// </summary>
    private async Task AssertNoWaveScheduled(ServerGhostRespawnSystem serverRespawn, ClientGhostRespawnSystem clientRespawn, bool openWithoutWave, string when)
    {
        await Server.WaitAssertion(() =>
        {
            var gate = serverRespawn.GetGate(ServerSession!);
            if (openWithoutWave)
            {
                Assert.That(gate, Is.Null, $"{when}: no wave scheduled, and the window allows that");
                return;
            }

            Assert.That(gate, Is.Not.Null, $"{when}: no wave scheduled, so refused");
            Assert.That(gate!.Value.OpensAt, Is.Null, $"{when}: with no time to give");
        });

        await Client.WaitAssertion(() =>
        {
            if (openWithoutWave)
                Assert.That(clientRespawn.LocalGate, Is.Null, $"{when}: and the ghost is told it may respawn");
            else
                Assert.That(clientRespawn.LocalGate is { OpensAt: null }, Is.True, $"{when}: and the ghost is told it is refused, with no time to give");
        });
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), 0f)]
    public async Task RespawnIsAllowedOnlyInTheWindowBeforeTheNextWave([Values] bool openWithoutWave)
    {
        var commanderRule = openWithoutWave ? _commanderRule : _closedCommanderRule;
        var serverRespawn = SEntMan.System<ServerGhostRespawnSystem>();
        var clientRespawn = CEntMan.System<ClientGhostRespawnSystem>();
        var ruleSystem = SEntMan.System<KsFieldCommanderRuleSystem>();
        var ticker = SEntMan.System<GameTicker>();

        // A dead defender, waiting to respawn.
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(_mob, MapCoordinates.Nullspace);
            var mobStateSystem = SEntMan.System<MobStateSystem>();
            mobStateSystem.ChangeMobState(mob, MobState.Alive);
            var mindSystem = SEntMan.System<SharedMindSystem>();
            mindSystem.TransferTo(mindSystem.CreateMind(ServerSession!.UserId), mob);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(serverRespawn.GetGate(ServerSession!), Is.Null, "precondition: nothing holds it back without the rule"));

        EntityUid ruleUid = default;
        await Server.WaitPost(() => Assert.That(ticker.StartGameRule(commanderRule, out ruleUid), Is.True));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(
            SEntMan.GetComponent<KsFieldCommanderRespawnWindowComponent>(ruleUid).OpenWhileNoWaveScheduled,
            Is.EqualTo(openWithoutWave),
            "precondition: the rule's window is set as this case needs"));
        await AssertNoWaveScheduled(serverRespawn, clientRespawn, openWithoutWave, "before the first wave");

        TimeSpan windowOpens = default;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var windowComponent = SEntMan.GetComponent<KsFieldCommanderRespawnWindowComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, SEntMan.GetComponent<GameRuleComponent>(ruleUid)), 0);
            windowOpens = ruleComponent.NextWaveTime - windowComponent.Window;
            Assert.That(SGameTiming.CurTime, Is.LessThan(windowOpens), "precondition: the next wave is past the window");
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(serverRespawn.GetGate(ServerSession!)?.OpensAt, Is.EqualTo(windowOpens),
            "refused until the window before the next wave opens"));
        await Client.WaitAssertion(() => Assert.That(clientRespawn.LocalGate?.OpensAt, Is.EqualTo(windowOpens), "and the ghost is told when"));

        await Client.WaitPost(() => clientRespawn.RequestRespawn());
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() => Assert.That(serverRespawn.IsSessionPendingRespawn(ServerSession!), Is.True, "so asking does nothing"));

        // Into a phase with no waves, and back to one with them: the ghost is told each time.
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, SEntMan.GetComponent<GameRuleComponent>(ruleUid)), 3);
            Assert.That(ruleComponent.NextWaveTime, Is.EqualTo(TimeSpan.MaxValue), "precondition: no wave scheduled");
        });
        await Pair.RunTicksSync(3);
        await AssertNoWaveScheduled(serverRespawn, clientRespawn, openWithoutWave, "in a phase with no waves");

        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var windowComponent = SEntMan.GetComponent<KsFieldCommanderRespawnWindowComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, SEntMan.GetComponent<GameRuleComponent>(ruleUid)), 0);
            windowOpens = ruleComponent.NextWaveTime - windowComponent.Window;
        });
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() => Assert.That(clientRespawn.LocalGate?.OpensAt, Is.EqualTo(windowOpens),
            "a wave scheduled again: the ghost is told when the window before it opens"));

        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var windowComponent = SEntMan.GetComponent<KsFieldCommanderRespawnWindowComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, SEntMan.GetComponent<GameRuleComponent>(ruleUid)), 1);
            Assert.That(SGameTiming.CurTime, Is.GreaterThanOrEqualTo(ruleComponent.NextWaveTime - windowComponent.Window),
                "precondition: the next wave is inside the window");
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(serverRespawn.GetGate(ServerSession!), Is.Null, "allowed inside the window"));
        await Client.WaitAssertion(() => Assert.That(clientRespawn.LocalGate, Is.Null, "and the ghost is told it opened"));

        // Closed again, opening by itself shortly: nothing calls for a refresh then, the opening time is enough.
        TimeSpan opensIn = default;
        await Server.WaitPost(() =>
        {
            var ruleComponent = SEntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            var windowComponent = SEntMan.GetComponent<KsFieldCommanderRespawnWindowComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, SEntMan.GetComponent<GameRuleComponent>(ruleUid)), 2);
            opensIn = ruleComponent.NextWaveTime - windowComponent.Window - SGameTiming.CurTime;
            Assert.That(opensIn, Is.GreaterThan(TimeSpan.Zero), "precondition: the next wave is just past the window");
        });
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() => Assert.That(clientRespawn.LocalGate, Is.Not.Null, "precondition: closed again"));

        var tickRate = (float)SGameTiming.TickRate;
        await Pair.RunTicksSync((int)MathF.Ceiling(((float)opensIn.TotalSeconds + 1f) * tickRate));
        await Client.WaitAssertion(() => Assert.That(clientRespawn.LocalGate, Is.Null, "the ghost is told when the window opens with time"));

        await Server.WaitPost(() => ticker.EndGameRule(ruleUid));
        await Pair.RunTicksSync(3);
    }
}
