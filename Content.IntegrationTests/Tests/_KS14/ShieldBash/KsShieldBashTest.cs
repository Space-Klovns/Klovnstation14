#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.ShieldBash;
using Content.Shared._KS14.ShoveWeapon;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Timing;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ShieldBash;

[TestFixture]
[TestOf(typeof(KsShieldBashSystem))]
[TestOf(typeof(KsShoveWeaponSystem))]
public sealed class KsShieldBashTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Shield = "RiotShield";
    private static readonly EntProtoId EnergyShield = "EnergyShield";
    private static readonly EntProtoId Baton = "Stunbaton";
    private static readonly EntProtoId Wrench = "Wrench";

    [Test]
    public async Task MeleeWeaponBashesShieldWithCooldown()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity(Human, testMap.GridCoords);
            var shieldUid = SEntMan.SpawnEntity(Shield, testMap.GridCoords);
            var batonUid = SEntMan.SpawnEntity(Baton, testMap.GridCoords);
            var wrenchUid = SEntMan.SpawnEntity(Wrench, testMap.GridCoords);
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            var interactionSystem = SEntMan.System<SharedInteractionSystem>();
            var useDelaySystem = SEntMan.System<UseDelaySystem>();
            Assert.That(handsSystem.TryPickupAnyHand(userUid, shieldUid));

            interactionSystem.InteractUsing(userUid, wrenchUid, shieldUid, testMap.GridCoords, checkCanInteract: false);
            Assert.That(useDelaySystem.IsDelayed(shieldUid, KsShieldBashSystem.BashDelayId), Is.False, "A tool must not bash the shield.");

            interactionSystem.InteractUsing(userUid, batonUid, shieldUid, testMap.GridCoords, checkCanInteract: false);
            Assert.That(useDelaySystem.IsDelayed(shieldUid, KsShieldBashSystem.BashDelayId), Is.True, "A baton must bash the shield.");
            Assert.That(SEntMan.HasComponent<JitteringComponent>(userUid), Is.True, "Bashing must shake the basher.");
        });
    }

    [Test]
    public async Task ShieldShovesNeverMissAndEnergyShieldsMustBeOn()
    {
        await Server.WaitAssertion(() =>
        {
            var shieldUid = SEntMan.SpawnEntity(Shield, MapCoordinates.Nullspace);
            var energyShieldUid = SEntMan.SpawnEntity(EnergyShield, MapCoordinates.Nullspace);
            var userUid = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var targetUid = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var defaultSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

            var shove = new KsDisarmWithWeaponAttemptEvent(userUid, targetUid, 0.7f, defaultSound);
            SEntMan.EventBus.RaiseLocalEvent(shieldUid, ref shove);
            Assert.That(shove.Cancelled, Is.False);
            Assert.That(shove.Chance, Is.EqualTo(0f), "A shield shove must never fail.");
            Assert.That(shove.SuccessSound, Is.Not.SameAs(defaultSound), "A shield shove must sound like one.");

            var offShove = new KsDisarmWithWeaponAttemptEvent(userUid, targetUid, 0.7f, defaultSound);
            SEntMan.EventBus.RaiseLocalEvent(energyShieldUid, ref offShove);
            Assert.That(offShove.Cancelled, Is.True, "An energy shield that is off must not shove.");
        });
    }
}
