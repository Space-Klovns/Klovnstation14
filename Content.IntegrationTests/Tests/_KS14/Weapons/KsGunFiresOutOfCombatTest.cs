#nullable enable
using System.Numerics;
using Content.Client._KS14.Weapons.Ranged;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     A gun with <see cref="KsGunFiresOutOfCombatComponent"/>, the fire extinguisher, fires only out of combat mode,
///         while an ordinary gun still fires only in it. Shots go through the request a real client sends.
/// </summary>
[TestOf(typeof(KsGunFiresOutOfCombatComponent))]
public sealed class KsGunFiresOutOfCombatTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands and combat mode, to fire a gun

    private static readonly EntProtoId Extinguisher = "FireExtinguisher";
    private static readonly EntProtoId OrdinaryGun = "WeaponLaserGun";

    private static readonly EntProtoId WaterTank = "WaterTankFull";
    private static readonly EntProtoId Backpack = "ClothingBackpack";
    private static readonly EntProtoId ExtinguisherCabinet = "ExtinguisherCabinet";
    private static readonly EntProtoId Wall = "WallSolid";

    [Test]
    public async Task ExtinguisherFiresOutOfCombatMode()
    {
        Assert.That(await TryShoot(Extinguisher, inCombatMode: false), Is.True,
            "The extinguisher didn't fire out of combat mode.");
    }

    [Test]
    public async Task ExtinguisherDoesNotFireInCombatMode()
    {
        Assert.That(await TryShoot(Extinguisher, inCombatMode: true), Is.False,
            "The extinguisher fired in combat mode, where it should only melee.");
    }

    /// <summary>
    ///     Controls for the two above: an ordinary gun is unaffected, so the inversion is the component's and not every gun's.
    /// </summary>
    [Test]
    public async Task OrdinaryGunStillNeedsCombatMode([Values] bool inCombatMode)
    {
        Assert.That(await TryShoot(OrdinaryGun, inCombatMode), Is.EqualTo(inCombatMode),
            inCombatMode ? "An ordinary gun didn't fire in combat mode." : "An ordinary gun fired out of combat mode.");
    }

    /// <summary>
    ///     A click out of combat mode on what the extinguisher is used on, rather than fired at, holds its fire: tanks to
    ///         refill from, storage and slots to put it in. Anything else, like a wall, doesn't.
    /// </summary>
    [Test]
    public async Task ClickInteractsWithRefillsAndStorageOnly()
    {
        var waterTankNetEntity = await Spawn(WaterTank);
        var backpackNetEntity = await Spawn(Backpack);
        var cabinetNetEntity = await Spawn(ExtinguisherCabinet);
        var wallNetEntity = await Spawn(Wall);

        await Client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ClickInteracts(waterTankNetEntity), Is.True, "A click on a water tank fires instead of refilling.");
                Assert.That(ClickInteracts(backpackNetEntity), Is.True, "A click on a backpack fires instead of storing.");
                Assert.That(ClickInteracts(cabinetNetEntity), Is.True, "A click on a cabinet fires instead of slotting.");
                Assert.That(ClickInteracts(wallNetEntity), Is.False, "A click on a wall holds fire.");
            }
        });
    }

    private bool ClickInteracts(NetEntity targetNetEntity)
    {
        var ev = new KsGunClickInteractsEvent();
        CEntMan.EventBus.RaiseLocalEvent(CEntMan.GetEntity(targetNetEntity), ref ev);
        return ev.Interacts;
    }

    private async Task<NetEntity> Spawn(EntProtoId prototype)
    {
        var netEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var uid = SEntMan.SpawnEntity(prototype, new EntityCoordinates(MapData.Grid.Owner, new Vector2(1.5f, 0.5f)));
            netEntity = SEntMan.GetNetEntity(uid);
        });
        await RunTicks(5);
        return netEntity;
    }

    /// <summary>
    ///     Asks to shoot <paramref name="gunPrototype"/> once, held in the player's hand, and says whether the server fired it.
    /// </summary>
    private async Task<bool> TryShoot(EntProtoId gunPrototype, bool inCombatMode)
    {
        var gunNetEntity = await PlaceInHands(gunPrototype); // and takes an extinguisher's safety off
        await SetCombatMode(inCombatMode);
        await RunSeconds(2f); // the pickup cooldown

        var gunUid = SEntMan.GetEntity(gunNetEntity);
        var nextFireBefore = SEntMan.GetComponent<GunComponent>(gunUid).NextFire;

        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid.Owner, new Vector2(10.5f, 0.5f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });
        await RunTicks(5);

        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestStopShootEvent { Gun = gunNetEntity }));
        await RunTicks(2);

        return SEntMan.GetComponent<GunComponent>(gunUid).NextFire > nextFireBefore;
    }
}
