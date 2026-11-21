#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Trauma.Projectiles;
using Content.Shared._KS14.Damage.Pending;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     What a client shows of a hit is what the server did: a target's damage on a client never runs ahead of the server's
///         and never goes back, and a gun's spread is the same on both.
///
///     Each of these was a client changing networked state that the server never confirmed, so the next state put it back
///         and the server's own change then arrived: health flickered on every hit, and a predicted shot flew a different
///         line from the server's.
/// </summary>
[TestOf(typeof(PredictedProjectileSystem))]
public sealed class KsGunHitPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsGunHitPredictionTestGun";
    private const string TestSpreadGun = "KsGunHitPredictionTestSpreadGun";
    private const string TestTarget = "KsGunHitPredictionTestTarget";
    private const string TestArmouredTarget = "KsGunHitPredictionTestArmouredTarget";
    private const string TestModifierSet = "KsGunHitPredictionTestModifierSet";
    private const string TestPassiveMob = "KsGunHitPredictionTestPassiveMob";
    private const string TestShield = "KsGunHitPredictionTestShield";
    private const string TestFrailMob = "KsGunHitPredictionTestFrailMob";
    private const string TestLaser = "KsGunHitPredictionTestLaser";
    private const string TestRay = "KsGunHitPredictionTestRay";
    private const string TestBleeder = "KsGunHitPredictionTestBleeder";
    private const string TestBleedSet = "KsGunHitPredictionTestBleedSet";

    private readonly EntProtoId _testTarget = TestTarget;
    private readonly EntProtoId _testArmouredTarget = TestArmouredTarget;
    private readonly EntProtoId _testPassiveMob = TestPassiveMob;
    private readonly EntProtoId _testShield = TestShield;
    private readonly EntProtoId _testFrailMob = TestFrailMob;
    private static readonly ProtoId<DamageGroupPrototype> BruteGroup = "Brute";
    private readonly EntProtoId _testBleeder = TestBleeder;
    private static readonly ProtoId<Content.Shared._KS14.Particles.KsParticlesPrototype> SplatterMist = "KsBloodSplatterMist";
    private static readonly EntProtoId BulletPistol = "BulletPistol";
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";

    /// <summary>
    ///     How many spread shots are fired: enough that the spread has built up well past where it starts.
    /// </summary>
    private const int SpreadShots = 4;

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
  id: {TestSpreadGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 5
    minAngle: 0
    maxAngle: 80
    angleIncrease: 10
    angleDecay: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10

- type: entity
  id: {TestTarget}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        hard: true
        layer:
        - WallLayer
        mask:
        - FullTileMask
  - type: Damageable
  - type: Injurable

- type: damageModifierSet
  id: {TestModifierSet}
  coefficients:
    Piercing: 0.5
  flatReductions:
    Piercing: 2

- type: entity
  parent: {TestTarget}
  id: {TestArmouredTarget}
  components:
  - type: Damageable
    damageModifierSet: {TestModifierSet}

- type: entity
  id: {TestRay}
  components:
  - type: HitscanAmmo
  - type: HitscanBasicRaycast
  - type: HitscanBasicDamage
    damage:
      types:
        Piercing: 16

- type: entity
  id: {TestLaser}
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
    proto: {TestRay}
    capacity: 10
    count: 10

- type: damageModifierSet
  id: {TestBleedSet}
  coefficients:
    Piercing: 1

- type: entity
  id: {TestBleeder}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        hard: true
        layer:
        - MobLayer
        mask:
        - MobMask
  - type: Damageable
  - type: Injurable
  - type: SolutionContainerManager
  - type: Bloodstream
    damageBleedModifiers: {TestBleedSet}
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1

- type: entity
  id: {TestFrailMob}
  components:
  - type: Damageable
  - type: Injurable
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      20: Critical

- type: entity
  id: {TestShield}
  components:
  - type: Damageable
  - type: Injurable
  - type: Blocking
    passiveBlockModifier:
      coefficients:
        Piercing: 0.9
    activeBlockModifier:
      coefficients:
        Piercing: 0.8

- type: entity
  id: {TestPassiveMob}
  components:
  - type: Damageable
  - type: Injurable
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
  - type: PassiveDamage
    allowedStates:
    - Alive
    damage:
      types:
        Blunt: 1
";

    private async Task<FixedPoint2> ServerDamage(EntityUid serverUid)
    {
        var total = FixedPoint2.Zero;
        await Server.WaitPost(() => total = SEntMan.System<DamageableSystem>().GetTotalDamage(serverUid));
        return total;
    }

    private async Task<FixedPoint2> ClientDamage(EntityUid clientUid)
    {
        var total = FixedPoint2.Zero;
        await Client.WaitPost(() => total = CEntMan.System<DamageableSystem>().GetTotalDamage(clientUid));
        return total;
    }

    /// <summary>
    ///     Runs <paramref name="ticks"/> ticks one at a time, and after each checks that the client's damage of
    ///         <paramref name="serverUid"/> is no more than the server's and no less than it was the tick before.
    /// </summary>
    /// <returns>The server's damage at the end.</returns>
    private async Task<FixedPoint2> AssertClientDamageFollowsTheServers(EntityUid serverUid, int ticks)
    {
        var clientUid = ToClient(serverUid);
        var previousClientDamage = await ClientDamage(clientUid);
        var serverDamage = FixedPoint2.Zero;

        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);

            serverDamage = await ServerDamage(serverUid);
            var clientDamage = await ClientDamage(clientUid);

            Assert.That(clientDamage, Is.LessThanOrEqualTo(serverDamage),
                $"tick {tick}: the client shows {clientDamage} damage, the server has only dealt {serverDamage}");
            Assert.That(clientDamage, Is.GreaterThanOrEqualTo(previousClientDamage),
                $"tick {tick}: the client's damage went back from {previousClientDamage} to {clientDamage}");

            previousClientDamage = clientDamage;
        }

        return serverDamage;
    }

    /// <summary>
    ///     The shooter's client keeps its own copy of the bullet, which hits before the server's does. The client shows
    ///         that hit's damage at once, as pending, and it stays put from then on: not taken back by the states that
    ///         follow, and not counted twice when the server's hit arrives. Dealt by the kept copy as ordinary damage, it
    ///         was put back by the next state and dealt again by the server's hit.
    /// </summary>
    [Test]
    public async Task ShootersClientShowsTheHitAtOnceAndItStaysPut()
    {
        // The client predicts well past the last server state, so every state reset re-predicts, as a real client does.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var shooterPosition = Vector2.Zero;
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testTarget, new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId));
        });
        await RunTicks(5); // the target reaches the client

        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = aimCoordinates,
            });
        });

        var clientTargetUid = ToClient(targetUid);
        var clientDamages = new List<FixedPoint2>();
        var shownBeforeTheServerDealtIt = false;
        var serverDamage = FixedPoint2.Zero;

        // Well past the round trip, and past how long pending damage waits: nothing is left pending at the end.
        var ticks = (int)(3f * (float)Server.ResolveDependency<IGameTiming>().TickRate);
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);

            serverDamage = await ServerDamage(targetUid);
            var clientDamage = await ClientDamage(clientTargetUid);

            if (clientDamages.Count > 0)
            {
                Assert.That(clientDamage, Is.GreaterThanOrEqualTo(clientDamages[^1]),
                    $"tick {tick}: the client's damage went back from {clientDamages[^1]} to {clientDamage}");
            }

            shownBeforeTheServerDealtIt |= clientDamage > FixedPoint2.Zero && serverDamage == FixedPoint2.Zero;

            // Whatever is shown beyond what the server dealt is pending, and accounted for as pending.
            var pendingNow = FixedPoint2.Zero;
            await Client.WaitPost(() => pendingNow = CEntMan.System<KsPendingDamageSystem>().GetPendingDamage(clientTargetUid));
            Assert.That(clientDamage - pendingNow, Is.LessThanOrEqualTo(serverDamage),
                $"tick {tick}: the client shows {clientDamage} with {pendingNow} pending, and the server has only dealt {serverDamage}");
            clientDamages.Add(clientDamage);
        }

        Assert.That(serverDamage, Is.GreaterThan(FixedPoint2.Zero), "the server's bullet never damaged the target, so this proves nothing");
        Assert.That(shownBeforeTheServerDealtIt, "the client only showed the hit's damage once the server had dealt it");
        Assert.That(clientDamages.Max(), Is.EqualTo(serverDamage), "the client showed more than the one hit: the server's was counted on top of its own");
        Assert.That(clientDamages[^1], Is.EqualTo(serverDamage), "the client did not end on the server's damage");

        var pendingAtTheEnd = FixedPoint2.Zero;
        await Client.WaitPost(() => pendingAtTheEnd = CEntMan.System<KsPendingDamageSystem>().GetPendingDamage(clientTargetUid));
        Assert.That(pendingAtTheEnd, Is.EqualTo(FixedPoint2.Zero), "damage is still pending on the client long after the server dealt it");
    }

    /// <summary>
    ///     Damage a client predicted that the server never deals (its bullet missed there) is taken back off, once, after
    ///         waiting for it. Pending damage is matched to its shot: someone else's hit meanwhile, of the same kind and
    ///         size, is not mistaken for it, and nor is the server's hit of another shot.
    /// </summary>
    [Test]
    public async Task PendingDamageTheServerNeverDealsIsTakenBackOnce()
    {
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testTarget, new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var clientTargetUid = ToClient(targetUid);
        var predicted = FixedPoint2.New(16);
        var unrelated = predicted; // as like the pending hit as damage can be
        const int shotId = 7;

        await Client.WaitPost(() =>
            CEntMan.System<KsPendingDamageSystem>().Add(clientTargetUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), predicted), shotId));
        Assert.That(await ClientDamage(clientTargetUid), Is.EqualTo(predicted), "the predicted damage did not show at once");

        // Someone else hits it just as hard, in the same type: not the shot that is pending.
        await Server.WaitPost(() =>
            SEntMan.System<DamageableSystem>().ChangeDamage(targetUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), unrelated)));
        await RunTicks(5);
        Assert.That(await ClientDamage(clientTargetUid), Is.EqualTo(predicted + unrelated),
            "someone else's damage was taken for the pending hit, or did not arrive on top of it");

        // Nor is the server's hit of another shot.
        var resolvedByAnotherShot = false;
        await Client.WaitPost(() => resolvedByAnotherShot = CEntMan.System<KsPendingDamageSystem>().Resolve(clientTargetUid, shotId + 1));
        Assert.That(resolvedByAnotherShot, Is.False, "another shot's hit took this shot's pending damage off");
        Assert.That(await ClientDamage(clientTargetUid), Is.EqualTo(predicted + unrelated));

        // It goes exactly once: down to the server's damage, and stays there.
        var drops = 0;
        var previous = predicted + unrelated;
        var ticks = (int)(3f * (float)Server.ResolveDependency<IGameTiming>().TickRate);
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);
            var clientDamage = await ClientDamage(clientTargetUid);
            if (clientDamage < previous)
                drops++;

            previous = clientDamage;
        }

        Assert.That(previous, Is.EqualTo(unrelated), "damage the server never dealt is still shown on the client");
        Assert.That(drops, Is.EqualTo(1), "the client's damage came down more than once");
    }

    /// <summary>
    ///     A bullet a client does not predict (anyone else's) is the server's to resolve: its hit here deals nothing and
    ///         does not spend the client's copy of it. This is what a victim's client did to itself with every bullet
    ///         that reached it.
    /// </summary>
    [Test]
    public async Task UnpredictedBulletHitsNothingOnAClient()
    {
        var targetUid = EntityUid.Invalid;
        var bulletUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testTarget, new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId));
            // At rest beside it, and no one's: it stays where it is, and this client is not its shooter.
            bulletUid = SEntMan.SpawnEntity(BulletPistol, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var clientTargetUid = ToClient(targetUid);
        var clientBulletUid = ToClient(bulletUid);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<PhysicsComponent>(clientBulletUid).Predict, Is.False,
                "the client predicts the bullet, so this proves nothing");

            CEntMan.System<PredictedProjectileSystem>().DoHit(clientBulletUid, clientTargetUid);

            Assert.That(CEntMan.System<DamageableSystem>().GetTotalDamage(clientTargetUid), Is.EqualTo(FixedPoint2.Zero),
                "a bullet the client does not predict dealt damage on it");
            Assert.That(CEntMan.GetComponent<ProjectileComponent>(clientBulletUid).ProjectileSpent, Is.False,
                "a bullet the client does not predict was spent on it, which no state undoes");
        });

        // The same hit on the server is a hit.
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<PredictedProjectileSystem>().DoHit(bulletUid, targetUid);
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid), Is.GreaterThan(FixedPoint2.Zero),
                "the bullet does no damage on the server either, so this proves nothing");
        });
    }

    /// <summary>
    ///     A gun's spread builds with every shot, on the client as on the server: the angle it has reached and when it
    ///         last fired are networked. Neither was, so every state put the client's spread back to where it started, and
    ///         its predicted shots flew a narrower line than the server's.
    /// </summary>
    [Test]
    public async Task SpreadBuildsTheSameOnClientAndServer()
    {
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var gunNetEntity = await PlaceInHands(TestSpreadGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() => shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer));
        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));

        var serverGunUid = SEntMan.GetEntity(gunNetEntity);
        var clientGunUid = ToClient(gunNetEntity);
        var clientTiming = Client.ResolveDependency<IGameTiming>();

        var shotsAsked = 0;
        var ticksPerSecond = (int)clientTiming.TickRate;
        for (var tick = 0; tick < ticksPerSecond * 10 && shotsAsked < SpreadShots; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                if (CEntMan.GetComponent<GunComponent>(clientGunUid).NextFire > clientTiming.CurTime)
                    return;

                shotsAsked++;
                CEntMan.RaisePredictiveEvent(new RequestShootEvent
                {
                    Gun = gunNetEntity,
                    Coordinates = aimCoordinates,
                });
            });
        }

        Assert.That(shotsAsked, Is.EqualTo(SpreadShots), "the client never got to ask for every shot");
        await RunTicks(ticksPerSecond); // well past the round trip of the last

        var serverAngle = Angle.Zero;
        var serverLastFire = TimeSpan.Zero;
        await Server.WaitPost(() =>
        {
            var gunComponent = SEntMan.GetComponent<GunComponent>(serverGunUid);
            serverAngle = gunComponent.CurrentAngle;
            serverLastFire = gunComponent.LastFire;
        });

        await Client.WaitAssertion(() =>
        {
            var gunComponent = CEntMan.GetComponent<GunComponent>(clientGunUid);
            Assert.Multiple(() =>
            {
                Assert.That(serverAngle.Theta, Is.GreaterThan(gunComponent.MinAngleModified.Theta),
                    "the server's spread never built up, so this proves nothing");
                Assert.That(gunComponent.CurrentAngle.Theta, Is.EqualTo(serverAngle.Theta).Within(0.0001),
                    "the client's spread is not the server's");
                Assert.That(gunComponent.LastFire, Is.EqualTo(serverLastFire),
                    "the client's time of the last shot, which the spread settles from, is not the server's");
            });
        });
    }

    /// <summary>
    ///     What a mob takes or heals by itself every interval is the server's: a client did it on a clock of its own, and
    ///         showed each step before the server made it.
    /// </summary>
    [Test]
    public async Task PassiveDamageIsNotAheadOnAClient()
    {
        var mobUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            mobUid = SEntMan.SpawnEntity(_testPassiveMob, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var interval = 0f;
        await Server.WaitPost(() => interval = SEntMan.GetComponent<PassiveDamageComponent>(mobUid).Interval);
        var ticksPerSecond = (float)Server.ResolveDependency<IGameTiming>().TickRate;

        // Several intervals, each of which the server damages it on once.
        var serverDamage = await AssertClientDamageFollowsTheServers(mobUid, ticks: (int)(interval * 4f * ticksPerSecond));

        Assert.That(serverDamage, Is.GreaterThan(FixedPoint2.Zero), "the server never damaged the mob, so this proves nothing");
    }

    /// <summary>
    ///     <see cref="DamageableSystem.PreviewDamage"/> says what <see cref="DamageableSystem.ChangeDamage"/> then deals,
    ///         resistances included, and deals none of it.
    /// </summary>
    [Test]
    public async Task PreviewedDamageIsWhatIsDealtAndDealsNone()
    {
        await Server.WaitAssertion(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            var targetUid = SEntMan.SpawnEntity(_testArmouredTarget, new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId));
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var damageableComponent = SEntMan.GetComponent<DamageableComponent>(targetUid);

            var raw = new DamageSpecifier(ProtoMan.Index(PiercingDamage), FixedPoint2.New(20));

            var previewed = damageableSystem.PreviewDamage((targetUid, damageableComponent), raw);
            Assert.That(damageableSystem.GetTotalDamage(targetUid), Is.EqualTo(FixedPoint2.Zero), "previewing damage dealt it");
            Assert.That(previewed.GetTotal(), Is.LessThan(raw.GetTotal()), "the target resists nothing, so this proves nothing");

            var dealt = damageableSystem.ChangeDamage((targetUid, damageableComponent), raw);
            Assert.That(previewed.GetTotal(), Is.EqualTo(dealt.GetTotal()), "the preview is not what was dealt");
            Assert.That(damageableSystem.GetTotalDamage(targetUid), Is.EqualTo(dealt.GetTotal()));
        });
    }

    /// <summary>
    ///     A shield takes a share of what it blocks, from the handler that works out what is left
    ///         (<c>BlockingSystem.OnUserDamageModified</c>). Asked what the damage would be, it blocks the same share and
    ///         takes none: a preview that damaged the shield would be the flicker moved from its holder onto it.
    /// </summary>
    [Test]
    public async Task PreviewedDamageTakesNothingFromABlockingShield()
    {
        await Server.WaitAssertion(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            var coordinates = new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId);
            var holderUid = SEntMan.SpawnEntity(_testTarget, coordinates);
            var shieldUid = SEntMan.SpawnEntity(_testShield, coordinates);
            SEntMan.EnsureComponent<Content.Shared.Blocking.BlockingUserComponent>(holderUid).BlockingItem = shieldUid;

            var damageableSystem = SEntMan.System<DamageableSystem>();
            var raw = new DamageSpecifier(ProtoMan.Index(PiercingDamage), FixedPoint2.New(20));

            var previewed = damageableSystem.PreviewDamage(holderUid, raw);
            Assert.That(previewed.GetTotal(), Is.LessThan(raw.GetTotal()), "the shield blocks nothing, so this proves nothing");
            Assert.That(damageableSystem.GetTotalDamage(shieldUid), Is.EqualTo(FixedPoint2.Zero), "previewing damage damaged the shield that would block it");

            var dealt = damageableSystem.ChangeDamage(holderUid, raw);
            Assert.That(previewed.GetTotal(), Is.EqualTo(dealt.GetTotal()), "the preview is not what was dealt");
            Assert.That(damageableSystem.GetTotalDamage(shieldUid), Is.GreaterThan(FixedPoint2.Zero), "the shield takes nothing of a real hit either, so this proves nothing");
        });
    }

    /// <summary>
    ///     What a hit on something that bleeds shows, the shooter's client shows at once, from its own prediction: a spray
    ///         of its own (client-side), there before the server's damage is. For a bullet, whose kept copy deals no
    ///         damage here, as for a hitscan shot, whose damage is predicted with the shot.
    /// </summary>
    [Test]
    public async Task ShootersClientSpraysBloodBeforeTheServersDamageArrives([Values(TestGun, TestLaser)] string gun)
    {
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var gunNetEntity = await PlaceInHands(gun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var shooterPosition = Vector2.Zero;
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testBleeder, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));
        });
        await RunTicks(5); // the target reaches the client

        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = aimCoordinates,
            });
        });

        int? sprayTick = null;
        int? serverDamageTick = null;
        for (var tick = 0; tick < 60 && (sprayTick == null || serverDamageTick == null); tick++)
        {
            await RunTicks(1);

            if (serverDamageTick == null && await ServerDamage(targetUid) > FixedPoint2.Zero)
                serverDamageTick = tick;

            await Client.WaitPost(() =>
            {
                if (sprayTick != null)
                    return;

                var query = CEntMan.AllEntityQueryEnumerator<Content.Shared._KS14.Particles.KsParticlesComponent>();
                while (query.MoveNext(out var uid, out var particlesComponent))
                {
                    if (particlesComponent.Prototype == SplatterMist && CEntMan.IsClientSide(uid))
                        sprayTick = tick;
                }
            });
        }

        Assert.That(serverDamageTick, Is.Not.Null, "the server's shot never damaged the target, so this proves nothing");
        Assert.That(sprayTick, Is.Not.Null, "the shooter's client never sprayed blood of its own for the hit");
        Assert.That(sprayTick, Is.LessThanOrEqualTo(serverDamageTick), "the shooter's client sprayed its blood only after the server's hit");
    }

    /// <summary>
    ///     Whatever asks the damage system for an entity's damage on a client gets what is pending for it too, in the
    ///         total, by type and by group, with nothing of its own to do for it.
    /// </summary>
    [Test]
    public async Task AskingForDamageIncludesWhatIsPending()
    {
        var targetUid = EntityUid.Invalid;
        var served = FixedPoint2.New(3);
        var predicted = FixedPoint2.New(16);
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testTarget, new MapCoordinates(shooterPosition + new Vector2(5f, 0f), MapData.MapId));
            SEntMan.System<DamageableSystem>().ChangeDamage(targetUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), served));
        });
        await RunTicks(5);

        var clientTargetUid = ToClient(targetUid);
        await Client.WaitAssertion(() =>
        {
            var damageableSystem = CEntMan.System<DamageableSystem>();
            CEntMan.System<KsPendingDamageSystem>().Add(clientTargetUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), predicted), shotId: 1);

            Assert.Multiple(() =>
            {
#pragma warning disable CS0618 // the getters are what is under test
                Assert.That(damageableSystem.GetTotalDamage(clientTargetUid), Is.EqualTo(served + predicted), "the total");
                Assert.That(damageableSystem.GetAllDamage(clientTargetUid).DamageDict[PiercingDamage], Is.EqualTo(served + predicted), "by type");
                Assert.That(damageableSystem.GetDamagePerGroup(clientTargetUid)[BruteGroup], Is.EqualTo(served + predicted), "by group");
#pragma warning restore CS0618
            });
        });

        // The server's own value is untouched by it.
        Assert.That(await ServerDamage(targetUid), Is.EqualTo(served));
    }

    /// <summary>
    ///     Pending damage does not put a mob down. With enough pending to cross its critical threshold, a little damage
    ///         from the server (whose state makes the client check thresholds) leaves it alive on the client: while a
    ///         state is applied, an entity's damage is the server's alone. A mob put down here by a hit the server never
    ///         made would stay down on this client, as the server has nothing to correct.
    /// </summary>
    [Test]
    public async Task PendingDamageDoesNotPutAMobDown()
    {
        var mobUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            mobUid = SEntMan.SpawnEntity(_testFrailMob, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var clientMobUid = ToClient(mobUid);
        var critical = FixedPoint2.Zero;
        await Client.WaitAssertion(() =>
        {
            var thresholdSystem = CEntMan.System<Content.Shared.Mobs.Systems.MobThresholdSystem>();
            Assert.That(thresholdSystem.TryGetThresholdForState(clientMobUid, Content.Shared.Mobs.MobState.Critical, out var threshold));
            critical = threshold!.Value;

            // A little short of critical by itself; over it with what the server is about to deal.
            CEntMan.System<KsPendingDamageSystem>().Add(clientMobUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), critical - FixedPoint2.New(1)), shotId: 1);
        });

        await Server.WaitPost(() =>
            SEntMan.System<DamageableSystem>().ChangeDamage(mobUid, new DamageSpecifier(ProtoMan.Index(PiercingDamage), FixedPoint2.New(2))));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.System<DamageableSystem>().GetTotalDamage(clientMobUid), Is.GreaterThanOrEqualTo(critical),
                "the damage shown is not over the threshold, so this proves nothing");
            Assert.That(CEntMan.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(clientMobUid),
                "the mob went down on the client for damage the server has not dealt");
        });
    }

    /// <summary>
    ///     The server's decals on a client, by where they are: those of every chunk, the client's own predicted ones left out.
    /// </summary>
    private List<Content.Shared.Decals.Decal> ClientServerDecals()
    {
        var decals = new List<Content.Shared.Decals.Decal>();
        var query = CEntMan.AllEntityQueryEnumerator<Content.Shared.Decals.DecalChunkComponent>();
        while (query.MoveNext(out var chunkComponent))
        {
            foreach (var (decalId, decal) in chunkComponent.Decals)
            {
                if (decalId < Content.Shared.Decals.DecalChunkComponent.MinPredictedDecalId)
                    decals.Add(decal);
            }
        }

        return decals;
    }

    /// <summary>
    ///     The blood stains of a bullet's hit stay on the shooter's screen from the moment their own bullet hits: not gone
    ///         with the next state until the server's arrive, never shown twice, and laid out as the server lays its own
    ///         out, so nothing changes when those take their place. They were written into the networked decal chunk,
    ///         which the next state wiped, and rolled from the tick, which is not the server's tick for the hit.
    /// </summary>
    [Test]
    public async Task ShootersBloodStainsStayPutAndMatchTheServers()
    {
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        // Floor under the target and behind it: a decal needs a tile.
        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            var plating = new Tile(TileMan[Plating].TileId);
            for (var tileX = -2; tileX <= 10; tileX++)
            {
                for (var tileY = -4; tileY <= 4; tileY++)
                    MapSystem.SetTile(MapData.Grid, new Vector2i(tileX, tileY), plating);
            }

            SEntMan.SpawnEntity(_testBleeder, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));
        });
        await RunTicks(5);

        var clientGridUid = ToClient(MapData.Grid.Owner);
        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = aimCoordinates,
            });
        });

        var decalSystem = CEntMan.System<Content.Client.Decals.DecalSystem>();
        List<Content.Shared.Decals.Decal>? predicted = null;
        var mostShown = 0;
        var ticks = (int)(3f * (float)Server.ResolveDependency<IGameTiming>().TickRate);
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);
            await Client.WaitAssertion(() =>
            {
                var kept = decalSystem.KsGetKeptDecals(clientGridUid).Select(keptDecal => keptDecal.Decal).ToList();
                var shown = kept.Count + ClientServerDecals().Count;
                mostShown = Math.Max(mostShown, shown);

                if (predicted == null)
                {
                    if (kept.Count > 0)
                        predicted = kept;

                    return;
                }

                Assert.That(shown, Is.GreaterThanOrEqualTo(predicted.Count),
                    $"tick {tick}: {predicted.Count} stains were predicted and {shown} are shown: some went before the server's came");
            });
        }

        Assert.That(predicted, Is.Not.Null, "the shooter's client never kept a predicted blood stain");

        await Client.WaitAssertion(() =>
        {
            var served = ClientServerDecals();
            Assert.That(served, Is.Not.Empty, "the server's stains never reached the client, so this proves nothing");
            Assert.That(decalSystem.KsGetKeptDecals(clientGridUid), Is.Empty, "predicted stains are still kept beside the server's own");
            Assert.That(mostShown, Is.EqualTo(served.Count), "more stains were shown at once than the hit left: predicted and server's together");

            // Laid out alike: each the same decal, colour and turn, and close by.
            Assert.That(predicted!, Has.Count.EqualTo(served.Count), "the client predicted a different number of stains");
            foreach (var predictedDecal in predicted!)
            {
                Assert.That(served.Any(servedDecal =>
                        servedDecal.Id == predictedDecal.Id &&
                        servedDecal.Color == predictedDecal.Color &&
                        servedDecal.Angle.EqualsApprox(predictedDecal.Angle) &&
                        Vector2.Distance(servedDecal.Coordinates, predictedDecal.Coordinates) < 0.5f),
                    $"no stain of the server's matches the predicted {predictedDecal.Id} at {predictedDecal.Coordinates}");
            }
        });
    }
}
