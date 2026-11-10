#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
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

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsGhostRespawnTestMob
  components:
  - type: MindContainer
  - type: MobState
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
}
