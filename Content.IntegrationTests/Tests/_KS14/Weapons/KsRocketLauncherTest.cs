#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.Projectiles;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     Rocket launchers: the rocket detonating on a mob, and <see cref="GunBackblastComponent"/>'s cone behind the
///         shooter.
///
///     Every damage assertion is a difference against a reading taken just before the shot, never an absolute
///         value: a mob picks up a trickle of Blunt and Heat from its surroundings over the seconds a test takes, and
///         "has any Heat" would pass on that alone.
/// </summary>
public sealed class KsRocketLauncherTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // two hands, so the launcher can be wielded

    private const string MobHuman = "MobHuman";
    private const string FloorSteel = "FloorSteel";
    private const string Heat = "Heat";
    private const string Blunt = "Blunt";

    private const string At4AlwaysBreaksTiles = "KsRocketLauncherTestAt4AlwaysBreaksTiles";
    private const string At4NeverBreaksTiles = "KsRocketLauncherTestAt4NeverBreaksTiles";

    /// <summary>
    ///     How much more Heat than it had before the shot a mob must have for it to count as caught in the explosion.
    ///         One point of explosion intensity is 5 Heat (the <c>Default</c> explosion type), and the rocket's is
    ///         far higher at its epicentre; the surroundings add well under 1 over the second the test waits.
    /// </summary>
    private static readonly FixedPoint2 ExplosionHeatThreshold = FixedPoint2.New(5);

    // The shipped AT-4, with only the tilebreak roll pinned so the tile assertions are deterministic.
    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  parent: KsWeaponLauncherRocketAt4
  id: {At4AlwaysBreaksTiles}
  components:
  - type: GunBackblast
    tilebreakChance: 1

- type: entity
  parent: KsWeaponLauncherRocketAt4
  id: {At4NeverBreaksTiles}
  components:
  - type: GunBackblast
    tilebreakChance: 0
";

    #region Rocket detonation

    /// <summary>
    ///     A player's rocket caught by a lag-compensation ghost must still detonate. The ghost's fixture is soft,
    ///         so nothing that listens for hard collisions sees the ghost contact, and the hit on the real target
    ///         used to go through <see cref="Content.Shared._Trauma.Projectiles.PredictedProjectileSystem.DoHit"/>
    ///         alone - impact damage, no <c>TriggerOnCollide</c>, no explosion.
    ///
    ///     The target has to have <i>moved</i>, as it does in a real round: the ghost stands where the target was
    ///         one lag-window ago, on the rocket's line, while the real mob is two metres off it. With a stationary
    ///         target the two overlap, and the moment the ghost is spent the rocket touches the real mob in the same
    ///         tick and detonates on that ordinary collision - which is why a moving target is what turned the bug
    ///         up in play, and why a stationary one would make this test pass with the bug present.
    ///
    ///     Integration-test channels report a ping of zero, so firing never spawns ghosts by itself; the test
    ///         asks for them explicitly and asserts they exist, so it cannot pass on the ordinary physics path.
    /// </summary>
    [TestCase("WeaponLauncherRocket")]
    [TestCase("KsWeaponLauncherRocketAt4")]
    public async Task LagCompensatedHitDetonates(string launcherPrototype)
    {
        await PrepareRange(FloorSteel);

        var onLineCoordinates = CoordinatesAt(3.5f, 0.5f);
        var offLineCoordinates = CoordinatesAt(3.5f, 2.5f);

        var targetUid = ToServer(await SpawnTarget(MobHuman, onLineCoordinates));

        await ReadyLauncher(launcherPrototype);

        // Lag compensation records a position on every move and rewinds to the first one at or after
        //      (now - lag), so the target has to keep moving on the line right up to the shot to leave a history
        //      that places it there. A hair either side of the same spot is enough to raise a MoveEvent.
        for (var tick = 0; tick < 45; tick++)
        {
            var nudge = tick % 2 == 0 ? 0.01f : -0.01f;
            await Server.WaitPost(() =>
                Transform.SetCoordinates(targetUid, SEntMan.GetCoordinates(onLineCoordinates).Offset(new Vector2(0f, nudge))));
            await RunTicks(1);
        }

        var lagCompProjectileSystem = SEntMan.System<LagCompProjectileSystem>();
        var heatBefore = FixedPoint2.Zero;
        var bluntBefore = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            heatBefore = GetDamage(targetUid, Heat);
            bluntBefore = GetDamage(targetUid, Blunt);

            // Dodged, as far as the server's present is concerned - but not as far as the shooter saw it.
            Transform.SetCoordinates(targetUid, SEntMan.GetCoordinates(offLineCoordinates));

            var rocketUid = Shoot(SEntMan.GetCoordinates(onLineCoordinates), targetUid);

            lagCompProjectileSystem.CompensateProjectile(rocketUid, SPlayer, TimeSpan.FromMilliseconds(500));

            Assert.That(SEntMan.TryGetComponent<LagCompensatingProjectileComponent>(rocketUid, out var lagCompensatingComponent) &&
                        lagCompensatingComponent.IgnoredRealTargets.Contains(targetUid),
                "no ghost was spawned for the target, so this test is not exercising the lag-compensated hit at all");

            var ghostPosition = Transform.GetWorldPosition(lagCompensatingComponent!.Ghosts.Single());
            var onLinePosition = Transform.ToMapCoordinates(SEntMan.GetCoordinates(onLineCoordinates)).Position;
            Assert.That(Vector2.Distance(ghostPosition, onLinePosition), Is.LessThan(0.1f),
                "the ghost was not rewound onto the rocket's line, so it cannot be what the rocket hits");
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            // Told apart so a failure says which half broke: the impact is Blunt only, so Heat can only have come
            //      from the explosion.
            Assert.That(GetDamage(targetUid, Blunt) - bluntBefore, Is.GreaterThan(FixedPoint2.Zero),
                "the rocket never hit the target at all, through its ghost or otherwise");
            Assert.That(GetDamage(targetUid, Heat) - heatBefore, Is.GreaterThan(ExplosionHeatThreshold),
                "the rocket hit the target through its lag-compensation ghost but never detonated");
        });
    }

    /// <summary>
    ///     Control for <see cref="LagCompensatedHitDetonates"/>: the same shot on the ordinary physics path, with no
    ///         ghost, so a failure there can be told apart from the explosion itself being broken.
    /// </summary>
    [TestCase("WeaponLauncherRocket")]
    [TestCase("KsWeaponLauncherRocketAt4")]
    public async Task DirectHitDetonates(string launcherPrototype)
    {
        await PrepareRange(FloorSteel);

        var targetUid = ToServer(await SpawnTarget(MobHuman, CoordinatesAt(3.5f, 0.5f)));

        await ReadyLauncher(launcherPrototype);

        var heatBefore = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            heatBefore = GetDamage(targetUid, Heat);

            var rocketUid = Shoot(SEntMan.GetCoordinates(CoordinatesAt(3.5f, 0.5f)), targetUid);
            Assert.That(SEntMan.HasComponent<LagCompensatingProjectileComponent>(rocketUid), Is.False);
        });

        await RunSeconds(1f);

        Assert.That(GetDamage(targetUid, Heat) - heatBefore, Is.GreaterThan(ExplosionHeatThreshold),
            "the rocket hit the target directly but never detonated");
    }

    #endregion

    #region Backblast

    /// <summary>
    ///     Layout, in tiles, with the shooter at (0, 0) firing east:
    ///     <code>
    ///         . . S . . .     S - side control, straight north of the shooter: 90 degrees off the cone's axis
    ///         . B P F . .     B - behind, 1m straight back: inside the cone
    ///         . . . . . .     P - player, F - front control, diagonally ahead and clear of the rocket's path
    ///     </code>
    ///     The push, damage and knockdown all happen inside the gunshot event, so they are read in the same server
    ///         tick as the shot - before friction, standing up, or anything in the surroundings can move them.
    /// </summary>
    [Test]
    public async Task BackblastHitsOnlyTheConeBehind()
    {
        await PrepareRange(FloorSteel);

        var behindUid = ToServer(await Spawn(MobHuman, CoordinatesAt(-0.5f, 0.5f)));
        var sideUid = ToServer(await Spawn(MobHuman, CoordinatesAt(0.5f, 1.5f)));
        var frontUid = ToServer(await Spawn(MobHuman, CoordinatesAt(1.5f, 1.5f)));

        await ReadyLauncher(At4AlwaysBreaksTiles);

        var behindStartX = 0f;

        await Server.WaitAssertion(() =>
        {
            var controls = new[] { (sideUid, "beside"), (frontUid, "in front of"), (SPlayer, "firing as") };

            behindStartX = Transform.GetWorldPosition(behindUid).X;
            var behindDamageBefore = GetDamage(behindUid);
            var controlDamageBefore = controls.ToDictionary(control => control.Item1, control => GetDamage(control.Item1));

            // Far off to the east, with nothing in the way - the rocket must not detonate anywhere near the test.
            Shoot(SEntMan.GetCoordinates(CoordinatesAt(40.5f, 0.5f)), target: null);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(behindUid).LinearVelocity.X, Is.LessThan(0f),
                    "the mob behind the shooter was not pushed backwards");
                Assert.That(GetDamageIncrease(behindUid, behindDamageBefore), Is.Not.Empty,
                    "the mob behind the shooter was not damaged");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(behindUid),
                    "the mob behind the shooter was not knocked down");

                foreach (var (controlUid, name) in controls)
                {
                    Assert.That(SEntMan.GetComponent<PhysicsComponent>(controlUid).LinearVelocity, Is.EqualTo(Vector2.Zero),
                        $"the mob {name} the shooter was pushed");
                    Assert.That(GetDamageIncrease(controlUid, controlDamageBefore[controlUid]), Is.Empty,
                        $"the mob {name} the shooter was damaged");
                    Assert.That(SEntMan.HasComponent<KnockedDownComponent>(controlUid), Is.False,
                        $"the mob {name} the shooter was knocked down");
                }
            });
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Transform.GetWorldPosition(behindUid).X, Is.LessThan(behindStartX),
                    "the mob behind the shooter did not actually move backwards");

                // Inside the cone: the tile straight behind, and the shooter's own tile, which the sector starts in.
                Assert.That(GetTileId(-1, 0), Is.Not.EqualTo(FloorSteel), "the tile behind the shooter was not broken");
                Assert.That(GetTileId(0, 0), Is.Not.EqualTo(FloorSteel), "the shooter's own tile was not broken");

                // Outside it: ahead of the shooter, and behind but beyond the radius.
                Assert.That(GetTileId(1, 0), Is.EqualTo(FloorSteel), "the tile in front of the shooter was broken");
                Assert.That(GetTileId(2, 0), Is.EqualTo(FloorSteel), "a tile further ahead was broken");
                Assert.That(GetTileId(-3, 0), Is.EqualTo(FloorSteel), "a tile behind the shooter but out of range was broken");
            });
        });
    }

    /// <summary>
    ///     The push, damage and knockdown are unconditional; only tile breaking is rolled.
    /// </summary>
    [Test]
    public async Task BackblastWithZeroTilebreakChanceStillHits()
    {
        await PrepareRange(FloorSteel);

        var behindUid = ToServer(await Spawn(MobHuman, CoordinatesAt(-0.5f, 0.5f)));

        await ReadyLauncher(At4NeverBreaksTiles);

        await Server.WaitAssertion(() =>
        {
            var behindDamageBefore = GetDamage(behindUid);

            Shoot(SEntMan.GetCoordinates(CoordinatesAt(40.5f, 0.5f)), target: null);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(behindUid).LinearVelocity.X, Is.LessThan(0f),
                    "the mob behind the shooter was not pushed backwards");
                Assert.That(GetDamageIncrease(behindUid, behindDamageBefore), Is.Not.Empty,
                    "the mob behind the shooter was not damaged");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(behindUid),
                    "the mob behind the shooter was not knocked down");
            });
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetTileId(-1, 0), Is.EqualTo(FloorSteel), "a tile broke with a tilebreak chance of zero");
                Assert.That(GetTileId(0, 0), Is.EqualTo(FloorSteel), "a tile broke with a tilebreak chance of zero");
            });
        });
    }

    #endregion

    #region Helpers

    /// <summary>
    ///     Floors a 13x5 area around the shooter and gives it air, and gravity - without which every mob is
    ///         weightless, and <c>GravityAffectedComponent</c> refuses to let a weightless mob be knocked down.
    /// </summary>
    private async Task PrepareRange(string tilePrototype)
    {
        var tile = new Tile(TileMan[tilePrototype].TileId);

        await Server.WaitPost(() =>
        {
            for (var x = -4; x <= 8; x++)
            {
                for (var y = -2; y <= 2; y++)
                    MapSystem.SetTile(MapData.Grid, new Vector2i(x, y), tile);
            }
        });

        await AddAtmosphere();
        await AddGravity();
        await RunTicks(5);
    }

    /// <summary>
    ///     Puts the launcher in the player's hands, wields it - unwielded spread is wide enough to miss a mob at
    ///         a few metres - and waits out the pickup cooldown.
    /// </summary>
    private async Task ReadyLauncher(string launcherPrototype)
    {
        await PlaceInHands(launcherPrototype);
        await UseInHand();
        await SetCombatMode(true);
        await RunSeconds(2f);
    }

    /// <summary>
    ///     Fires the held gun and returns the projectile it spawned, before physics has stepped it.
    ///         Must run on the server thread.
    /// </summary>
    private EntityUid Shoot(EntityCoordinates toCoordinates, EntityUid? target)
    {
        Assert.That(SGun.TryGetGun(SPlayer, out var gun), "the player is not holding a gun");
        Assert.That(SGun.AttemptShoot(SPlayer, gun, toCoordinates, target), "the gun failed to fire");

        var projectileEnumerator = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
        var projectileUids = new List<EntityUid>();
        while (projectileEnumerator.MoveNext(out var projectileUid, out var projectileComponent))
        {
            if (projectileComponent.Shooter == SPlayer)
                projectileUids.Add(projectileUid);
        }

        Assert.That(projectileUids, Has.Count.EqualTo(1), "expected exactly one projectile from the shot");
        return projectileUids.Single();
    }

    private NetCoordinates CoordinatesAt(float x, float y)
    {
        return SEntMan.GetNetCoordinates(Transform.WithEntityId(MapData.GridCoords.Offset(new Vector2(x, y)), MapData.MapUid));
    }

    private string GetTileId(int x, int y)
    {
        var tileRef = MapSystem.GetTileRef(MapData.Grid, new Vector2i(x, y));
        return TileMan[tileRef.Tile.TypeId].ID;
    }

    private Dictionary<string, FixedPoint2> GetDamage(EntityUid uid)
    {
        var damageableComponent = SEntMan.GetComponent<DamageableComponent>(uid);
        return SEntMan.System<DamageableSystem>()
            .GetPositiveDamage((uid, damageableComponent))
            .DamageDict
            .ToDictionary(pair => pair.Key.Id, pair => pair.Value);
    }

    private FixedPoint2 GetDamage(EntityUid uid, string damageType)
    {
        return GetDamage(uid).TryGetValue(damageType, out var damage) ? damage : FixedPoint2.Zero;
    }

    /// <summary>
    ///     Every damage type that went up since <paramref name="damageBefore"/>, as "Type: +amount" - empty if none
    ///         did. A list rather than a bool, so a failing assertion prints what the mob took.
    /// </summary>
    private List<string> GetDamageIncrease(EntityUid uid, Dictionary<string, FixedPoint2> damageBefore)
    {
        var increases = new List<string>();
        foreach (var (damageType, damage) in GetDamage(uid))
        {
            var increase = damage - damageBefore.GetValueOrDefault(damageType);
            if (increase > FixedPoint2.Zero)
                increases.Add($"{damageType}: +{increase}");
        }

        return increases;
    }

    #endregion
}
