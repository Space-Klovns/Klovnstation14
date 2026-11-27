#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Damage.Pending;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     Someone who is down is only hit by a shot aimed at them
///         (<see cref="Content.Shared.Damage.Components.RequireProjectileTargetComponent"/>). A player who clicks on one
///         sees their own bullet hit, as the server's does.
/// </summary>
[TestOf(typeof(RequireProjectileTargetSystem))]
public sealed class KsDownedTargetPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsDownedTargetPredictionTestGun";
    private const string TestBurstGun = "KsDownedTargetPredictionTestBurstGun";

    private readonly EntProtoId _testBurstGun = TestBurstGun;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10

- type: entity
  id: {TestBurstGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: Burst
    availableModes:
    - Burst
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10
";

    /// <summary>
    ///     How far from the player the target is put.
    /// </summary>
    private const float TargetDistance = 4f;

    private NetEntity _gunNetEntity;

    public enum DownedBy
    {
        Lying,
        Critical,
        Dead,
    }

    private async Task ArmPlayer()
    {
        // The client predicts well past the last server state, so every state reset re-predicts, as a real client does.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        _gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown
    }

    /// <summary>
    ///     Someone down, <see cref="TargetDistance"/> from the player, whom nothing but a shot hurts.
    /// </summary>
    private async Task<EntityUid> SpawnDownedTarget(DownedBy downedBy)
    {
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(PlayerPrototype, new MapCoordinates(shooterPosition + new Vector2(TargetDistance, 0f), MapData.MapId));
        });
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            // Nothing but a shot is to hurt it: the map has no air.
            SEntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(targetUid);
            SEntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(targetUid);
            SEntMan.RemoveComponent<Content.Shared.Temperature.Components.TemperatureComponent>(targetUid);
            SEntMan.RemoveComponent<Content.Shared.Damage.Components.PassiveDamageComponent>(targetUid);

            switch (downedBy)
            {
                case DownedBy.Lying:
                    SEntMan.System<StandingStateSystem>().Down(targetUid);
                    break;
                case DownedBy.Critical:
                    SEntMan.System<MobStateSystem>().ChangeMobState(targetUid, MobState.Critical);
                    break;
                case DownedBy.Dead:
                    SEntMan.System<MobStateSystem>().ChangeMobState(targetUid, MobState.Dead);
                    break;
            }
        });
        await RunTicks(10);

        var isDown = false;
        await Server.WaitPost(() => isDown = SEntMan.System<StandingStateSystem>().IsDown(targetUid));
        Assert.That(isDown, "precondition: the target is down");

        var before = FixedPoint2.Zero;
        var after = FixedPoint2.Zero;
        await Server.WaitPost(() => before = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
        await RunSeconds(2f);
        await Server.WaitPost(() => after = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
        Assert.That(after, Is.EqualTo(before), "precondition: nothing but a shot hurts the target");

        return targetUid;
    }

    /// <summary>
    ///     Fires once along the line through <paramref name="targetUid"/>, having clicked on it or not, and watches the
    ///         shot out.
    /// </summary>
    /// <returns>
    ///     What the server's shot dealt it; the most the client showed as pending for it meanwhile, which is what its own
    ///         copy of the shot dealt; and whether that copy flew on past it.
    /// </returns>
    private async Task<(FixedPoint2 ServerDealt, FixedPoint2 ClientPending, bool ClientCopyFlewPast)> Shoot(EntityUid targetUid, bool clickedOn)
    {
        var clientTargetUid = ToClient(targetUid);
        var shooterPosition = Vector2.Zero;
        var serverBefore = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            serverBefore = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid);
        });

        // Shots still in the air from before are not this one.
        var earlierProjectiles = new HashSet<EntityUid>();
        await Client.WaitPost(() =>
        {
            var projectiles = CEntMan.EntityQueryEnumerator<ProjectileComponent>();
            while (projectiles.MoveNext(out var projectileUid, out _))
            {
                earlierProjectiles.Add(projectileUid);
            }
        });

        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(TargetDistance, 0f)));
        var targetNetEntity = SEntMan.GetNetEntity(targetUid);
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = _gunNetEntity,
                Coordinates = aimCoordinates,
                Target = clickedOn ? targetNetEntity : null,
            });
        });

        var serverAfter = serverBefore;
        var clientPending = FixedPoint2.Zero;
        var clientCopyFlewPast = false;

        // Until the gun may fire again, which is long after the shot has got there on both sides.
        var ticks = (int)Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().TickRate;
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => serverAfter = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
            await Client.WaitPost(() =>
            {
                var pending = CEntMan.System<KsPendingDamageSystem>().GetPendingDamage(clientTargetUid);
                if (pending > clientPending)
                    clientPending = pending;

                var transformSystem = CEntMan.System<SharedTransformSystem>();
                var projectiles = CEntMan.EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
                while (projectiles.MoveNext(out var projectileUid, out _, out var projectileTransform))
                {
                    // The client's own copy of the shot, not the server's, which it hides.
                    if (CEntMan.IsClientSide(projectileUid) &&
                        !earlierProjectiles.Contains(projectileUid) &&
                        (transformSystem.GetWorldPosition(projectileTransform) - shooterPosition).Length() >
                        (transformSystem.GetWorldPosition(clientTargetUid) - shooterPosition).Length() + 1.5f)
                        clientCopyFlewPast = true;
                }
            });
        }

        return (serverAfter - serverBefore, clientPending, clientCopyFlewPast);
    }

    /// <summary>
    ///     The client's copy of a shot at someone down does what the server's does: hits them if they were clicked on,
    ///         and flies over them if they were not.
    /// </summary>
    [Test]
    public async Task ClientsShotAtSomeoneDownDoesWhatTheServersDoes([Values] DownedBy downedBy)
    {
        await ArmPlayer();

        var targetUid = await SpawnDownedTarget(downedBy);

        var (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(targetUid, clickedOn: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverDealt, Is.EqualTo(FixedPoint2.Zero), "precondition: the server's shot flew over one not clicked on");
            Assert.That(clientPending, Is.EqualTo(FixedPoint2.Zero), "the client's copy of the shot hit one not clicked on");
            Assert.That(clientCopyFlewPast, "the client's copy of the shot stopped at one not clicked on");
        }

        for (var shot = 0; shot < 3; shot++)
        {
            (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(targetUid, clickedOn: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(serverDealt, Is.GreaterThan(FixedPoint2.Zero), $"precondition: the server's shot {shot} hit the one clicked on");
                Assert.That(clientPending, Is.GreaterThan(FixedPoint2.Zero), $"the client's copy of shot {shot} did not hit the one clicked on");
                Assert.That(clientCopyFlewPast, Is.False, $"the client's copy of shot {shot} flew over the one clicked on");
            }
        }
    }

    /// <summary>
    ///     The shots of a burst that the server fires by itself, the ones after the first, are aimed at whoever the first
    ///         was: they hit someone down who was clicked on, as it did.
    /// </summary>
    [Test]
    public async Task EveryShotOfABurstHitsTheOneClickedOn()
    {
        var targetUid = await SpawnDownedTarget(DownedBy.Lying);

        // Fired by someone with no client, whose gun's burst nothing but the server carries on.
        var shotsPerBurst = 0;
        var shooterUid = EntityUid.Invalid;
        var gunUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterCoordinates = new MapCoordinates(SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer) + new Vector2(0f, 1f), MapData.MapId);
            shooterUid = SEntMan.SpawnEntity(PlayerPrototype, shooterCoordinates);
            gunUid = SEntMan.SpawnEntity(_testBurstGun, shooterCoordinates);
            Assert.That(SEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(shooterUid, gunUid),
                "precondition: the shooter holds the gun");
        });
        await RunSeconds(2f); // the pickup cooldown

        await Server.WaitPost(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            var gunSystem = SEntMan.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>();
            var gunComponent = SEntMan.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(gunUid);
            shotsPerBurst = gunComponent.ShotsPerBurstModified;
            var fired = gunSystem.AttemptShoot(shooterUid, (gunUid, gunComponent), transformSystem.GetMoverCoordinates(targetUid), targetUid);
            Assert.That(fired, "precondition: the burst began");
        });
        Assert.That(shotsPerBurst, Is.GreaterThan(1), "precondition: a burst is more than one shot");

        // Each hit is a tick on which its damage rose.
        var hits = 0;
        var last = FixedPoint2.Zero;
        await Server.WaitPost(() => last = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
        var ticks = (int)Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().TickRate * 3;
        for (var tick = 0; tick < ticks && hits < shotsPerBurst; tick++)
        {
            await RunTicks(1);
            var now = last;
            await Server.WaitPost(() => now = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
            if (now > last)
                hits++;

            last = now;
        }

        Assert.That(hits, Is.EqualTo(shotsPerBurst), "not every shot of the burst hit the one clicked on");
    }

    /// <summary>
    ///     A corpse lying still on a grid has its collision switched off while its body is asleep
    ///         (<see cref="CollisionWakeComponent"/>, which the dead are given). A shot aimed at it hits it all the same, on
    ///         the client as on the server.
    /// </summary>
    [Test]
    public async Task ShotHitsACorpseClickedOnWhoseBodyIsAsleep()
    {
        await ArmPlayer();
        // On a grid: off one, collision is never switched off.
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var position = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            var tile = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = 0f; x <= TargetDistance + 1f; x++)
            {
                mapSystem.SetTile(MapData.Grid, mapSystem.WorldToTile(MapData.Grid, MapData.Grid.Comp, position + new Vector2(x, 0f)), tile);
            }
        });
        await RunTicks(5);

        var targetUid = await SpawnDownedTarget(DownedBy.Dead);

        var asleep = false;
        for (var second = 0; second < 20 && !asleep; second++)
        {
            await RunSeconds(1f);
            await Server.WaitPost(() => asleep = !SEntMan.GetComponent<PhysicsComponent>(targetUid).CanCollide);
        }

        var clientAsleep = false;
        await Client.WaitPost(() => clientAsleep = !CEntMan.GetComponent<PhysicsComponent>(ToClient(targetUid)).CanCollide);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asleep, "precondition: the corpse's collision is off on the server");
            Assert.That(clientAsleep, "precondition: the corpse's collision is off on the client");
        }

        var (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(targetUid, clickedOn: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverDealt, Is.GreaterThan(FixedPoint2.Zero), "the server's shot did not hit the corpse clicked on");
            Assert.That(clientPending, Is.GreaterThan(FixedPoint2.Zero), "the client's copy of the shot did not hit the corpse clicked on");
            Assert.That(clientCopyFlewPast, Is.False, "the client's copy of the shot flew over the corpse clicked on");
        }
    }
}
