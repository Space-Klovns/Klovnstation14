#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.GhostRespawn;
using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using ClientGhostRespawnSystem = Content.Client._KS14.GhostRespawn.GhostRespawnSystem;
using ServerGhostRespawnSystem = Content.Server._KS14.GhostRespawn.GhostRespawnSystem;

namespace Content.IntegrationTests.Tests._KS14.GhostRespawn;

/// <summary>
///     Ghost respawning: dying starts a respawn timer, which the client is told about, and which only the first death
///         since the last respawn sets.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerGhostRespawnSystem))]
public sealed class KsGhostRespawnTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Long enough that a respawn never becomes allowed during a test.
    private const float CooldownSeconds = 300f;

    private readonly EntProtoId _mob = "KsGhostRespawnTestMob";
    private readonly EntProtoId _overrideRule = "KsGhostRespawnTestOverrideRule";
    private readonly EntProtoId _cooldownOnlyRule = "KsGhostRespawnTestCooldownOnlyRule";

    // Any message that exists; the gate test only needs a reason to pass along.
    private readonly LocId _denyReason = "ks-ghost-respawn-popup-ghostingblocked";

    // The test rules' own values, distinct from each other and from the cvars the tests set.
    private static readonly TimeSpan RuleCooldown = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan RulePenalty = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CooldownOnlyRuleCooldown = TimeSpan.FromSeconds(40);
    private const float CvarPenaltySeconds = 5f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsGhostRespawnTestMob
  components:
  - type: MindContainer
  - type: MobState

- type: entity
  parent: BaseGameRule
  id: KsGhostRespawnTestOverrideRule
  components:
  - type: KsGhostRespawnOverrideRule
    enabled: true
    cooldown: 100
    penalty: 20

- type: entity
  parent: BaseGameRule
  id: KsGhostRespawnTestCooldownOnlyRule
  components:
  - type: KsGhostRespawnOverrideRule
    cooldown: 40
";

    private ServerGhostRespawnSystem ServerRespawn => SEntMan.System<ServerGhostRespawnSystem>();
    private ClientGhostRespawnSystem ClientRespawn => CEntMan.System<ClientGhostRespawnSystem>();

    private async Task<EntityUid> SpawnMobAsync()
    {
        EntityUid uid = default;
        await Server.WaitPost(() =>
        {
            uid = SEntMan.SpawnEntity(_mob, MapCoordinates.Nullspace);
            SEntMan.System<MobStateSystem>().ChangeMobState(uid, MobState.Alive);
        });

        return uid;
    }

    /// <summary>
    ///     Puts the test player in <paramref name="uid"/>, under a mind of their own.
    /// </summary>
    private async Task PossessAsync(EntityUid uid, EntityUid? mindUid = null)
    {
        await Server.WaitPost(() =>
        {
            var mindSystem = SEntMan.System<SharedMindSystem>();
            mindUid ??= mindSystem.CreateMind(ServerSession!.UserId);
            mindSystem.TransferTo(mindUid.Value, uid);
        });

        await Pair.RunTicksSync(3);
    }

    private async Task SetMobStateAsync(EntityUid uid, MobState state)
    {
        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(uid, state));
        await Pair.RunTicksSync(3);
    }

    [Test]
    public async Task TheServersRespawnTimeReachesTheClient()
    {
        var time = TimeSpan.FromMinutes(5);

        await Server.WaitPost(() => SEntMan.EntityNetManager.SendSystemNetworkMessage(new GhostRespawnTimeMessage(time), ServerSession!.Channel));
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalRespawnTime, Is.EqualTo(time)));

        await Server.WaitPost(() => SEntMan.EntityNetManager.SendSystemNetworkMessage(new GhostRespawnTimeMessage(null), ServerSession!.Channel));
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalRespawnTime, Is.Null, "and so does clearing it"));
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    public async Task DyingStartsTheTimerAndBeingRevivedCancelsIt()
    {
        var mob = await SpawnMobAsync();
        await PossessAsync(mob);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.False, "precondition: alive"));

        TimeSpan deathTime = default;
        await Server.WaitPost(() => deathTime = SGameTiming.CurTime);
        await SetMobStateAsync(mob, MobState.Dead);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.True, "dead, so pending"));
        await Client.WaitAssertion(() =>
            Assert.That(ClientRespawn.LocalRespawnTime, Is.EqualTo(deathTime + TimeSpan.FromSeconds(CooldownSeconds)).Within(TimeSpan.FromSeconds(1)),
                "the client is told when, a cooldown after the death"));

        await SetMobStateAsync(mob, MobState.Alive);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.False, "revived, so no longer pending"));
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalRespawnTime, Is.Null));
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    public async Task OnlyTheFirstDeathSetsTheTimer()
    {
        var firstBody = await SpawnMobAsync();
        await PossessAsync(firstBody);
        await SetMobStateAsync(firstBody, MobState.Dead);

        TimeSpan? firstRespawnTime = null;
        EntityUid mindUid = default;
        await Client.WaitPost(() => firstRespawnTime = ClientRespawn.LocalRespawnTime);
        await Server.WaitPost(() => Assert.That(SEntMan.System<SharedMindSystem>().TryGetMind(ServerSession!, out mindUid, out _), Is.True));
        Assert.That(firstRespawnTime, Is.Not.Null, "precondition: the first death started the timer");

        // Into another body, e.g. a ghost role, and die there too, later.
        var secondBody = await SpawnMobAsync();
        await PossessAsync(secondBody, mindUid);
        await Pair.RunTicksSync(10);
        await SetMobStateAsync(secondBody, MobState.Dead);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.True);
            Assert.That(ServerRespawn.IsEntityTrackingThisSessionsDeath(firstBody, ServerSession!), Is.True, "the original body still counts");
        });
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalRespawnTime, Is.EqualTo(firstRespawnTime), "a later death does not push the timer back"));
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    public async Task AskingToRespawnBeforeTheTimerIsUpDoesNothing()
    {
        var mob = await SpawnMobAsync();
        await PossessAsync(mob);
        await SetMobStateAsync(mob, MobState.Dead);

        await Client.WaitPost(() => ClientRespawn.RequestRespawn());
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.True, "still waiting");
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(mob), "and not respawned");
        });
    }

    private async Task<EntityUid> StartRuleAsync(EntProtoId rule)
    {
        EntityUid ruleUid = default;
        await Server.WaitPost(() => Assert.That(SEntMan.System<GameTicker>().StartGameRule(rule, out ruleUid), Is.True));
        await Pair.RunTicksSync(3);
        return ruleUid;
    }

    private async Task EndRuleAsync(EntityUid ruleUid)
    {
        await Server.WaitPost(() => SEntMan.System<GameTicker>().EndGameRule(ruleUid));
        await Pair.RunTicksSync(3);
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnPenaltySeconds), CvarPenaltySeconds)]
    public async Task AnOverrideRuleReplacesTheCvarsWhileActive()
    {
        GhostRespawnSettings cvarSettings = default;
        await Server.WaitPost(() => cvarSettings = new GhostRespawnSettings(
            false,
            ServerRespawn.Settings.AlwaysStartTimer,
            TimeSpan.FromSeconds(CooldownSeconds),
            TimeSpan.FromSeconds(CvarPenaltySeconds)));
        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings, Is.EqualTo(cvarSettings), "precondition: the cvars"));
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalEnabled, Is.False, "precondition: off on the client"));

        var ruleUid = await StartRuleAsync(_overrideRule);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings,
            Is.EqualTo(cvarSettings with { Enabled = true, Cooldown = RuleCooldown, Penalty = RulePenalty }),
            "the rule's values replace the cvars' (not added to them), and what it leaves out stays"));
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalEnabled, Is.True, "the client is told respawning is forced on"));

        await EndRuleAsync(ruleUid);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings, Is.EqualTo(cvarSettings), "ending the rule brings the cvars back"));
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalEnabled, Is.False, "and the client follows the cvar again"));

        // A round restart deletes rules without ending them.
        ruleUid = await StartRuleAsync(_overrideRule);
        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings.Cooldown, Is.EqualTo(RuleCooldown), "precondition: started again"));
        await Server.WaitPost(() => SEntMan.DeleteEntity(ruleUid));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings, Is.EqualTo(cvarSettings), "deleting the rule brings the cvars back too"));
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnPenaltySeconds), CvarPenaltySeconds)]
    public async Task TheLatestStartedOverrideRuleWinsEachFieldItSets()
    {
        var firstUid = await StartRuleAsync(_overrideRule);
        var secondUid = await StartRuleAsync(_cooldownOnlyRule);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerRespawn.Settings.Cooldown, Is.EqualTo(CooldownOnlyRuleCooldown), "the later rule's cooldown");
            Assert.That(ServerRespawn.Settings.Penalty, Is.EqualTo(RulePenalty), "the earlier rule's penalty, which the later leaves out");
        });

        await EndRuleAsync(secondUid);
        await Server.WaitAssertion(() => Assert.That(ServerRespawn.Settings.Cooldown, Is.EqualTo(RuleCooldown), "back to the earlier rule's"));

        await EndRuleAsync(firstUid);
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), CooldownSeconds)]
    public async Task StartingAnOverrideRuleMovesAPendingTimer()
    {
        var mob = await SpawnMobAsync();
        await PossessAsync(mob);
        await SetMobStateAsync(mob, MobState.Dead);

        TimeSpan? before = null;
        await Server.WaitPost(() => before = ServerRespawn.GetRespawnTime(ServerSession!));
        Assert.That(before, Is.Not.Null, "precondition: pending");

        var ruleUid = await StartRuleAsync(_overrideRule);

        var expected = before!.Value + RuleCooldown - TimeSpan.FromSeconds(CooldownSeconds);
        await Server.WaitAssertion(() => Assert.That(ServerRespawn.GetRespawnTime(ServerSession!), Is.EqualTo(expected),
            "the timer moves by the difference between the cooldowns"));
        await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalRespawnTime, Is.EqualTo(expected), "and the client is told"));

        await EndRuleAsync(ruleUid);
        await Server.WaitAssertion(() => Assert.That(ServerRespawn.GetRespawnTime(ServerSession!), Is.EqualTo(before), "and back when it ends"));
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), 0f)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnPenaltySeconds), CvarPenaltySeconds)]
    public async Task ADeniedRespawnIsRefusedAndCostsNothing()
    {
        var probe = SEntMan.System<KsGhostRespawnGateProbeSystem>();
        await Server.WaitPost(() => probe.DenyReason = _denyReason);

        try
        {
            var mob = await SpawnMobAsync();
            await PossessAsync(mob);
            await SetMobStateAsync(mob, MobState.Dead);

            await Client.WaitAssertion(() =>
            {
                Assert.That(ClientRespawn.LocalRespawnTime, Is.Not.Null, "precondition: pending");
                Assert.That(ClientRespawn.LocalGate?.Reason, Is.EqualTo(_denyReason), "the client is told what holds it back");
                Assert.That(ClientRespawn.LocalGate?.OpensAt, Is.Null, "with no time, as the gate gave none");
            });

            await Client.WaitPost(() => ClientRespawn.RequestRespawn());
            await Pair.RunTicksSync(3);

            await Server.WaitAssertion(() =>
            {
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(mob), "the timer is up, but the gate refused the respawn");
                Assert.That(ServerRespawn.IsSessionPendingRespawn(ServerSession!), Is.True, "still waiting");
                Assert.That(ServerRespawn.GetPenalty(ServerSession!), Is.EqualTo(TimeSpan.Zero), "and no penalty was charged for it");
            });

            await Server.WaitPost(() =>
            {
                probe.DenyReason = null;
                ServerRespawn.RefreshGates();
            });
            await Pair.RunTicksSync(3);
            await Client.WaitAssertion(() => Assert.That(ClientRespawn.LocalGate, Is.Null, "the client hears the gate opened"));

            await Client.WaitPost(() => ClientRespawn.RequestRespawn());
            await Pair.RunTicksSync(3);

            await Server.WaitAssertion(() =>
            {
                // (Not "no longer pending": leaving a dead body without a ghost between starts a new timer at once.)
                Assert.That(ServerSession!.AttachedEntity, Is.Not.EqualTo(mob), "control: respawned once allowed");
                Assert.That(ServerRespawn.GetPenalty(ServerSession!), Is.EqualTo(TimeSpan.FromSeconds(CvarPenaltySeconds)), "control: a real respawn is charged");
            });
        }
        finally
        {
            await Server.WaitPost(() => probe.DenyReason = null);
        }
    }
}
