#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     Where to shoot is the client's word, and the server takes none of it that is not a place: a request aimed at
///         coordinates that are not finite, or on nothing, fires nothing. A position's NaN is not flushed on the way in
///         (only plain float fields are), so it arrives as sent, and went on into the shot's direction, the
///         projectile's velocity and every lookup along the ray.
/// </summary>
[TestOf(typeof(SharedGunSystem))]
public sealed class KsShootRequestValidationTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsShootRequestValidationTestGun";
    private const string TestLaser = "KsShootRequestValidationTestLaser";
    private const string TestRay = "KsShootRequestValidationTestRay";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10

- type: entity
  id: {TestRay}
  components:
  - type: HitscanAmmo
  - type: HitscanBasicRaycast
  - type: HitscanBasicDamage
    damage:
      types:
        Piercing: 10

- type: entity
  id: {TestLaser}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: {TestRay}
    capacity: 10
    count: 10
";

    public enum BadAim
    {
        NaN,
        Infinity,
        NoSuchEntity,
    }

    /// <summary>
    ///     The rounds left in the gun on the server: one fewer for every shot it fired, whatever the shot was.
    /// </summary>
    private async Task<int> ServerRoundsLeft(NetEntity gunNetEntity)
    {
        var roundsLeft = 0;
        await Server.WaitPost(() =>
        {
            var ammoProviderComponent = SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(SEntMan.GetEntity(gunNetEntity));
            roundsLeft = ammoProviderComponent.Count ?? ammoProviderComponent.Capacity ?? 0;
        });
        return roundsLeft;
    }

    private async Task<int> ServerProjectiles()
    {
        var count = 0;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<ProjectileComponent>();
            while (query.MoveNext(out _, out var projectileComponent))
            {
                if (projectileComponent.Shooter == SPlayer)
                    count++;
            }
        });
        return count;
    }

    /// <summary>
    ///     A request aimed nowhere fires nothing, for a gun that shoots bullets and one that shoots rays; one aimed
    ///         somewhere, straight after, fires.
    /// </summary>
    [Test]
    public async Task RequestAimedNowhereFiresNothing([Values(TestGun, TestLaser)] string gun, [Values] BadAim aim)
    {
        var gunNetEntity = await PlaceInHands(gun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var roundsBefore = await ServerRoundsLeft(gunNetEntity);
        var mapNetEntity = SEntMan.GetNetEntity(MapData.MapUid);
        var badCoordinates = aim switch
        {
            BadAim.NaN => new NetCoordinates(mapNetEntity, new Vector2(float.NaN, float.NaN)),
            BadAim.Infinity => new NetCoordinates(mapNetEntity, new Vector2(float.PositiveInfinity, 0f)),
            _ => new NetCoordinates(NetEntity.Invalid, new Vector2(5f, 0f)),
        };

        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = badCoordinates,
            });
        });
        await RunTicks(15); // well past the round trip

        Assert.That(await ServerRoundsLeft(gunNetEntity), Is.EqualTo(roundsBefore), "the server fired at coordinates that are no place");
        Assert.That(await ServerProjectiles(), Is.EqualTo(0), "the server shot a projectile at coordinates that are no place");

        // And the gun still works.
        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() => shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer));
        var goodCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = goodCoordinates,
            });
        });
        await RunTicks(15);

        Assert.That(await ServerRoundsLeft(gunNetEntity), Is.LessThan(roundsBefore), "the server did not fire a request aimed at a real place, so this proves nothing");
    }
}
