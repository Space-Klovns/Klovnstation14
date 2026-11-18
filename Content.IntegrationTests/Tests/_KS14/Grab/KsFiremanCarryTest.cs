#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Carrying;
using Content.Shared._Trauma.Hands;
using Content.Shared.Alert;
using Content.Shared.Buckle;
using Content.Shared.CombatMode;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared.DragDrop;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     Fireman carry: dragging someone held in an aggressive grab, lying down, onto yourself lifts them after a
///         do-after; dropping the hand or wriggling free puts them down.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFiremanCarrySystem))]
public sealed class KsFiremanCarryTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Bed = "Bed";

    private async Task<(EntityUid Carrier, EntityUid Victim)> SetUp(bool grab, bool prone, bool pull = true)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid carrierUid = default;
        EntityUid victimUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 4; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            carrierUid = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            victimUid = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            if (prone)
                SEntMan.System<SharedStunSystem>().TryKnockdown(victimUid, TimeSpan.FromSeconds(60), drop: false);

            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(carrierUid, true);
            if (pull || grab)
                SEntMan.System<PullingSystem>().TogglePull(victimUid, carrierUid);
        });

        if (grab)
        {
            // A pull tightens only after a second.
            await RunSeconds(1);
            await Server.WaitPost(() => SEntMan.System<PullingSystem>().TogglePull(victimUid, carrierUid));
        }

        // Let the grip finish drawing the victim in: the moving victim would interrupt the carry's do-after.
        await RunSeconds(0.5);

        return (carrierUid, victimUid);
    }

    private bool DragOntoSelf(EntityUid carrierUid, EntityUid victimUid)
    {
        var dragDrop = new DragDropDraggedEvent(carrierUid, carrierUid);
        SEntMan.EventBus.RaiseLocalEvent(victimUid, ref dragDrop);
        return dragDrop.Handled;
    }

    /// <summary>
    ///     Runs a little longer than <paramref name="seconds"/>: a do-after finishes a few ticks after its delay.
    /// </summary>
    private async Task RunSeconds(double seconds)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling((seconds + 0.5) * timing.TickRate));
    }

    private bool IsCarried(EntityUid carrierUid, EntityUid victimUid)
    {
        return SEntMan.TryGetComponent<KsBeingCarriedComponent>(victimUid, out var carried) && carried.Carrier == carrierUid &&
               SEntMan.GetComponent<TransformComponent>(victimUid).ParentUid == carrierUid;
    }

    private string Describe(EntityUid carrierUid, EntityUid victimUid)
    {
        var parent = SEntMan.GetComponent<TransformComponent>(victimUid).ParentUid;
        return $"carried component: {SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid)}, " +
               $"parent: {SEntMan.ToPrettyString(parent)}, " +
               $"puller: {SEntMan.ToPrettyString(SEntMan.GetComponent<PullableComponent>(victimUid).Puller)}, " +
               $"down: {SEntMan.HasComponent<KnockedDownComponent>(victimUid)}";
    }

    /// <summary>
    ///     Whether a do-after of theirs is still under way. A cancelled one stays on the component for a moment, to
    ///         show its bar going red, so the component alone doesn't say.
    /// </summary>
    private bool HasRunningDoAfter(EntityUid uid)
    {
        return SEntMan.TryGetComponent<DoAfterComponent>(uid, out var doAfters) &&
               doAfters.DoAfters.Values.Any(doAfter => !doAfter.Cancelled && !doAfter.Completed);
    }

    private async Task Lift(EntityUid carrierUid, EntityUid victimUid)
    {
        await Server.WaitAssertion(() => Assert.That(DragOntoSelf(carrierUid, victimUid), "The carry must start."));
        await RunSeconds(SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).CarryTime.TotalSeconds);
        await Server.WaitAssertion(() => Assert.That(IsCarried(carrierUid, victimUid), "The carry must finish after its do-after."));
    }

    [Test]
    public async Task LiftsAfterTheDoAfter()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);

        await Server.WaitAssertion(() =>
        {
            Assert.That(DragOntoSelf(carrierUid, victimUid), "Dragging someone held and lying down onto yourself must start a carry.");
            Assert.That(IsCarried(carrierUid, victimUid), Is.False, "Lifting someone must take time.");
        });

        await RunSeconds(SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).CarryTime.TotalSeconds);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsCarried(carrierUid, victimUid), Describe(carrierUid, victimUid));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(victimUid).CanCollide, Is.False, "Someone carried must not collide.");
            Assert.That(SEntMan.GetComponent<PullableComponent>(victimUid).Puller, Is.Null, "The carry must replace the grab.");
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.EnumerateHeld(carrierUid).Count(held =>
                SEntMan.TryGetComponent<VirtualItemComponent>(held, out var virtualItem) && virtualItem.BlockingEntity == victimUid),
                Is.EqualTo(2), "The carry must take both hands.");
        });
    }

    /// <summary>
    ///     Alt-clicking someone held and lying down offers the carry as the default alternative action.
    /// </summary>
    /// <summary>
    ///     A lift under way is called off as soon as it stops being possible, not left to run out and then fail.
    /// </summary>
    [Test]
    public async Task LiftStopsWhenTheyGetUp()
    {
        var (carrierUid, victimUid) = await SetUp(grab: false, prone: true, pull: false);

        await Server.WaitAssertion(() =>
        {
            Assert.That(DragOntoSelf(carrierUid, victimUid), "The carry must start.");
            Assert.That(HasRunningDoAfter(carrierUid), "Lifting someone must be a do-after.");
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(HasRunningDoAfter(carrierUid), "The lift must still be under way.");
            SEntMan.RemoveComponent<KnockedDownComponent>(victimUid);
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(victimUid), Is.False, "The victim must be back on their feet.");
        });

        // Far less than the lift takes: only the every-tick check can have ended it.
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(HasRunningDoAfter(carrierUid), Is.False,
                "A lift must stop as soon as whoever is being lifted gets up.");
            Assert.That(IsCarried(carrierUid, victimUid), Is.False);
        });
    }

    [Test]
    public async Task AltClickOffersTheCarry()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);

        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(victimUid, carrierUid, typeof(AlternativeVerb));
            var carry = verbs.OfType<AlternativeVerb>().SingleOrDefault(verb => verb.Text == Robust.Shared.Localization.Loc.GetString("ks-carry-verb"));
            Assert.That(carry, Is.Not.Null, "Alt-click must offer a fireman carry.");
            Assert.That(verbs.OfType<AlternativeVerb>().Max(verb => verb.Priority), Is.EqualTo(carry!.Priority),
                "The carry must be what alt-click does.");
            carry.Act?.Invoke();
        });

        await RunSeconds(SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).CarryTime.TotalSeconds);
        await Server.WaitAssertion(() => Assert.That(IsCarried(carrierUid, victimUid), Describe(carrierUid, victimUid)));
    }

    /// <summary>
    ///     Throwing a hand that carries someone throws them off your back.
    /// </summary>
    [Test]
    public async Task ThrowingTheHandThrowsThemOff()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);
        await Lift(carrierUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryGetActiveItem(carrierUid, out var active) &&
                        SEntMan.GetComponent<VirtualItemComponent>(active.Value).BlockingEntity == victimUid,
                "With both hands carrying, the active hand holds the carried.");

            var target = SEntMan.GetComponent<TransformComponent>(carrierUid).Coordinates.Offset(new System.Numerics.Vector2(3f, 0f));
            Assert.That(SEntMan.System<PredictedHandsSystem>().ThrowHeldItem(carrierUid, target));
            Assert.That(SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid), Is.False, "A thrown carry must end.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(victimUid), "The carried must be thrown.");
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(victimUid).CanCollide, "Someone thrown must collide again.");
        });

        // The other hand's virtual item is deleted at the end of the tick.
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().EnumerateHeld(carrierUid).Any(held => SEntMan.HasComponent<VirtualItemComponent>(held)),
                Is.False, "Both hands must be free after the throw."));
    }

    /// <summary>
    ///     Someone on a back can't be pulled off it, by the carrier or anyone else.
    /// </summary>
    [Test]
    public async Task TheCarriedCantBePulled()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);
        await Lift(carrierUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<PullingSystem>().TogglePull(victimUid, carrierUid);
            Assert.That(SEntMan.GetComponent<PullableComponent>(victimUid).Puller, Is.Null, "Someone carried must not be pulled.");
            Assert.That(IsCarried(carrierUid, victimUid), Describe(carrierUid, victimUid));
        });
    }

    /// <summary>
    ///     Someone strapped to a bed isn't lifted off it: they have to be unbuckled first.
    /// </summary>
    [Test]
    public async Task TheBuckledArentLifted()
    {
        var (carrierUid, victimUid) = await SetUp(grab: false, prone: false, pull: false);

        await Server.WaitAssertion(() =>
        {
            var bedUid = SEntMan.SpawnEntity(Bed, SEntMan.GetComponent<TransformComponent>(victimUid).Coordinates);
            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            Assert.That(buckleSystem.TryBuckle(victimUid, victimUid, bedUid), "This test needs the victim buckled.");
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(victimUid),
                "This test needs the victim lying down, so that only the buckle stops the carry.");

            Assert.That(DragOntoSelf(carrierUid, victimUid) && HasRunningDoAfter(carrierUid), Is.False,
                "Someone buckled must not be lifted.");
            Assert.That(buckleSystem.IsBuckled(victimUid), "The refused carry must leave them buckled.");
        });
    }

    /// <summary>
    ///     No grip is needed, held or not: only that they lie down.
    /// </summary>
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public async Task OnlyNeedsThemLyingDown(bool pull, bool prone, bool expectCarried)
    {
        var (carrierUid, victimUid) = await SetUp(grab: false, prone: prone, pull: pull);

        await Server.WaitAssertion(() =>
        {
            // Without a carry the drag falls through to the strip menu, which isn't ours to check.
            DragOntoSelf(carrierUid, victimUid);
        });

        await RunSeconds(SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).CarryTime.TotalSeconds);

        await Server.WaitAssertion(() =>
        {
            if (expectCarried)
                Assert.That(IsCarried(carrierUid, victimUid), "Anyone lying down must be liftable, grabbed or not. " + Describe(carrierUid, victimUid));
            else
                Assert.That(SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid), Is.False, "Someone standing can't be lifted.");
        });
    }

    /// <summary>
    ///     Someone in crit can't wriggle off, and going into crit mid-wriggle stops it.
    /// </summary>
    [Test]
    public async Task CritCantWriggleFree()
    {
        var (carrierUid, victimUid) = await SetUp(grab: false, prone: true);
        await Lift(carrierUid, victimUid);
        var escapeTime = SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).EscapeTime.TotalSeconds;

        // Starts wriggling while awake, then drops into crit.
        await Server.WaitPost(() => SEntMan.System<KsFiremanCarrySystem>().TryEscape(
            (victimUid, SEntMan.GetComponent<KsBeingCarriedComponent>(victimUid))));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            // Real damage, as forcing the state alone would be undone by the damage thresholds.
            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 120 } };
            SEntMan.System<DamageableSystem>().ChangeDamage(victimUid, damage, ignoreResistances: true);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(victimUid), "This test needs the victim in crit.");
        });
        await RunSeconds(escapeTime);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsCarried(carrierUid, victimUid), "Going into crit must stop a wriggle under way. " + Describe(carrierUid, victimUid));

            var alerts = SEntMan.System<AlertsSystem>();
            var carrier = SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid);
            alerts.ActivateAlert(victimUid, SProtoMan.Index(carrier.CarriedAlert));
            SEntMan.System<KsFiremanCarrySystem>().TryEscape((victimUid, SEntMan.GetComponent<KsBeingCarriedComponent>(victimUid)));
        });

        await RunSeconds(escapeTime);

        await Server.WaitAssertion(() =>
            Assert.That(IsCarried(carrierUid, victimUid), "Someone in crit must not be able to wriggle off."));
    }

    [Test]
    public async Task DroppingTheHandPutsThemDown()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);
        await Lift(carrierUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var handItem = hands.EnumerateHeld(carrierUid).First(held => SEntMan.HasComponent<VirtualItemComponent>(held));
            Assert.That(hands.TryDrop(carrierUid, handItem));

            Assert.That(SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsCarryingComponent>(carrierUid), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(victimUid).ParentUid, Is.Not.EqualTo(carrierUid),
                "Someone put down must be back on the floor.");
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(victimUid).CanCollide, "Someone put down must collide again.");
        });
    }

    [Test]
    public async Task WrigglingFreeTakesTime()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);
        await Lift(carrierUid, victimUid);

        await Server.WaitAssertion(() =>
        {
            var alerts = SEntMan.System<AlertsSystem>();
            var carrier = SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid);
            Assert.That(alerts.IsShowingAlert(victimUid, carrier.CarriedAlert));
            Assert.That(alerts.ActivateAlert(victimUid, SProtoMan.Index(carrier.CarriedAlert)));
            Assert.That(IsCarried(carrierUid, victimUid), "Wriggling free must take time.");
        });

        await RunSeconds(SEntMan.GetComponent<KsFiremanCarrierComponent>(carrierUid).EscapeTime.TotalSeconds);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid), Is.False, "Wriggling free must put them down.");
            Assert.That(SEntMan.System<SharedHandsSystem>().EnumerateHeld(carrierUid).Any(held => SEntMan.HasComponent<VirtualItemComponent>(held)),
                Is.False, "The carrier's hand must be freed.");
        });
    }

    [Test]
    public async Task DeletingTheCarrierPutsThemDown()
    {
        var (carrierUid, victimUid) = await SetUp(grab: true, prone: true);
        await Lift(carrierUid, victimUid);

        await Server.WaitPost(() => SEntMan.DeleteEntity(carrierUid));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(victimUid), "Deleting the carrier must not take the carried with it.");
            Assert.That(SEntMan.HasComponent<KsBeingCarriedComponent>(victimUid), Is.False);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(victimUid).CanCollide);
        });
    }
}
