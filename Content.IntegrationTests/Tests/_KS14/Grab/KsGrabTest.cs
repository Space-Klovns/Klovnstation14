#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Grab;
using Content.Shared._KS14.Resist;
using Content.Shared._Trauma.Hands;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     The grab ladder through the real ctrl-click path, <see cref="PullingSystem.TogglePull(Entity{PullableComponent?}, EntityUid)"/>:
///         a pull, then tighter grips, what each does to both sides, resisting, pinning, letting go and throwing.
/// </summary>
[TestFixture]
[TestOf(typeof(KsGrabSystem))]
public sealed class KsGrabTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Crate = "CrateGenericSteel";
    private readonly EntProtoId _rigged = "KsGrabTestRiggedHuman";
    private readonly EntProtoId _wall = "KsGrabTestWall";
    private readonly EntProtoId _npc = "KsGrabTestNpcHuman";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksGrabStage
  id: KsGrabTestNeverEscape
  takesHand: true
  blocksVictimMovement: true
  escapeChance: 0
  resistFailStaminaDamageMin: 10
  resistFailStaminaDamageMax: 10
  victimAlert: KsGrabbedAggressive
  enterUserPopup: ks-grab-aggressive-user

- type: ksGrabStage
  id: KsGrabTestAlwaysEscape
  takesHand: true
  blocksVictimMovement: true
  escapeChance: 1
  victimAlert: KsGrabbedAggressive
  enterUserPopup: ks-grab-aggressive-user

- type: entity
  parent: MobHuman
  id: KsGrabTestRiggedHuman
  components:
  - type: KsGrabber
    stages:
    - KsGrabPassive
    - KsGrabTestNeverEscape
    - KsGrabTestAlwaysEscape

- type: entity
  parent: MobHuman
  id: KsGrabTestNpcHuman
  components:
  - type: HTN
    rootTask:
      task: IdleCompound

- type: entity
  id: KsGrabTestWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        mask:
        - FullTileMask
        hard: true
";

    private async Task<(EntityUid Grabber, EntityUid Victim)> SetUp(EntProtoId? grabber = null, EntProtoId? victim = null,
        bool combat = true)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid grabberUid = default;
        EntityUid victimUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 8; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            grabberUid = SEntMan.SpawnEntity(grabber ?? Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            victimUid = SEntMan.SpawnEntity(victim ?? Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(grabberUid, combat);
        });

        await Pair.RunTicksSync(5);
        return (grabberUid, victimUid);
    }

    /// <summary>
    ///     A ctrl-click on <paramref name="target"/>, as the input handler makes it.
    /// </summary>
    private void CtrlClick(EntityUid grabber, EntityUid target)
    {
        SEntMan.System<PullingSystem>().TogglePull(target, grabber);
    }

    /// <summary>
    ///     Pulls, waits out the delay before a pull can be tightened, then grips aggressively.
    /// </summary>
    private async Task GrabAggressively(EntityUid grabber, EntityUid victim)
    {
        await Server.WaitPost(() => CtrlClick(grabber, victim));
        await RunSeconds(SEntMan.GetComponent<KsGrabberComponent>(grabber).UpgradeCooldown.TotalSeconds);
        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabber, victim);
            Assert.That(Stage(grabber), Is.EqualTo(1), "The grip must tighten once the delay is over.");
        });
    }

    private int Stage(EntityUid grabber) => SEntMan.GetComponent<KsGrabberComponent>(grabber).Stage;

    private bool IsPulling(EntityUid grabber, EntityUid victim) => SEntMan.GetComponent<PullerComponent>(grabber).Pulling == victim;

    private bool IsDown(EntityUid uid) => SEntMan.System<StandingStateSystem>().IsDown(uid);

    private int VirtualItemsHeld(EntityUid uid)
    {
        return SEntMan.System<SharedHandsSystem>().EnumerateHeld(uid).Count(held => SEntMan.HasComponent<VirtualItemComponent>(held));
    }

    /// <summary>
    ///     Runs a little longer than <paramref name="seconds"/>: a do-after finishes a few ticks after its delay.
    /// </summary>
    private async Task RunSeconds(double seconds)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling((seconds + 0.5) * timing.TickRate));
    }

    /// <summary>
    ///     Makes the grab's hand the active one, as a player would before throwing it.
    /// </summary>
    private void SelectGrabHand(EntityUid grabber)
    {
        var hands = SEntMan.System<SharedHandsSystem>();
        var handItem = hands.EnumerateHeld(grabber).Single(held => SEntMan.HasComponent<VirtualItemComponent>(held));
        Assert.That(hands.IsHolding(grabber, handItem, out var hand));
        hands.SetActiveHand(grabber, hand);
    }

    private void Shove(EntityUid shover, EntityUid target)
    {
        var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
        Assert.That(meleeSystem.TryGetWeapon(shover, out var weaponUid, out var weapon));
        meleeSystem.AttemptDisarmAttack(shover, weaponUid, weapon!, target);
    }

    /// <summary>
    ///     Pulling an object, or someone, takes no hand.
    /// </summary>
    [Test]
    public async Task PullTakesNoHand()
    {
        var (grabberUid, victimUid) = await SetUp(victim: Crate);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(IsPulling(grabberUid, victimUid), "Ctrl-clicking a crate must pull it.");
            Assert.That(VirtualItemsHeld(grabberUid), Is.Zero, "Pulling must not take a hand.");

            // A crate has no grab levels, so a second ctrl-click lets go as before.
            CtrlClick(grabberUid, victimUid);
            Assert.That(IsPulling(grabberUid, victimUid), Is.False, "Ctrl-clicking a pulled crate must let go.");
        });
    }

    /// <summary>
    ///     A pull can't be tightened in the same breath, nor at all outside combat mode.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task AggressiveNeedsTheDelayAndCombatMode(bool combat)
    {
        var (grabberUid, victimUid) = await SetUp(combat: combat);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            CtrlClick(grabberUid, victimUid);
            Assert.That(IsPulling(grabberUid, victimUid), "A second ctrl-click must not let go of someone.");
            Assert.That(Stage(grabberUid), Is.Zero, "A pull must not tighten straight away.");
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(Stage(grabberUid), Is.EqualTo(combat ? 1 : 0),
                combat ? "In combat mode the grip must tighten after the delay." : "Outside combat mode the grip must not tighten.");
        });
    }

    /// <summary>
    ///     Pull, aggressive after the delay, neck after its upgrade time, strangle after its own; each does what its
    ///         stage says.
    /// </summary>
    [Test]
    public async Task LadderClimbsStageByStage()
    {
        var (grabberUid, victimUid) = await SetUp();
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            Assert.That(VirtualItemsHeld(grabberUid), Is.EqualTo(1), "An aggressive grab must take a hand.");
            var blocker = SEntMan.System<ActionBlockerSystem>();
            Assert.That(blocker.CanMove(victimUid), Is.False, "An aggressive grab must hold the victim in place.");
            Assert.That(blocker.CanInteract(victimUid, null), "An aggressive grab must leave the victim their hands.");
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(victimUid, "KsGrabbedAggressive"));
        });

        // The grip can only be tightened once a second, and has to draw the victim in first.
        await RunSeconds(1);
        var neckTime = SProtoMan.Index<KsGrabStagePrototype>("KsGrabNeck").UpgradeTime.TotalSeconds;

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(Stage(grabberUid), Is.EqualTo(1), "The neck grab must take time.");
        });

        await RunSeconds(neckTime);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Stage(grabberUid), Is.EqualTo(2), "The neck grab must land after its upgrade time.");
            Assert.That(IsDown(victimUid), "A neck grab must floor the victim.");
            Assert.That(VirtualItemsHeld(grabberUid), Is.EqualTo(1), "Tightening the grip must not take a second hand.");
            CtrlClick(grabberUid, victimUid);
        });

        var strangle = SProtoMan.Index<KsGrabStagePrototype>("KsGrabStrangle");
        await RunSeconds(strangle.UpgradeTime.TotalSeconds + strangle.DamageInterval.TotalSeconds * 2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Stage(grabberUid), Is.EqualTo(3), "The strangle must land after its upgrade time.");
            var damage = SEntMan.System<DamageableSystem>().GetAllDamage(victimUid);
            Assert.That(damage.DamageDict.TryGetValue("Asphyxiation", out var asphyxiation) && asphyxiation > 0,
                "A strangle must suffocate the victim.");

            CtrlClick(grabberUid, victimUid);
            Assert.That(Stage(grabberUid), Is.EqualTo(3), "There is no grip past the last stage.");
        });
    }

    /// <summary>
    ///     An aggressive grip draws the victim in close.
    /// </summary>
    [Test]
    public async Task AggressiveGripDrawsTheVictimIn()
    {
        var (grabberUid, victimUid) = await SetUp();
        var aggressive = SProtoMan.Index<KsGrabStagePrototype>("KsGrabAggressive");
        Assert.That(aggressive.HoldDistance, Is.Not.Null, "The aggressive grip must draw the victim in.");

        await GrabAggressively(grabberUid, victimUid);
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var distance = (transform.GetWorldPosition(grabberUid) - transform.GetWorldPosition(victimUid)).Length();
            Assert.That(distance, Is.LessThanOrEqualTo(aggressive.HoldDistance!.Value + 0.05f),
                $"The victim must be drawn in to the grip's hold distance, got {distance}.");
        });
    }

    /// <summary>
    ///     Letting go resets the grip, frees the hand and the victim.
    /// </summary>
    [Test]
    public async Task ReleaseResetsTheGrip()
    {
        var (grabberUid, victimUid) = await SetUp();
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            var pullable = SEntMan.GetComponent<PullableComponent>(victimUid);
            Assert.That(SEntMan.System<PullingSystem>().TryStopPull(victimUid, pullable, grabberUid), "The grabber must be able to let go.");

            Assert.That(Stage(grabberUid), Is.Zero, "Letting go must reset the grip.");
            Assert.That(VirtualItemsHeld(grabberUid), Is.Zero, "Letting go must free the hand.");
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(victimUid), "Letting go must free the victim.");
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(victimUid, "KsGrabbedAggressive"), Is.False);
        });
    }

    /// <summary>
    ///     Dropping the hand that holds the victim lets go too.
    /// </summary>
    [Test]
    public async Task DroppingTheHandLetsGo()
    {
        var (grabberUid, victimUid) = await SetUp();
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var handItem = hands.EnumerateHeld(grabberUid).Single(held => SEntMan.HasComponent<VirtualItemComponent>(held));
            Assert.That(hands.TryDrop(grabberUid, handItem));
            Assert.That(IsPulling(grabberUid, victimUid), Is.False, "Dropping the grab's hand must let go.");
            Assert.That(Stage(grabberUid), Is.Zero);
        });
    }

    /// <summary>
    ///     A victim held tight can't just let go: their own attempt is a resist. A failed resist costs stamina, a
    ///         successful one frees them; and resists have a cooldown. The resist button takes the same path.
    /// </summary>
    [Test]
    public async Task ResistFailsOrFreesByChance()
    {
        var (grabberUid, victimUid) = await SetUp(grabber: _rigged);
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            // The victim clicking their pulled alert.
            var pullable = SEntMan.GetComponent<PullableComponent>(victimUid);
            Assert.That(SEntMan.System<PullingSystem>().TryStopPull(victimUid, pullable, victimUid), Is.False,
                "A victim held tight must not be able to just let go.");
            Assert.That(IsPulling(grabberUid, victimUid), "A resist that can't succeed must keep them held.");
            Assert.That(SEntMan.GetComponent<StaminaComponent>(victimUid).StaminaDamage, Is.EqualTo(10f).Within(0.01f),
                "A failed resist must cost the stage's stamina damage.");

            Assert.That(SEntMan.System<KsResistSystem>().TryResist(victimUid), "The resist button must reach the grab.");
            Assert.That(SEntMan.GetComponent<StaminaComponent>(victimUid).StaminaDamage, Is.EqualTo(10f).Within(0.01f),
                "A resist inside the cooldown must do nothing.");
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(Stage(grabberUid), Is.EqualTo(2));

            Assert.That(SEntMan.System<KsResistSystem>().TryResist(victimUid));
            Assert.That(IsPulling(grabberUid, victimUid), Is.False, "A resist that can't fail must free them.");
            Assert.That(Stage(grabberUid), Is.Zero);
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(victimUid));
        });
    }

    /// <summary>
    ///     Holding a direction keeps struggling, once per cooldown, not just once when the key goes down.
    /// </summary>
    [Test]
    public async Task HoldingADirectionKeepsStruggling()
    {
        var (grabberUid, victimUid) = await SetUp(grabber: _rigged);
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitPost(() =>
        {
            var mover = new Entity<InputMoverComponent>(victimUid, SEntMan.GetComponent<InputMoverComponent>(victimUid));
            SEntMan.System<SharedMoverController>().SetVelocityDirection(mover, Direction.East, subTick: 0, enabled: true);
        });

        // Long enough for at least three attempts against the never-escape grip.
        var cooldown = SEntMan.GetComponent<KsGrabbableComponent>(victimUid).ResistCooldown.TotalSeconds;
        await RunSeconds(cooldown * 2.5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsPulling(grabberUid, victimUid), "The grip can't be broken, so they must still be held.");
            // One attempt costs 10. Stamina recovers a little meanwhile, so more than one attempt's worth is the proof.
            Assert.That(SEntMan.GetComponent<StaminaComponent>(victimUid).StaminaDamage, Is.GreaterThan(15f),
                "Holding a direction must keep struggling, each failure costing stamina.");
        });
    }

    /// <summary>
    ///     A victim in a plain pull walks off freely: their own attempt to let go just works.
    /// </summary>
    [Test]
    public async Task PassivePullIsFree()
    {
        var (grabberUid, victimUid) = await SetUp(grabber: _rigged);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(victimUid), "A pull must not hold anyone in place.");
            var pullable = SEntMan.GetComponent<PullableComponent>(victimUid);
            Assert.That(SEntMan.System<PullingSystem>().TryStopPull(victimUid, pullable, victimUid), "A pulled victim must be able to let go.");
            Assert.That(SEntMan.GetComponent<StaminaComponent>(victimUid).StaminaDamage, Is.Zero);
        });
    }

    /// <summary>
    ///     An NPC held tight struggles by itself until it breaks free.
    /// </summary>
    [Test]
    public async Task NpcsStruggleFree()
    {
        var (grabberUid, victimUid) = await SetUp(grabber: _rigged, victim: _npc);
        await GrabAggressively(grabberUid, victimUid);
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            // Over to the stage that can't hold anyone.
            CtrlClick(grabberUid, victimUid);
            Assert.That(Stage(grabberUid), Is.EqualTo(2));
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
            Assert.That(IsPulling(grabberUid, victimUid), Is.False, "An NPC must struggle free on its own."));
    }

    /// <summary>
    ///     A grabber with no free hand can't go past a pull.
    /// </summary>
    [Test]
    public async Task NoFreeHandNoTighterGrip()
    {
        var (grabberUid, victimUid) = await SetUp();

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var coordinates = SEntMan.GetComponent<TransformComponent>(grabberUid).Coordinates;
            foreach (var hand in hands.EnumerateHands(grabberUid).ToList())
            {
                var item = SEntMan.SpawnEntity("Wrench", coordinates);
                Assert.That(hands.TryPickup(grabberUid, item, hand));
            }

            CtrlClick(grabberUid, victimUid);
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            CtrlClick(grabberUid, victimUid);
            Assert.That(IsPulling(grabberUid, victimUid));
            Assert.That(Stage(grabberUid), Is.Zero, "An aggressive grab needs a free hand.");
        });
    }

    /// <summary>
    ///     Shoving someone held tight pins them to the floor, where they stay until they break free.
    /// </summary>
    [Test]
    public async Task ShovePinsTheVictimUntilReleased()
    {
        var (grabberUid, victimUid) = await SetUp();
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsDown(victimUid), Is.False);
            Shove(grabberUid, victimUid);
            Assert.That(IsDown(victimUid), "A shove must pin someone held tight to the floor.");
            Assert.That(IsPulling(grabberUid, victimUid), "Pinning must not let go of them.");
        });

        await RunSeconds(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsDown(victimUid), "A pinned victim must stay down while held.");
            var pullable = SEntMan.GetComponent<PullableComponent>(victimUid);
            Assert.That(SEntMan.System<PullingSystem>().TryStopPull(victimUid, pullable, grabberUid));
            Assert.That(SEntMan.GetComponent<KsGrabberComponent>(grabberUid).Pinned, Is.False, "Letting go must end the pin.");
        });

        await RunSeconds(4);

        await Server.WaitAssertion(() =>
            Assert.That(IsDown(victimUid), Is.False, "Once let go, a pinned victim must be able to get up."));
    }

    /// <summary>
    ///     Someone on their feet can't be thrown; once down, throwing the grab's hand throws them and lets go.
    /// </summary>
    [Test]
    public async Task ThrowingNeedsTheVictimDown()
    {
        var (grabberUid, victimUid) = await SetUp();
        await GrabAggressively(grabberUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            SelectGrabHand(grabberUid);
            var target = SEntMan.GetComponent<TransformComponent>(grabberUid).Coordinates.Offset(new Vector2(4f, 0f));
            SEntMan.System<PredictedHandsSystem>().ThrowHeldItem(grabberUid, target);
            Assert.That(IsPulling(grabberUid, victimUid), "Someone standing must not be thrown.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(victimUid), Is.False);

            Shove(grabberUid, victimUid);
            Assert.That(IsDown(victimUid));
        });

        // Throwing has its own cooldown.
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            SelectGrabHand(grabberUid);
            var target = SEntMan.GetComponent<TransformComponent>(grabberUid).Coordinates.Offset(new Vector2(4f, 0f));
            Assert.That(SEntMan.System<PredictedHandsSystem>().ThrowHeldItem(grabberUid, target));
            Assert.That(IsPulling(grabberUid, victimUid), Is.False, "Throwing the victim must let go of them.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(victimUid), "Throwing the grab's hand must throw the victim.");
        });
    }

    /// <summary>
    ///     A victim thrown into someone knocks both over; thrown into a wall, they go down.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ThrownVictimIsKnockedDownByWhatTheyHit(bool intoPerson)
    {
        var (grabberUid, victimUid) = await SetUp();
        EntityUid obstacleUid = default;

        await Server.WaitPost(() =>
        {
            var coordinates = SEntMan.GetComponent<TransformComponent>(grabberUid).Coordinates;
            obstacleUid = SEntMan.SpawnEntity(intoPerson ? Human : _wall, coordinates.Offset(new Vector2(3f, 0f)));
        });

        await GrabAggressively(grabberUid, victimUid);

        // Brought down first: only someone down can be thrown.
        await Server.WaitPost(() => Shove(grabberUid, victimUid));
        await RunSeconds(1);

        var timing = Server.ResolveDependency<IGameTiming>();
        var throwTime = TimeSpan.Zero;
        await Server.WaitAssertion(() =>
        {
            throwTime = timing.CurTime;
            SelectGrabHand(grabberUid);
            var target = SEntMan.GetComponent<TransformComponent>(grabberUid).Coordinates.Offset(new Vector2(6f, 0f));
            Assert.That(SEntMan.System<PredictedHandsSystem>().ThrowHeldItem(grabberUid, target));
            Assert.That(SEntMan.HasComponent<KsGrabThrownComponent>(victimUid));
        });

        await RunSeconds(1.5);

        await Server.WaitAssertion(() =>
        {
            // The pin's own knockdown runs out well before this: only the impact can have renewed it.
            Assert.That(SEntMan.TryGetComponent<KnockedDownComponent>(victimUid, out var knockedDown) &&
                        knockedDown.NextUpdate > throwTime + TimeSpan.FromSeconds(2),
                intoPerson ? "A victim thrown into someone must go down." : "A victim thrown into a wall must go down.");
            if (intoPerson)
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(obstacleUid), "Whoever is hit must go down too.");

            var grabber = SEntMan.GetComponent<KsGrabberComponent>(grabberUid);
            var impact = grabber.ThrowImpactDamage!;
            var collateral = grabber.ThrowCollateralDamage!;
            var damageable = SEntMan.System<DamageableSystem>();
            Assert.That(damageable.GetAllDamage(victimUid).GetTotal(), Is.GreaterThanOrEqualTo(impact.GetTotal()),
                "The impact must hurt the thrown victim.");
            if (intoPerson)
            {
                Assert.That(damageable.GetAllDamage(obstacleUid).GetTotal(), Is.GreaterThanOrEqualTo(collateral.GetTotal()),
                    "The impact must hurt whoever they slam into.");
            }
            Assert.That(SEntMan.HasComponent<KsGrabThrownComponent>(victimUid), Is.False, "Only the first hit counts.");
        });
    }
}
