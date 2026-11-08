#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     Someone going down with a full-auto gun's trigger held can keep hold of it, firing until it's empty or they recover.
/// </summary>
/// <remarks>
///     The held trigger is <see cref="SharedGunSystem.AttemptShoot(EntityUid, Entity{GunComponent}, EntityCoordinates, EntityUid?)"/>
///         on the server, as NPCs fire. A test client has no key held, so it answers a real shot with a stop request at once.
/// </remarks>
[TestOf(typeof(KsTriggerSpasmSystem))]
public sealed class KsTriggerSpasmTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands, a mob state, and able to be stunned

    private const string FullAutoGun = "KsTestSpasmGun";
    private const string SemiAutoGun = "KsTestSpasmSemiGun";
    private const string WieldedGun = "KsTestSpasmWieldedGun";

    private const string SpasmPopup = "Your hand clenches on the trigger!";

    private const int Capacity = 10;
    private const float FireRate = 10f;

    /// <summary>
    ///     Comfortably longer than the magazine takes to empty.
    /// </summary>
    private const float EmptySeconds = Capacity / FireRate * 2f;

    /// <summary>
    ///     Long enough for anything still firing to have spent its magazine, or to have a stale trigger.
    /// </summary>
    private static readonly TimeSpan LongStun = TimeSpan.FromSeconds(30);

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseBullet
  id: KsTestSpasmBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 1

- type: entity
  parent: BaseItem
  id: KsTestSpasmGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: FullAuto
    availableModes:
    - FullAuto
  - type: BasicEntityAmmoProvider
    proto: KsTestSpasmBullet
    capacity: 10
    count: 10

- type: entity
  parent: KsTestSpasmGun
  id: KsTestSpasmSemiGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto

- type: entity
  parent: KsTestSpasmGun
  id: KsTestSpasmWieldedGun
  components:
  - type: Wieldable
  - type: GunRequiresWield
";

    [SetUp]
    public async Task AlwaysSpasm()
    {
        await SetChance(1f);
    }

    protected override async Task TearDown()
    {
        await SetChance(KsCCVars.GunTriggerSpasmChance.DefaultValue);
        await base.TearDown();
    }

    [Test]
    public async Task StunnedShooterFiresUntilEmpty()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, Stun);
        Assert.That(IsSpasming(gunUid), Is.True, "Going down with the trigger held didn't start a spasm.");

        await RunSeconds(EmptySeconds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Ammo(gunUid), Is.Zero, "The gun stopped firing with rounds left.");
            Assert.That(IsSpasming(gunUid), Is.False, "The spasm outlasted the magazine.");
            Assert.That(IsHeld(gunUid), Is.True, "The gun left the hand gripping it.");
            Assert.That(Gun(gunUid).LastShotWasEmpty, Is.False, "The next real pull of the trigger wouldn't click.");
        }
    }

    [Test]
    public async Task RecoveringStopsFireWithRoundsLeft()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, () =>
            SEntMan.System<SharedStunSystem>().TryUpdateParalyzeDuration(SPlayer, TimeSpan.FromSeconds(Capacity / FireRate / 2f)));
        await RunSeconds(EmptySeconds);

        var ammoAfterRecovery = Ammo(gunUid);
        await RunSeconds(EmptySeconds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsSpasming(gunUid), Is.False, "The spasm outlasted the stun.");
            Assert.That(ammoAfterRecovery, Is.GreaterThan(0), "The gun was emptied, so recovery didn't stop it.");
            Assert.That(ammoAfterRecovery, Is.LessThan(Capacity - 1), "The gun never fired during the stun.");
            Assert.That(Ammo(gunUid), Is.EqualTo(ammoAfterRecovery), "The gun kept firing after the shooter recovered.");
        }
    }

    [TestCase(MobState.Critical)]
    [TestCase(MobState.Dead)]
    public async Task CritAndDeathKeepFiring(MobState mobState)
    {
        await StopChoking();
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, () => SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, mobState));
        await RunSeconds(EmptySeconds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Ammo(gunUid), Is.Zero, $"A {mobState} shooter's gun didn't empty.");
            Assert.That(IsHeld(gunUid), Is.True, $"A {mobState} shooter lost the gun.");
        }
    }

    [Test]
    public async Task FallingAsleepKeepsFiring()
    {
        await StopChoking();
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, () => SEntMan.System<SleepingSystem>().TrySleeping(SPlayer));
        await RunSeconds(EmptySeconds);

        Assert.That(Ammo(gunUid), Is.Zero, "A sleeping shooter's gun didn't empty.");
    }

    /// <summary>
    ///     Each further stun, and crit turning to death, raises the drop again. The grip isn't rolled for a second time.
    /// </summary>
    [Test]
    public async Task FurtherHitsDoNotLoosenTheGrip()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, Stun);
        await SetChance(0f);
        await Server.WaitPost(() => Stun());
        await RunTicks(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsHeld(gunUid), Is.True, "A second stun knocked the gun out of the grip.");
            Assert.That(IsSpasming(gunUid), Is.True, "A second stun ended the spasm.");
        }
    }

    [Test]
    public async Task WieldedGunStaysWieldedAndFires()
    {
        var gunUid = await HoldGun(WieldedGun);
        await Server.WaitPost(() => SEntMan.System<SharedWieldableSystem>().TryWield(gunUid, SPlayer));
        await RunTicks(1);

        await GoDownFiring(gunUid, Stun);
        await RunSeconds(EmptySeconds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SEntMan.GetComponent<WieldableComponent>(gunUid).Wielded, Is.True, "Going down unwielded the gun.");
            Assert.That(Ammo(gunUid), Is.Zero, "A wielded gun didn't keep firing.");
        }
    }

    [Test]
    public async Task RecoilWalksTheAimOneWay()
    {
        var gunUid = await HoldGun(FullAutoGun);
        await GoDownFiring(gunUid, Stun);

        var spasmComponent = SEntMan.GetComponent<KsTriggerSpasmComponent>(gunUid);
        var driftSign = spasmComponent.DriftSign;
        var previousAim = spasmComponent.AimAngle.Theta;
        var previousAmmo = Ammo(gunUid);
        var steps = new List<double>();

        // Stops at the last round, as the empty click after it ends the spasm.
        while (Ammo(gunUid) > 0)
        {
            await RunTicks(1);
            await Server.WaitAssertion(() =>
            {
                if (Ammo(gunUid) == previousAmmo)
                    return;

                var aim = spasmComponent.AimAngle.Theta;
                Assert.That(Math.Sign(aim - previousAim), Is.EqualTo(driftSign), "The aim didn't move, or moved the wrong way.");

                var firedTowards = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(Gun(gunUid).ShootCoordinates!.Value).Position
                    - SEntMan.System<SharedTransformSystem>().GetMapCoordinates(SPlayer).Position;
                Assert.That(Angle.ShortestDistance(firedTowards.ToAngle(), new Angle(previousAim)).Theta, Is.EqualTo(0d).Within(0.001d),
                    "The shot didn't go where the aim was pointing.");

                steps.Add(Math.Abs(aim - previousAim));
                previousAim = aim;
                previousAmmo = Ammo(gunUid);
            });
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(steps, Has.Count.GreaterThan(1), "Too few shots were seen for the aim to have walked.");
            Assert.That(steps, Is.All.GreaterThanOrEqualTo(KsTriggerSpasmSystem.LowestDriftFloor.Theta - 0.0001d),
                "This gun's recoil is tiny, so the aim should have moved by the floor at least.");
            Assert.That(steps.Distinct().Count(), Is.GreaterThan(1), "Every step was the same, so the floor isn't being rolled.");
        }
    }

    /// <summary>
    ///     A client doesn't know its shots are refused, and keeps asking while its mouse button is down.
    /// </summary>
    [Test]
    public async Task ShootRequestsDoNotAimADownedShooter()
    {
        var gunUid = await HoldGun(FullAutoGun);
        await SetCombatMode(true);
        await GoDownFiring(gunUid, Stun);

        var aimBefore = SEntMan.GetComponent<KsTriggerSpasmComponent>(gunUid).AimAngle;
        var gunNetEntity = SEntMan.GetNetEntity(gunUid);
        var behindCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(SPlayer, new Vector2(-5f, 0f)));

        await RunTicks(5);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestShootEvent
        {
            Gun = gunNetEntity,
            Coordinates = behindCoordinates,
        }));
        await RunTicks(3);

        var aimAfter = SEntMan.GetComponent<KsTriggerSpasmComponent>(gunUid).AimAngle;
        Assert.That(Math.Abs(Angle.ShortestDistance(aimBefore, aimAfter).Theta), Is.LessThan(MathHelper.PiOver2),
            "A shoot request turned the gun of someone who is down.");
    }

    [Test]
    public async Task LosingTheGunEndsTheSpasm()
    {
        var gunUid = await HoldGun(FullAutoGun);
        await GoDownFiring(gunUid, Stun);

        await Server.WaitPost(() => HandSys.TryDrop(SPlayer, gunUid, checkActionBlocker: false));
        await RunTicks(2);

        var ammoAfterDrop = Ammo(gunUid);
        await RunSeconds(EmptySeconds);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsSpasming(gunUid), Is.False, "The spasm outlived the grip.");
            Assert.That(Ammo(gunUid), Is.EqualTo(ammoAfterDrop), "A gun nobody held kept firing.");
        }
    }

    [Test]
    public async Task NoChanceMeansAnOrdinaryDrop()
    {
        await SetChance(0f);
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, Stun);

        await AssertDropped(gunUid);
    }

    [Test]
    public async Task SemiAutoGunsDrop()
    {
        var gunUid = await HoldGun(SemiAutoGun);

        await GoDownFiring(gunUid, Stun);

        await AssertDropped(gunUid);
    }

    /// <summary>
    ///     Someone only knocked down can still shoot, so nothing has taken over their hand.
    /// </summary>
    [Test]
    public async Task PlainKnockdownDrops()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, () => SEntMan.System<SharedStunSystem>().TryKnockdown(SPlayer, TimeSpan.FromSeconds(5), force: true));

        await AssertDropped(gunUid);
    }

    [Test]
    public async Task IdleGunDrops()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await Server.WaitPost(() => Stun());
        await RunTicks(1);

        await AssertDropped(gunUid);
    }

    /// <summary>
    ///     A gun fired a while ago still has its old coordinates when the shooter's hold-to-attack client never stopped it.
    /// </summary>
    [Test]
    public async Task GunFiredAWhileAgoDrops()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await Server.WaitPost(() => Fire(gunUid));
        await RunSeconds(1f);
        await Server.WaitPost(() => Stun());
        await RunTicks(1);

        await AssertDropped(gunUid);
    }

    /// <summary>
    ///     A real shot and stop request from the client. The stop clears the gun's coordinates, while it's still recent.
    /// </summary>
    [Test]
    public async Task ReleasedTriggerDrops()
    {
        var gunUid = await HoldGun(FullAutoGun);
        await SetCombatMode(true);

        var gunNetEntity = SEntMan.GetNetEntity(gunUid);
        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid.Owner, new Vector2(10.5f, 0.5f)));
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestShootEvent
        {
            Gun = gunNetEntity,
            Coordinates = targetCoordinates,
        }));
        await RunTicks(2);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestStopShootEvent { Gun = gunNetEntity }));
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Ammo(gunUid), Is.LessThan(Capacity), "The client's shot was never fired, so nothing was released.");
            Assert.That(Gun(gunUid).ShootCoordinates, Is.Null, "The stop request didn't reach the gun.");
            Stun();
        });
        await RunTicks(1);

        await AssertDropped(gunUid);
    }

    [Test]
    public async Task ShooterSeesThePopup()
    {
        var gunUid = await HoldGun(FullAutoGun);

        await GoDownFiring(gunUid, Stun);
        await RunTicks(2);

        Assert.That(ClientPopups(), Has.Some.EqualTo(SpasmPopup));
    }

    [Test]
    public async Task BystandersDoNotSeeThePopup()
    {
        var gunUid = await HoldGun(FullAutoGun);

        // The client watches from the next tile, leaving the shooter to go down without one.
        await Server.WaitPost(() =>
        {
            var bystanderUid = SEntMan.SpawnEntity(PlayerPrototype, SEntMan.GetCoordinates(PlayerCoords));
            Server.PlayerMan.SetAttachedEntity(ServerSession, bystanderUid);
        });
        await RunTicks(5);

        await GoDownFiring(gunUid, Stun);
        await RunTicks(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsSpasming(gunUid), Is.True, "Nothing happened that a bystander could have seen.");
            Assert.That(ClientPopups(), Has.None.EqualTo(SpasmPopup));
        }
    }

    /// <summary>
    ///     The shooter's client runs the shots itself, or they would neither hear them nor see the muzzle flash.
    /// </summary>
    [Test]
    public async Task ShootersClientKeepsTheGunAndPredictsTheShots()
    {
        var gunUid = await HoldGun(FullAutoGun);
        var clientGunUid = ToClient(gunUid);
        var lastFireBefore = CEntMan.GetComponent<GunComponent>(clientGunUid).LastFire;

        await GoDownFiring(gunUid, Stun);
        await RunTicks(3);

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.HasComponent<KsTriggerSpasmComponent>(clientGunUid), Is.True, "The spasm didn't reach the client."));

        await RunSeconds(EmptySeconds);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<BasicEntityAmmoProviderComponent>(clientGunUid).Count, Is.Zero, "The client's ammo count fell behind.");
            // Not networked, so this only moves on a client that took the shots itself.
            Assert.That(CEntMan.GetComponent<GunComponent>(clientGunUid).LastFire, Is.GreaterThan(lastFireBefore), "The client didn't predict the shots.");
            Assert.That(CEntMan.GetComponent<TransformComponent>(clientGunUid).ParentUid, Is.EqualTo(ToClient(SPlayer)), "The gun left the client's hand.");
        });
    }

    private async Task SetChance(float chance)
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(KsCCVars.GunTriggerSpasmChance, chance));
    }

    /// <summary>
    ///     The test map has no air. Choking would wake a sleeper, and any damage puts a crit with none back to alive.
    ///         Not for anything that stuns or knocks down, as godmode blocks those too.
    /// </summary>
    private async Task StopChoking()
    {
        await Server.WaitPost(() => SEntMan.System<SharedGodmodeSystem>().EnableGodmode(SPlayer));
    }

    private async Task<EntityUid> HoldGun(string gunPrototype)
    {
        var gunNetEntity = await PlaceInHands(gunPrototype);
        await RunSeconds(1f); // the pickup cooldown
        return SEntMan.GetEntity(gunNetEntity);
    }

    /// <summary>
    ///     Fires <paramref name="gunUid"/>, and calls <paramref name="goDown"/> before the next shot was due.
    /// </summary>
    private async Task GoDownFiring(EntityUid gunUid, Action goDown)
    {
        await Server.WaitPost(() =>
        {
            Fire(gunUid);
            goDown();
        });
        await RunTicks(1);
    }

    private void Fire(EntityUid gunUid)
    {
        var fired = SEntMan.System<SharedGunSystem>().AttemptShoot(
            SPlayer,
            (gunUid, Gun(gunUid)),
            new EntityCoordinates(SPlayer, new Vector2(5f, 0f)));

        Assert.That(fired, Is.True, "The test's shot didn't fire.");
    }

    private void Stun()
    {
        SEntMan.System<SharedStunSystem>().TryUpdateParalyzeDuration(SPlayer, LongStun);
    }

    private async Task AssertDropped(EntityUid gunUid)
    {
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsSpasming(gunUid), Is.False, "This shouldn't have started a spasm.");
                Assert.That(IsHeld(gunUid), Is.False, "The gun wasn't dropped as usual.");
            }
        });
    }

    private List<string> ClientPopups()
    {
        return CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels.Select(label => label.Text).ToList();
    }

    private GunComponent Gun(EntityUid gunUid) => SEntMan.GetComponent<GunComponent>(gunUid);

    private int? Ammo(EntityUid gunUid) => SEntMan.GetComponent<BasicEntityAmmoProviderComponent>(gunUid).Count;

    private bool IsSpasming(EntityUid gunUid) => SEntMan.HasComponent<KsTriggerSpasmComponent>(gunUid);

    private bool IsHeld(EntityUid gunUid) => HandSys.IsHolding(SPlayer, gunUid);
}
