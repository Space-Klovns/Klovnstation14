#nullable enable
using System.Linq;
using Content.Client.Items;
using Content.Client.Weapons.Ranged.Components;
using Content.Client.Weapons.Ranged.ItemStatus;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     The ammo counter of a gun in the player's hand follows a change the server made to its ammo, which the client
///         never predicted (a resupply, an admin's edit), without the gun being picked up again: a magazine's rounds
///         inside the gun, and a battery gun's charge.
/// </summary>
[TestOf(typeof(SharedGunSystem))]
public sealed class KsAmmoCounterStateTest : InteractionTest
{
    private const string Rifle = "KsAmmoCounterStateTestRifle";
    private const string LaserGun = "KsAmmoCounterStateTestLaserGun";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: KsAmmoCounterStateTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: BulletPistol
    deleteOnSpawn: true

- type: entity
  parent: BaseItem
  id: KsAmmoCounterStateTestMagazine
  components:
  - type: BallisticAmmoProvider
    proto: KsAmmoCounterStateTestCartridge
    capacity: 8
    whitelist:
      components:
      - CartridgeAmmo
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

- type: entity
  parent: BaseItem
  id: {Rifle}
  components:
  - type: AmmoCounter
  - type: Gun
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: MagazineAmmoProvider
  - type: ItemSlots
    slots:
      gun_magazine:
        name: Magazine
        startingItem: KsAmmoCounterStateTestMagazine
        whitelist:
          components:
          - BallisticAmmoProvider
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot

- type: entity
  parent: BaseItem
  id: {LaserGun}
  components:
  - type: AmmoCounter
  - type: Gun
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BatteryAmmoProvider
    proto: BulletPistol
    fireCost: 100
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
";

    /// <summary>
    ///     The count the client's counter control for <paramref name="clientGunUid"/> shows. <paramref name="rebuild"/>
    ///         builds it anew first, as the hands' item status panel does when the gun is picked up.
    /// </summary>
    private int CounterShows(EntityUid clientGunUid, bool rebuild = false) => CounterShows(CEntMan, clientGunUid, rebuild);

    /// <inheritdoc cref="CounterShows(EntityUid, bool)"/>
    internal static int CounterShows(IEntityManager clientEntityManager, EntityUid clientGunUid, bool rebuild = false)
    {
        var counterComponent = clientEntityManager.GetComponent<AmmoCounterComponent>(clientGunUid);
        if (rebuild || counterComponent.Control == null)
            clientEntityManager.EventBus.RaiseLocalEvent(clientGunUid, new ItemStatusCollectMessage(), broadcast: true);

        Assert.That(counterComponent.Control, Is.Not.Null, "the gun should have an ammo counter control");
        var bullets = Descendants(counterComponent.Control!).OfType<BaseBulletRenderer>().FirstOrDefault();
        Assert.That(bullets, Is.Not.Null, "the counter should draw the rounds");
        return bullets!.Count;
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    [Test]
    public async Task CounterFollowsTheServersChangeToAMagazineInTheGun()
    {
        var gunNetEntity = await PlaceInHands(Rifle);
        await RunTicks(5);

        var clientGunUid = ToClient(gunNetEntity);
        EntityUid magazineUid = default;
        var capacity = 0;
        await Server.WaitPost(() =>
        {
            var slot = (ContainerSlot)SEntMan.System<SharedContainerSystem>().GetContainer(ToServer(gunNetEntity), SharedGunSystem.MagazineSlot);
            magazineUid = slot.ContainedEntity!.Value;
            capacity = SEntMan.GetComponent<BallisticAmmoProviderComponent>(magazineUid).Capacity;
        });

        await Client.WaitAssertion(() => Assert.That(CounterShows(clientGunUid, rebuild: true), Is.EqualTo(capacity), "a full magazine to start"));

        foreach (var rounds in new[] { capacity / 2, capacity })
        {
            await Server.WaitPost(() =>
            {
                var magazine = SEntMan.GetComponent<BallisticAmmoProviderComponent>(magazineUid);
                SGun.SetBallisticUnspawned((magazineUid, magazine), rounds);
            });
            await RunTicks(5);

            await Client.WaitAssertion(() => Assert.That(CounterShows(clientGunUid), Is.EqualTo(rounds),
                "the counter should show the rounds the server put in the magazine, without the gun being picked up again"));
        }
    }

    [Test]
    public async Task CounterFollowsTheServersChangeToABatteryGun()
    {
        var gunNetEntity = await PlaceInHands(LaserGun);
        await RunTicks(5);

        var clientGunUid = ToClient(gunNetEntity);
        var capacity = 0;
        await Server.WaitPost(() =>
        {
            var gunUid = ToServer(gunNetEntity);
            capacity = SGun.GetShots((gunUid, SEntMan.GetComponent<BatteryAmmoProviderComponent>(gunUid))).Item2;
        });
        Assert.That(capacity, Is.GreaterThan(0));

        await Client.WaitAssertion(() => Assert.That(CounterShows(clientGunUid, rebuild: true), Is.EqualTo(capacity), "a full battery to start"));

        foreach (var fraction in new[] { 0f, 1f })
        {
            var shots = 0;
            await Server.WaitPost(() =>
            {
                var gunUid = ToServer(gunNetEntity);
                var battery = SEntMan.GetComponent<BatteryComponent>(gunUid);
                SEntMan.System<SharedBatterySystem>().SetCharge((gunUid, battery), battery.MaxCharge * fraction);
                shots = SEntMan.GetComponent<BatteryAmmoProviderComponent>(gunUid).Shots;
            });
            await RunTicks(5);

            await Client.WaitAssertion(() => Assert.That(CounterShows(clientGunUid), Is.EqualTo(shots),
                "the counter should show the shots the server's charge leaves, without the gun being picked up again"));
        }
    }
}
