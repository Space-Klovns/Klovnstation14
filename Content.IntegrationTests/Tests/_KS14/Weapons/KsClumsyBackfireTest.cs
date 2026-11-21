#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Clumsy;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     A clumsy shooter's gun blowing up in their face calls the whole shot off, leaving the gun untouched, and ends the
///         shooting; and a clumsy holder never clenches a trigger, which would backfire on every shot it fires (#448).
/// </summary>
[TestOf(typeof(ClumsySystem))]
public sealed class KsClumsyBackfireTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands, a mob state, and able to be stunned

    private readonly EntProtoId _bullet = "KsTestBackfireBullet";
    private readonly EntProtoId _cartridge = "KsTestBackfireCartridge";
    private const string BallisticGun = "KsTestBackfireBallisticGun";
    private const string BatteryGun = "KsTestBackfireBatteryGun";
    private const string FullAutoGun = "KsTestBackfireFullAutoGun";
    private const string ClumsyProofGun = "KsTestBackfireClumsyProofGun";

    private const int Capacity = 10;
    private const float FireRate = 10f;

    /// <summary>
    ///     Comfortably longer than the full-auto gun takes to empty.
    /// </summary>
    private const float EmptySeconds = Capacity / FireRate * 2f;

    private static readonly TimeSpan LongStun = TimeSpan.FromSeconds(30);

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseBullet
  id: KsTestBackfireBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 1

- type: entity
  parent: CartridgePistol
  id: KsTestBackfireCartridge
  components:
  - type: CartridgeAmmo
    proto: KsTestBackfireBullet

- type: entity
  parent: BaseItem
  id: KsTestBackfireBallisticGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: KsTestBackfireCartridge
    capacity: 10
    unspawnedCount: 10
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

- type: entity
  parent: BaseItem
  id: KsTestBackfireBatteryGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BatteryAmmoProvider
    proto: KsTestBackfireBullet
    fireCost: 100
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000

- type: entity
  parent: BaseItem
  id: KsTestBackfireFullAutoGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: FullAuto
    availableModes:
    - FullAuto
  - type: BasicEntityAmmoProvider
    proto: KsTestBackfireBullet
    capacity: 10
    count: 10

- type: entity
  parent: KsTestBackfireFullAutoGun
  id: KsTestBackfireClumsyProofGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: FullAuto
    availableModes:
    - FullAuto
    clumsyProof: true
";

    protected override async Task TearDown()
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(KsCCVars.GunTriggerSpasmChance, KsCCVars.GunTriggerSpasmChance.DefaultValue));
        await base.TearDown();
    }

    /// <summary>
    ///     The setup fires for real when the roll doesn't backfire, so the tests below see a shot called off and not one
    ///         that never happened.
    /// </summary>
    [Test]
    public async Task ShotThatDoesNotBackfireFires()
    {
        await MakeClumsy(0f);
        var gunUid = await HoldGun(BatteryGun);

        await ShootFromClient(gunUid);

        Assert.That(Count(_bullet), Is.EqualTo(1), "The shot didn't fire a bullet, so the setup is broken.");
    }

    [Test]
    public async Task BallisticBackfireLeavesTheGunUntouched()
    {
        await MakeClumsy(1f);
        var gunUid = await HoldGun(BallisticGun);

        await ShootFromClient(gunUid);

        await Server.WaitAssertion(() =>
        {
            var provider = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gunUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(provider.UnspawnedCount + provider.Entities.Count, Is.EqualTo(Capacity), "The backfire took a round.");
                Assert.That(Count(_cartridge), Is.Zero, "The backfire spawned or ejected a cartridge.");
                Assert.That(Count(_bullet), Is.Zero, "A backfire fired a bullet.");
            }
        });

        await AssertNoClientBullets();
    }

    /// <remarks>
    ///     Checked on the tick of the shot, as a bolt left lying about hits whatever it's touching on the next one: the
    ///         shooter, here.
    /// </remarks>
    [Test]
    public async Task BatteryBackfireLeavesTheGunUntouched()
    {
        await MakeClumsy(1f);
        var gunUid = await HoldGun(BatteryGun);
        var shotsBefore = SEntMan.GetComponent<BatteryAmmoProviderComponent>(gunUid).Shots;

        await Server.WaitAssertion(() =>
        {
            var fired = TryFire(gunUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fired, Is.False, "The shot went off.");
                Assert.That(SEntMan.GetComponent<BatteryAmmoProviderComponent>(gunUid).Shots, Is.EqualTo(shotsBefore),
                    "The backfire cost charge.");
                Assert.That(Count(_bullet), Is.Zero, "The backfire spawned a bolt.");
            }
        });
        await RunTicks(5);

        await AssertNoClientBullets();
    }

    /// <summary>
    ///     The backfire's stun used to clench the shooter's hand on the trigger they were still holding, and the gun
    ///         kept firing, each shot backfiring and stunning them again, until it was empty.
    /// </summary>
    [Test]
    public async Task BackfireDoesNotClenchTheTrigger()
    {
        await SetSpasmChance(1f);
        await MakeClumsy(1f);
        var gunUid = await HoldGun(FullAutoGun);

        var fired = true;
        await Server.WaitPost(() => fired = TryFire(gunUid));
        await RunSeconds(EmptySeconds);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fired, Is.False, "The shot went off.");
                Assert.That(IsSpasming(gunUid), Is.False, "Going down to a backfire clenched the trigger.");
                Assert.That(Ammo(gunUid), Is.EqualTo(Capacity), "The gun fired.");
                Assert.That(IsHeld(gunUid), Is.False, "The backfire didn't knock the gun out of the shooter's hand.");
            }
        });
    }

    /// <summary>
    ///     A shooter already down and clenching the trigger who turns clumsy: the first backfire ends it.
    /// </summary>
    [Test]
    public async Task BackfireEndsAClenchedTrigger()
    {
        await SetSpasmChance(1f);
        await MakeClumsy(0f);
        var gunUid = await HoldGun(FullAutoGun);

        await Server.WaitPost(() =>
        {
            Fire(gunUid);
            SEntMan.System<SharedStunSystem>().TryUpdateParalyzeDuration(SPlayer, LongStun);
        });
        await RunTicks(1);
        Assert.That(IsSpasming(gunUid), Is.True, "Going down with the trigger held didn't clench it.");

        await MakeClumsy(1f);
        var ammoWhenClumsy = Ammo(gunUid);
        await RunSeconds(EmptySeconds);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsSpasming(gunUid), Is.False, "The clenched trigger outlasted a backfire.");
                Assert.That(Ammo(gunUid), Is.EqualTo(ammoWhenClumsy), "The clenched trigger fired while clumsy.");
                Assert.That(IsHeld(gunUid), Is.False, "The backfire didn't knock the gun out of the shooter's hand.");
            }
        });
    }

    /// <summary>
    ///     Someone whose clumsiness could blow the gun up never clenches its trigger, and anyone else still does. Made
    ///         clumsy after the shot, as that shot would otherwise backfire before they went down.
    /// </summary>
    [TestCase(false, FullAutoGun, ExpectedResult = true)]
    [TestCase(true, FullAutoGun, ExpectedResult = false)]
    [TestCase(true, ClumsyProofGun, ExpectedResult = true)]
    public async Task<bool> ClumsyHoldersDoNotClench(bool clumsy, string gunPrototype)
    {
        await SetSpasmChance(1f);
        var gunUid = await HoldGun(gunPrototype);

        await Server.WaitPost(() =>
        {
            Fire(gunUid);
            if (clumsy)
                SEntMan.EnsureComponent<ClumsyComponent>(SPlayer).ClumsyDefaultCheck = 1f;
            SEntMan.System<SharedStunSystem>().TryUpdateParalyzeDuration(SPlayer, LongStun);
        });
        await RunTicks(1);

        return IsSpasming(gunUid);
    }

    /// <summary>
    ///     <paramref name="chance"/> of 1 backfires every shot, and 0 never.
    /// </summary>
    private async Task MakeClumsy(float chance)
    {
        await Server.WaitPost(() =>
        {
            var clumsyComponent = SEntMan.EnsureComponent<ClumsyComponent>(SPlayer);
            clumsyComponent.ClumsyDefaultCheck = chance;
            SEntMan.Dirty(SPlayer, clumsyComponent);
        });
        await RunTicks(2);
    }

    private async Task SetSpasmChance(float chance)
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(KsCCVars.GunTriggerSpasmChance, chance));
    }

    private async Task<EntityUid> HoldGun(string gunPrototype)
    {
        var gunNetEntity = await PlaceInHands(gunPrototype);
        await RunSeconds(1f); // the pickup cooldown
        return SEntMan.GetEntity(gunNetEntity);
    }

    /// <summary>
    ///     A real shot, predicted by the shooter's client, then the trigger let go.
    /// </summary>
    private async Task ShootFromClient(EntityUid gunUid)
    {
        await SetCombatMode(true);

        var gunNetEntity = SEntMan.GetNetEntity(gunUid);
        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(SPlayer, new Vector2(5f, 0f)));
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestShootEvent
        {
            Gun = gunNetEntity,
            Coordinates = targetCoordinates,
        }));
        await RunTicks(2);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestStopShootEvent { Gun = gunNetEntity }));
        await RunTicks(5);
    }

    private void Fire(EntityUid gunUid)
    {
        Assert.That(TryFire(gunUid), Is.True, "The test's shot didn't go off.");
    }

    private bool TryFire(EntityUid gunUid)
    {
        return SEntMan.System<SharedGunSystem>().AttemptShoot(
            SPlayer,
            (gunUid, SEntMan.GetComponent<GunComponent>(gunUid)),
            new EntityCoordinates(SPlayer, new Vector2(5f, 0f)));
    }

    /// <summary>
    ///     Every entity of <paramref name="prototype"/> on the server, including any queued for deletion.
    /// </summary>
    private int Count(EntProtoId prototype)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var metaData))
        {
            if (metaData.EntityPrototype?.ID == prototype.Id)
                count++;
        }

        return count;
    }

    private async Task AssertNoClientBullets()
    {
        await Client.WaitAssertion(() =>
        {
            var count = 0;
            var query = CEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var metaData))
            {
                if (metaData.EntityPrototype?.ID == _bullet.Id)
                    count++;
            }

            Assert.That(count, Is.Zero, "The shooter's client was left with a bullet the backfire never fired.");
        });
    }

    private int? Ammo(EntityUid gunUid) => SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gunUid).Count;

    private bool IsSpasming(EntityUid gunUid) => SEntMan.HasComponent<KsTriggerSpasmComponent>(gunUid);

    private bool IsHeld(EntityUid gunUid) => HandSys.IsHolding(SPlayer, gunUid);
}
