#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     One click on a burst gun fires one burst on the shooter's client, not two (#415).
///
///     The burst's progress (<see cref="GunComponent.BurstActivated"/>, <see cref="GunComponent.BurstShotsCount"/>) is
///         networked with field deltas, so a value the server never marks dirty never reaches the client. Every state
///         reset then put the client's burst back to "not started", re-predicting the rest of it counted the burst
///         short, and the client went on to fire a second one.
/// </summary>
[TestOf(typeof(SharedGunSystem))]
public sealed class KsBurstFirePredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestBurstGun = "KsBurstFirePredictionTestGun";
    private const int ShotsPerBurst = 3;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestBurstGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 8
    burstFireRate: 8
    burstCooldown: 0.25
    shotsPerBurst: 3
    minAngle: 0
    maxAngle: 0
    selectedMode: Burst
    availableModes:
    - Burst
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 30
    count: 30
";

    /// <summary>
    ///     The client fires one burst and its prediction asks for exactly the burst's shots; the server fires the same.
    ///
    ///     A headless client has no mouse over a viewport, so <c>GunSystem.Update</c>'s own continuation (it keeps
    ///         asking to shoot while <see cref="GunComponent.BurstActivated"/>, after the button is released) is done
    ///         here, between ticks, from the client's predicted gun.
    /// </summary>
    [Test]
    public async Task OneClickFiresOneBurst()
    {
        // The client predicts well past the last server state, so every state reset re-predicts part of the burst.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var gunNetEntity = await PlaceInHands(TestBurstGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() => shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer));
        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));

        var clientGunUid = ToClient(gunNetEntity);
        var clientTiming = Client.ResolveDependency<IGameTiming>();
        var requests = 0;

        void RequestShot()
        {
            requests++;
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        }

        await Client.WaitPost(RequestShot); // the click

        // Long enough for two bursts and their cooldowns, and well past the round trip.
        var serverBullets = new HashSet<EntityUid>();
        for (var tick = 0; tick < 90; tick++)
        {
            await RunTicks(1);

            await Client.WaitPost(() =>
            {
                var gunComponent = CEntMan.GetComponent<GunComponent>(clientGunUid);
                if (gunComponent.BurstActivated && gunComponent.NextFire <= clientTiming.CurTime)
                    RequestShot();
            });
            await Server.WaitPost(() =>
            {
                var enumerator = SEntMan.AllEntityQueryEnumerator<ProjectileComponent>();
                while (enumerator.MoveNext(out var uid, out var projectileComponent))
                {
                    if (projectileComponent.Shooter == SPlayer)
                        serverBullets.Add(uid);
                }
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(serverBullets, Has.Count.EqualTo(ShotsPerBurst), "the server did not fire exactly one burst");
            Assert.That(requests, Is.EqualTo(ShotsPerBurst),
                $"the client asked to shoot {requests} times for one click on a {ShotsPerBurst}-round burst");
        });
    }
}
