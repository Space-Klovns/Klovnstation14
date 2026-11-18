#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Battlefield14.Weapons;

/// <summary>
///     <see cref="ChamberMagazineAmmoProviderComponent.AutoOpenBoltOnEmpty"/>: firing the last round locks the bolt back,
///         unless the gun turns that off.
/// </summary>
[TestFixture]
public sealed class BF14AutoOpenBoltTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeaponRifleAk
  id: BF14TestRifleBoltStaysClosed
  components:
  - type: ChamberMagazineAmmoProvider
    autoOpenBoltOnEmpty: false
";

    private static readonly EntProtoId StockRifle = "WeaponRifleAk";
    private static readonly EntProtoId EmptyMagazine = "MagazineLightRifleEmpty";
    private static readonly EntProtoId Cartridge = "CartridgeLightRifle";
    private readonly EntProtoId _boltStaysClosedRifle = "BF14TestRifleBoltStaysClosed";

    private const string MagazineSlot = "gun_magazine";
    private const string ChamberSlot = "gun_chamber";

    [Test]
    public async Task StockRifleLocksItsBoltBack()
    {
        Assert.That(await FireLastRound(StockRifle), Is.False, "the stock rifle's bolt stayed closed on an empty gun");
    }

    [Test]
    public async Task BoltStaysClosedWhenTurnedOff()
    {
        Assert.That(await FireLastRound(_boltStaysClosedRifle), Is.True, "the bolt locked back with autoOpenBoltOnEmpty off");
    }

    /// <summary>
    ///     Loads a rifle with one chambered round over an empty magazine, fires it, and returns whether the bolt is
    ///         closed afterwards.
    /// </summary>
    private async Task<bool?> FireLastRound(EntProtoId rifleProto)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();
        var gunSystem = server.System<SharedGunSystem>();

        EntityUid rifleUid = default;
        await server.WaitPost(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            rifleUid = SSpawnAtPosition(rifleProto, origin);

            if (itemSlotsSystem.GetItemOrNull(rifleUid, MagazineSlot) is { } fullMagazineUid)
                SEntMan.DeleteEntity(fullMagazineUid);
            if (itemSlotsSystem.GetItemOrNull(rifleUid, ChamberSlot) is { } chamberedUid)
                SEntMan.DeleteEntity(chamberedUid);

            Assert.That(itemSlotsSystem.TryInsert(rifleUid, MagazineSlot, SSpawnAtPosition(EmptyMagazine, origin), user: null), Is.True);
            Assert.That(itemSlotsSystem.TryInsert(rifleUid, ChamberSlot, SSpawnAtPosition(Cartridge, origin), user: null), Is.True);

            var ammoProvider = SEntMan.GetComponent<ChamberMagazineAmmoProviderComponent>(rifleUid);
            gunSystem.SetBoltClosed(rifleUid, ammoProvider, true);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(gunSystem.GetAmmoCount(rifleUid), Is.EqualTo(1), "the rifle should hold only its chambered round");
            Assert.That(gunSystem.AttemptShoot((rifleUid, SEntMan.GetComponent<GunComponent>(rifleUid))), Is.True, "the rifle did not fire");
        });

        await Pair.RunTicksSync(5);

        bool? boltClosed = null;
        await server.WaitPost(() =>
        {
            Assert.That(gunSystem.GetAmmoCount(rifleUid), Is.Zero, "the round was not fired");
            boltClosed = SEntMan.GetComponent<ChamberMagazineAmmoProviderComponent>(rifleUid).BoltClosed;
        });

        return boltClosed;
    }
}
