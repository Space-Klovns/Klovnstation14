using Content.Client._KS14.GhostRespawn;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.GhostRespawn;

public sealed class KsGhostRespawnTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly IEntityNetworkManager _serverEntityNetworkManager = default!;
    [SidedDependency(Side.Client)] private readonly IEntityNetworkManager _clientEntityNetworkManager = default!;

    [SidedDependency(Side.Server)] private readonly SharedMindSystem _serverMindSystem = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _serverMobStateSystem = default!;

    [SidedDependency(Side.Server)] private readonly Server._KS14.GhostRespawn.GhostRespawnSystem _serverGhostRespawnSystem = default!;
    [SidedDependency(Side.Client)] private readonly GhostRespawnSystem _clientGhostRespawnSystem = default!;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsGhostRespawnTestEntity
  components:
  - type: MindContainer
  - type: Body
  - type: MobState
";

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    public async Task TestProperStateReceived()
    {
        var pair = Pair;

        Assert.That(_clientGhostRespawnSystem.LocalRespawnTime, Is.Null, "GhostRespawnSystem.LocalRespawnTime was not the default value (null)");

        // long enough to make sure respawn doesnt get allowed anytime soon
        var testTimeSpan = TimeSpan.FromSeconds(300d);
        BroadcastState(testTimeSpan);
        Assert.That(_clientGhostRespawnSystem.LocalRespawnTime, Is.Not.Null, "GhostRespawnSystem.LocalRespawnTime was null when it should not have been");

        BroadcastState(null);
        Assert.That(_clientGhostRespawnSystem.LocalRespawnTime, Is.Null, "GhostRespawnSystem.LocalRespawnTime was not null when it should've been");

        async void BroadcastState(TimeSpan? broadcastedTime)
        {
            using (Assert.EnterMultipleScope())
            {
                // any non-null/non-zero value
                var message = new GhostRespawnTimeMessage(broadcastedTime);
                _serverEntityNetworkManager.SendSystemNetworkMessage(message);
            }

            await pair.RunTicksSync(3);
        }
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), 0f)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnPenaltySeconds), 0f)]
    public async Task TestRespawningDeathReaction()
    {
        var pair = Pair;

        var entityManager = Server.ResolveDependency<IServerEntityManager>();
        var prototypeManager = Server.ResolveDependency<IPrototypeManager>();

        EntityUid uid = default!;
        MobStateComponent mobStateComponent = default!;
        MindContainerComponent mindContainerComponent = default!;
        EntityUid mindUid = default!;

        await Server.WaitAssertion(() =>
        {
            uid = entityManager.SpawnEntity("KsGhostRespawnTestEntity", MapCoordinates.Nullspace);

            mobStateComponent = entityManager.EnsureComponent<MobStateComponent>(uid);
            _serverMobStateSystem.ChangeMobState(uid, MobState.Alive, component: mobStateComponent);

            mindContainerComponent = entityManager.EnsureComponent<MindContainerComponent>(uid);
            mindUid = _serverMindSystem.CreateMind(Client.User);

            _serverMindSystem.TransferTo(mindUid, uid);
        });

        await pair.RunTicksSync(5);

        // Make sure the player somehow isnt already pending a respawn
        Assert.That(!_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is pending even though it should not be");

        // Kill player and make sure they CAN respawn
        await KillAndAssertRespawnability();

        // Test if the client can respawn in original body
        await RequestRespawnAndErrorIfFailed();

        // Kill player again and make sure they CAN respawn
        await KillAndAssertRespawnability();

        async Task RequestRespawnAndErrorIfFailed()
        {
            // Client asks to respawn
            await Client.WaitAssertion(() =>
            {
                var message = new GhostRespawnActMessage();
                _clientEntityNetworkManager.SendSystemNetworkMessage(message);
            });

            await pair.RunTicksSync(3);

            // Make sure client can not respawn (after respawning)
            using (Assert.EnterMultipleScope())
            {
                // Make sure the player is NOT pending a respawn
                Assert.That(!_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is pending even though it should NOT be");

                // Make sure respawn time matches this (you are NOT pending a respawn if curtime is at or more than than the respawn time)
                Assert.That(CGameTiming.CurTime, Is.AtLeast(_clientGhostRespawnSystem.LocalRespawnTime), "Respawn time was less than CurTime, implying that respawn is pending, even though it should NOT be");
            }
        }

        async Task KillAndAssertRespawnability()
        {
            // Kill player
            await Server.WaitAssertion(() =>
            {
                _serverMobStateSystem.ChangeMobState(uid, MobState.Dead, component: mobStateComponent);
            });

            await pair.RunTicksSync(5);
            await AssertRespawnibility();
        }

        async Task AssertRespawnibility()
        {
            // Make sure they can respawn now that they are dead
            using (Assert.EnterMultipleScope())
            {
                // Make sure the player IS now pending a respawn
                Assert.That(_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is not pending even though it should be");

                // Make sure respawn time matches this (you are pending a respawn if curtime is less than the respawn time)
                Assert.That(CGameTiming.CurTime, Is.LessThan(_clientGhostRespawnSystem.LocalRespawnTime), "Respawn time was at or after CurTime, implying that respawn is not pending, even though it should be");
            }
        }
    }

    [Test]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnEnabled), true)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnAlwaysStartTimer), false)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnCooldownSeconds), 0f)]
    [EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.GhostRespawnPenaltySeconds), 0f)]
    public async Task TestRespawningAfterSwitchingMobs()
    {
        var pair = Pair;

        var entityManager = Server.ResolveDependency<IServerEntityManager>();
        var prototypeManager = Server.ResolveDependency<IPrototypeManager>();

        EntityUid uid = default!;
        MobStateComponent mobStateComponent = default!;
        MindContainerComponent mindContainerComponent = default!;
        EntityUid mindUid = default!;

        await Server.WaitAssertion(() =>
        {
            uid = entityManager.SpawnEntity("KsGhostRespawnTestEntity", MapCoordinates.Nullspace);

            mobStateComponent = entityManager.EnsureComponent<MobStateComponent>(uid);
            _serverMobStateSystem.ChangeMobState(uid, MobState.Alive, component: mobStateComponent);

            mindContainerComponent = entityManager.EnsureComponent<MindContainerComponent>(uid);
            mindUid = _serverMindSystem.CreateMind(Client.User);

            _serverMindSystem.TransferTo(mindUid, uid);
        });

        await pair.RunTicksSync(5);

        // Make sure the player somehow isnt already pending a respawn
        Assert.That(!_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is pending even though it should not be");

        // Kill body the player is in now and coincidentally also test if they can respawn
        await KillAndAssertRespawnability();

        // Transfer client to a new body while he can respawn
        var oldUid = uid;
        await Server.WaitAssertion(() =>
        {
            uid = entityManager.SpawnEntity("KsGhostRespawnTestEntity", MapCoordinates.Nullspace);

            mobStateComponent = entityManager.EnsureComponent<MobStateComponent>(uid);
            _serverMobStateSystem.ChangeMobState(uid, MobState.Alive, component: mobStateComponent);

            mindContainerComponent = entityManager.EnsureComponent<MindContainerComponent>(uid);
            _serverMindSystem.TransferTo(mindUid, uid);
        });

        // Test if the client can still respawn after switching bodies and dying in that body
        await AssertRespawnibility();

        // Kill new body that the player is now in
        await Server.WaitAssertion(() =>
        {
            _serverMobStateSystem.ChangeMobState(uid, MobState.Dead, component: mobStateComponent);
        });

        // Respawn timer shouldn't be affected by anything other revival/respawn after death, not even swapping to a new body and dying in it
        Assert.That(_serverGhostRespawnSystem.IsEntityTrackingThisSessionsDeath(oldUid, Client.Session), "Old entity is no longer tracking death of session after it switched to a new entity, even though it should be");

        await RequestRespawnAndErrorIfFailed();

        // Profit

        async Task RequestRespawnAndErrorIfFailed()
        {
            // Client asks to respawn
            await Client.WaitAssertion(() =>
            {
                var message = new GhostRespawnActMessage();
                _clientEntityNetworkManager.SendSystemNetworkMessage(message);
            });

            await pair.RunTicksSync(3);

            // Make sure client can not respawn (after respawning)
            using (Assert.EnterMultipleScope())
            {
                // Make sure the player is NOT pending a respawn
                Assert.That(!_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is pending even though it should NOT be");

                // Make sure respawn time matches this (you are NOT pending a respawn if curtime is at or more than than the respawn time)
                Assert.That(CGameTiming.CurTime, Is.AtLeast(_clientGhostRespawnSystem.LocalRespawnTime), "Respawn time was less than CurTime, implying that respawn is pending, even though it should NOT be");
            }
        }

        async Task KillAndAssertRespawnability()
        {
            // Kill player
            await Server.WaitAssertion(() =>
            {
                _serverMobStateSystem.ChangeMobState(uid, MobState.Dead, component: mobStateComponent);
            });

            await pair.RunTicksSync(5);
            await AssertRespawnibility();
        }

        async Task AssertRespawnibility()
        {
            // Make sure they can respawn now that they are dead
            using (Assert.EnterMultipleScope())
            {
                // Make sure the player IS now pending a respawn
                Assert.That(_serverGhostRespawnSystem.IsSessionPendingRespawn(Client.Session), "Respawn is not pending even though it should be");

                // Make sure respawn time matches this (you are pending a respawn if curtime is less than the respawn time)
                Assert.That(CGameTiming.CurTime, Is.LessThan(_clientGhostRespawnSystem.LocalRespawnTime), "Respawn time was at or after CurTime, implying that respawn is not pending, even though it should be");
            }
        }
    }
}
