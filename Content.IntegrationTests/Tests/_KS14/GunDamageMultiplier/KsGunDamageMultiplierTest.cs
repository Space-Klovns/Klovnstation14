#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.GunDamageMultiplier;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.GunDamageMultiplier;

/// <summary>
///     <see cref="KsGunDamageMultiplierComponent"/> on a gun that fires cartridges: the bullet is spawned by the
///         cartridge, so it is the bullet, not the cartridge, that has to come out scaled.
///
///     Every expectation is read from the bullet's own prototype, so retuning the rounds doesn't break the test.
/// </summary>
[TestOf(typeof(KsGunDamageMultiplierSystem))]
public sealed class KsGunDamageMultiplierTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string RegalCondor = "KsWeaponPistolRegalCondor";
    private const string DesertEagle = "KsWeaponPistolDeagle";

    [Test]
    public async Task RegalCondorBulletIsScaled()
    {
        var (bulletDamage, prototypeDamage, multiplier) = await FireAndMeasure(RegalCondor);

        Assert.That(multiplier, Is.Not.Null, "the Regal Condor has no damage multiplier at all");
        Assert.That(multiplier!.Value, Is.GreaterThan(1f), "the Regal Condor's multiplier does nothing, so this test proves nothing");
        Assert.That(prototypeDamage, Is.GreaterThan(FixedPoint2.Zero), "the bullet's prototype deals no damage, so scaling it proves nothing");

        Assert.That(bulletDamage, Is.EqualTo(prototypeDamage * multiplier.Value),
            "the bullet the Regal Condor fired was not scaled by the gun's damage multiplier");
    }

    /// <summary>
    ///     Control: a gun without the component fires its bullet untouched.
    /// </summary>
    [Test]
    public async Task DesertEagleBulletIsNotScaled()
    {
        var (bulletDamage, prototypeDamage, multiplier) = await FireAndMeasure(DesertEagle);

        Assert.That(multiplier, Is.Null, "the Desert Eagle is the control and must have no damage multiplier");
        Assert.That(bulletDamage, Is.EqualTo(prototypeDamage), "a gun with no damage multiplier changed its bullet's damage");
    }

    /// <summary>
    ///     Fires one round from a freshly spawned gun. Returns the total damage of the bullet in flight, the total
    ///         its prototype has, and the gun's multiplier if it has one.
    /// </summary>
    private async Task<(FixedPoint2 BulletDamage, FixedPoint2 PrototypeDamage, float? Multiplier)> FireAndMeasure(string gunPrototype)
    {
        await PlaceInHands(gunPrototype);
        await UseInHand(); // racks the slide, chambering a round from the magazine
        await SetCombatMode(true);
        await RunSeconds(1f);

        var bulletDamage = FixedPoint2.Zero;
        var prototypeDamage = FixedPoint2.Zero;
        float? multiplier = null;

        await Server.WaitAssertion(() =>
        {
            Assert.That(SGun.TryGetGun(SPlayer, out var gun), "the player is not holding a gun");

            if (SEntMan.TryGetComponent<KsGunDamageMultiplierComponent>(gun.Owner, out var multiplierComponent))
                multiplier = multiplierComponent.Multiplier;

            var toCoordinates = Transform.WithEntityId(MapData.GridCoords.Offset(new Vector2(5f, 0f)), MapData.MapUid);
            Assert.That(SGun.AttemptShoot(SPlayer, gun, toCoordinates), "the gun failed to fire");

            var projectileUids = new List<EntityUid>();
            var projectileEnumerator = SEntMan.EntityQueryEnumerator<ProjectileComponent>();
            while (projectileEnumerator.MoveNext(out var projectileUid, out var projectileComponent))
            {
                if (projectileComponent.Shooter == SPlayer)
                    projectileUids.Add(projectileUid);
            }

            Assert.That(projectileUids, Is.Not.Empty, "the shot spawned no bullet");

            var bulletUid = projectileUids.First();
            bulletDamage = SEntMan.GetComponent<ProjectileComponent>(bulletUid).Damage.GetTotal();

            var bulletPrototype = SEntMan.GetComponent<MetaDataComponent>(bulletUid).EntityPrototype;
            Assert.That(bulletPrototype, Is.Not.Null, "the bullet has no prototype to compare against");
            Assert.That(bulletPrototype!.TryGetComponent<ProjectileComponent>(out var prototypeProjectile, SEntMan.ComponentFactory),
                "the bullet's prototype has no projectile component");
            prototypeDamage = prototypeProjectile!.Damage.GetTotal();
        });

        return (bulletDamage, prototypeDamage, multiplier);
    }
}
