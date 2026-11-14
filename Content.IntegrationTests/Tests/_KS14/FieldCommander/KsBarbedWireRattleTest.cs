#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._KS14.Damage;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._KS14.Damage.DamagedSound;
using Content.Shared.Audio;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Barbed wire as it is in the game: a player wading about astride two pieces of it is cut once a damage tick, and
///         hears one rattle for each cut, from their own client, not one for each piece they touch, nor another for the
///         wear the wire takes from cutting them.
/// </summary>
[TestOf(typeof(KsDamagedSoundSystem))]
public sealed class KsBarbedWireRattleTest : MovementTest
{
    private static readonly EntProtoId BarbedWire = "KsBarbedWire";
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";

    [Test]
    public async Task AstrideTwoPiecesEachCutRattlesOnce()
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        var firstWire = await SpawnEntity(BarbedWire, origin);
        var secondWire = await SpawnEntity(BarbedWire, origin.Offset(new Vector2(1f, 0f)));

        // On the line between the two, touching both.
        await Server.WaitPost(() =>
        {
            // Still on the grid, as the player is: the test's own coordinates are the map's.
            var playerTransform = SEntMan.GetComponent<TransformComponent>(SPlayer);
            Transform.SetLocalPosition(SPlayer, playerTransform.LocalPosition + new Vector2(0.5f, 0f), playerTransform);

            // A body left still falls asleep, and a sleeping body's contacts are never updated. A walking one is awake.
            var physicsSystem = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            var physicsComponent = SEntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(SPlayer);
            physicsSystem.SetSleepingAllowed(SPlayer, physicsComponent, false);
            physicsSystem.WakeBody(SPlayer, body: physicsComponent);
        });
        await RunTicks(5);

        string[] rattles = [];
        await Server.WaitAssertion(() =>
        {
            var touching = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().GetContactingEntities(SPlayer);
            Assert.That(touching, Does.Contain(firstWire).And.Contain(secondWire), "the player should be touching both pieces");

            var sound = SEntMan.GetComponent<KsDamagedSoundComponent>(firstWire).Sound;
            rattles = sound is SoundCollectionSpecifier { Collection: { } collection }
                ? ProtoMan.Index<SoundCollectionPrototype>(collection).PickFiles.Select(file => file.ToString()).ToArray()
                : [((SoundPathSpecifier)sound!).Path.ToString()];
        });

        var clientHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var serverHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var cuts = 0;
        var slash = FixedPoint2.Zero;

        // To and fro about the line, as the wire only cuts the moving. A little each way, so as to stay on both.
        const float legSeconds = 0.25f;
        var legTicks = (int)(legSeconds / TickPeriod);
        for (var leg = 0; leg < 20; leg++)
        {
            var direction = leg % 2 == 0 ? DirectionFlag.East : DirectionFlag.West;
            await SetMovementKey(direction, Robust.Shared.Input.BoundKeyState.Down);

            for (var tick = 0; tick < legTicks; tick++)
            {
                await RunTicks(1);
                await Client.WaitPost(() =>
                {
                    foreach (var rattle in rattles)
                        KsDamagedSoundTest.CollectSounds(CEntMan, MapId, rattle, clientHeard);
                });
                await Server.WaitPost(() =>
                {
                    foreach (var rattle in rattles)
                        KsDamagedSoundTest.CollectSounds(SEntMan, MapId, rattle, serverHeard);

                    var slashNow = SEntMan.System<DamageableSystem>().GetAllDamage(SPlayer).DamageDict.GetValueOrDefault(Slash);
                    if (slashNow > slash)
                        cuts++;

                    slash = slashNow;
                });
            }

            await SetMovementKey(direction, Robust.Shared.Input.BoundKeyState.Up);
        }

        Assert.Multiple(() =>
        {
            Assert.That(cuts, Is.GreaterThanOrEqualTo(3), "the wire should have cut the player a few times");
            Assert.That(serverHeard, Has.Count.EqualTo(cuts), "each cut should rattle once, not once for each piece touched");
            Assert.That(serverHeard.Values.Select(sound => sound.ExcludedEntity), Is.All.EqualTo(SPlayer),
                "every rattle should be the cut's, which the player's client predicts: none for the wire's own wear");
            Assert.That(clientHeard.Values.All(sound => sound.ClientSide), Is.True,
                "the player should hear their own client's predicted rattles, not the server's");
            Assert.That(clientHeard, Has.Count.InRange(cuts - 1, cuts + 1), "the player should hear each cut once");
        });
    }
}
