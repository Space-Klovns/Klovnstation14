#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Battlefield14.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Battlefield14.Wieldable;

/// <summary>
///     <see cref="KsWieldDelayComponent"/>: a gun picked up can't be wielded until its delay has passed, and one just
///         wielded can't fire until it has passed again.
/// </summary>
[TestOf(typeof(KsWieldDelaySystem))]
public sealed class KsWieldDelayTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // The default test mob only has one hand

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeaponSniperMosin
  id: BF14TestWieldDelayRifle
  components:
  - type: KsWieldDelay
    baseDelay: 3
    preventFiring: true
";

    private static readonly EntProtoId MobHuman = "MobHuman";
    private readonly EntProtoId _rifle = "BF14TestWieldDelayRifle";

    /// <summary>
    ///     Longer than the test rifle's delay, and than the cooldown a gun has after it is picked up.
    /// </summary>
    private const float PastTheDelay = 3.5f;

    [Test]
    public async Task WieldAndFireWaitForTheDelay()
    {
        await AddAtmosphere(); // the target mob would suffocate otherwise
        await SpawnTarget(MobHuman);

        var rifleNet = await PlaceInHands(_rifle.Id);
        var rifleUid = ToServer(rifleNet);
        var wieldableComponent = Comp<WieldableComponent>(rifleNet);

        await UseInHand();
        Assert.That(wieldableComponent.Wielded, Is.False, "the rifle was wielded straight after it was picked up");

        await Pair.RunSeconds(PastTheDelay);
        await UseInHand();
        Assert.That(wieldableComponent.Wielded, Is.True, "the rifle could not be wielded once its delay had passed");

        var startAmmo = SGun.GetAmmoCount(rifleUid);
        await AttemptShoot(Target, assert: false);
        Assert.That(SGun.GetAmmoCount(rifleUid), Is.EqualTo(startAmmo), "the rifle fired straight after it was wielded");

        await Pair.RunSeconds(PastTheDelay);
        await AttemptShoot(Target);
        Assert.That(SGun.GetAmmoCount(rifleUid), Is.EqualTo(startAmmo - 1), "the rifle did not fire once its delay had passed");
    }
}
