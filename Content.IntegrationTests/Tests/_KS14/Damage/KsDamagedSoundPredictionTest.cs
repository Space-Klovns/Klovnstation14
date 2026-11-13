#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Damage.DamagedSound;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     The player who sets off a <see cref="KsDamagedSoundComponent"/> hears it once, from their own client's prediction:
///         the server's copy leaves them out. Two copies, or a networked one only, would mean the prediction is off.
/// </summary>
[TestOf(typeof(KsDamagedSoundSystem))]
public sealed class KsDamagedSoundPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and a fist, and damageable for contact damage

    [Test]
    public async Task PlayerHearsTheirOwnMeleeHitOnce()
    {
        await SpawnTarget(KsDamagedSoundTest.TestWire);
        await SetCombatMode(true);
        await RunSeconds(1f); // any attack cooldown

        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new LightAttackEvent(Target, Player, TargetCoords)));

        var clientHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var serverHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        int? clientFirstTick = null, serverFirstTick = null;
        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() => KsDamagedSoundTest.CollectSounds(CEntMan, MapId, KsDamagedSoundTest.TestSoundPath, clientHeard));
            await Server.WaitPost(() => KsDamagedSoundTest.CollectSounds(SEntMan, MapId, KsDamagedSoundTest.TestSoundPath, serverHeard));

            if (clientHeard.Count > 0)
                clientFirstTick ??= tick;
            if (serverHeard.Count > 0)
                serverFirstTick ??= tick;
        }

        Assert.Multiple(() =>
        {
            Assert.That(serverHeard, Has.Count.EqualTo(1), "the server should play the hit once");
            Assert.That(serverHeard.Values.Single().ExcludedEntity, Is.EqualTo(SPlayer),
                "the server's sound should leave out the attacker, whose client played it already");
            Assert.That(clientHeard, Has.Count.EqualTo(1), "the attacker should hear their hit once");
            Assert.That(clientHeard.Values.All(sound => sound.ClientSide), Is.True,
                "the attacker should hear their own client's predicted sound, not the server's");
            Assert.That(clientFirstTick ?? int.MaxValue, Is.LessThanOrEqualTo(serverFirstTick ?? 0),
                "the attacker's sound should not wait for the server's hit");
        });
    }

    /// <summary>
    ///     Standing in the wire, the player hears each contact damage tick once, from their own client, which ticks in
    ///         step with the server.
    /// </summary>
    [Test]
    public async Task PlayerInTheWireHearsEachContactTickOnce()
    {
        await SpawnTarget(KsDamagedSoundTest.TestWire, PlayerCoords);
        await Server.WaitPost(() =>
        {
            // A body left still falls asleep, and a sleeping body's contacts are never updated. A walking one is awake.
            var physicsComponent = SEntMan.GetComponent<PhysicsComponent>(SPlayer);
            var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
            physicsSystem.SetSleepingAllowed(SPlayer, physicsComponent, false);
            physicsSystem.WakeBody(SPlayer, body: physicsComponent);
        });

        var clientHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var serverHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        for (var tick = 0; tick < (int)(4f / TickPeriod); tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() => KsDamagedSoundTest.CollectSounds(CEntMan, MapId, KsDamagedSoundTest.TestSoundPath, clientHeard));
            await Server.WaitPost(() => KsDamagedSoundTest.CollectSounds(SEntMan, MapId, KsDamagedSoundTest.TestSoundPath, serverHeard));
        }

        Assert.Multiple(() =>
        {
            Assert.That(serverHeard, Has.Count.GreaterThanOrEqualTo(3), "contact damage should play the sound each tick");
            Assert.That(serverHeard.Values.Select(sound => sound.ExcludedEntity), Is.All.EqualTo(SPlayer),
                "the server's sound should leave out the player in the wire, whose client predicts it");
            Assert.That(clientHeard.Values.All(sound => sound.ClientSide), Is.True,
                "the player should hear their own client's predicted sound, not the server's");
            Assert.That(clientHeard, Has.Count.InRange(serverHeard.Count - 1, serverHeard.Count + 1),
                "the player's client should predict each tick the server plays, once");
        });
    }
}
