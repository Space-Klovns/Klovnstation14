#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.ShoveWeapon;
using Content.Shared.Climbing.Components;
using Content.Shared.CombatMode;
using Content.Server.Gravity;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ComplexShove;

/// <summary>
///     Shoves through the real melee path (<see cref="SharedMeleeWeaponSystem.AttemptDisarmAttack"/>), tgstation style:
///         no stamina damage; a push and a stagger in the open; a knockdown against a wall, a table or another person;
///         a paralysis, once, for someone already down. Held shields must keep doing all of it.
/// </summary>
[TestFixture]
[TestOf(typeof(ComplexShoveSystem))]
[TestOf(typeof(KsShoveWeaponSystem))]
public sealed class KsShoveTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Shield = "RiotShield";
    private static readonly EntProtoId EnergyShield = "EnergyShield";
    private static readonly EntProtoId Table = "Table";
    private static readonly EntProtoId Item = "Wrench";
    private static readonly EntProtoId Stagger = "KsStatusEffectStaggered";

    private readonly EntProtoId _human = "KsShoveTestHuman";
    private readonly EntProtoId _fumbler = "KsShoveTestFumbler";
    private readonly EntProtoId _wall = "KsShoveTestWall";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsShoveTestHuman
  components:
  - type: CombatMode
    canDisarm: true
    disarmFailChance: 0

- type: entity
  parent: MobHuman
  id: KsShoveTestFumbler
  components:
  - type: CombatMode
    canDisarm: true
    disarmFailChance: 1

- type: entity
  id: KsShoveTestWall
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

    /// <summary>
    ///     The shover stands at x 0.5, the target one tile east, and whatever they are shoved into a tile beyond the
    ///         target.
    /// </summary>
    private async Task<(EntityUid Shover, EntityUid Target, EntityUid? Behind)> SetUp(EntProtoId shoverPrototype,
        EntProtoId? behind = null, EntProtoId? held = null)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid shoverUid = default;
        EntityUid targetUid = default;
        EntityUid? behindUid = null;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 5; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            // Weightless mobs can't be knocked down, and a station has gravity.
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            shoverUid = SEntMan.SpawnEntity(shoverPrototype, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            targetUid = SEntMan.SpawnEntity(_human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            if (behind is { } behindPrototype)
                behindUid = SEntMan.SpawnEntity(behindPrototype, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));

            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(shoverUid, true);
        });

        // Let everything settle into the broadphase before anything casts a ray.
        await Pair.RunTicksSync(5);

        if (held is { } heldPrototype)
            await PickUp(shoverUid, heldPrototype);

        return (shoverUid, targetUid, behindUid);
    }

    /// <summary>
    ///     Shoves with whatever the shover would right-click with: the held item, or their own hands.
    /// </summary>
    private void Shove(EntityUid shoverUid, EntityUid targetUid)
    {
        var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
        Assert.That(meleeSystem.TryGetWeapon(shoverUid, out var weaponUid, out var weapon), "The shover has nothing to shove with.");
        meleeSystem.AttemptDisarmAttack(shoverUid, weaponUid, weapon!, targetUid);
    }

    /// <summary>
    ///     Waits out the attack cooldown of whatever the shover shoves with.
    /// </summary>
    private async Task WaitForNextShove(EntityUid shoverUid)
    {
        var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var seconds = 0f;
        await Server.WaitPost(() =>
        {
            Assert.That(meleeSystem.TryGetWeapon(shoverUid, out var weaponUid, out var weapon));
            seconds = (float)(weapon!.NextAttack - timing.CurTime).TotalSeconds;
        });

        await Pair.RunTicksSync(Math.Max(1, (int)MathF.Ceiling(seconds * (float)timing.TickRate)) + 1);
    }

    /// <summary>
    ///     Puts an item in the active hand, then waits out the attack cooldown that selecting a weapon starts.
    /// </summary>
    private async Task<EntityUid> PickUp(EntityUid holderUid, EntProtoId itemPrototype)
    {
        EntityUid itemUid = default;
        await Server.WaitAssertion(() =>
        {
            itemUid = SEntMan.SpawnEntity(itemPrototype, SEntMan.GetComponent<TransformComponent>(holderUid).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(holderUid, itemUid), $"Could not pick up {itemPrototype}.");
        });

        await WaitForNextShove(holderUid);
        return itemUid;
    }

    /// <summary>
    ///     A shove in the open sends the target out of reach; put them back beside the shover, at rest.
    /// </summary>
    private void PutBackInReach(EntityUid shoverUid, EntityUid targetUid)
    {
        var shoverCoordinates = SEntMan.GetComponent<TransformComponent>(shoverUid).Coordinates;
        SEntMan.System<SharedTransformSystem>().SetCoordinates(targetUid, shoverCoordinates.Offset(new Vector2(1f, 0f)));
        SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(targetUid, Vector2.Zero);
    }

    private float StaminaDamage(EntityUid uid) => SEntMan.GetComponent<StaminaComponent>(uid).StaminaDamage;

    private bool IsKnockedDown(EntityUid uid) => SEntMan.HasComponent<KnockedDownComponent>(uid);

    private bool IsStaggered(EntityUid uid)
    {
        return SEntMan.System<StatusEffectsSystem>().HasStatusEffect(uid, Stagger) &&
               SEntMan.GetComponent<MovementSpeedModifierComponent>(uid).SprintSpeedModifier < 1f;
    }

    /// <summary>
    ///     Seconds left of the target's knockdown.
    /// </summary>
    private double KnockdownLeft(EntityUid uid)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        return (SEntMan.GetComponent<KnockedDownComponent>(uid).NextUpdate - timing.CurTime).TotalSeconds;
    }

    /// <summary>
    ///     A shove in the open pushes the target away and staggers them, with bare hands or a shield, and does no
    ///         stamina damage.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task FreeShovePushesAndStaggers(bool withShield)
    {
        var (shoverUid, targetUid, _) = await SetUp(_human, held: withShield ? Shield : (EntProtoId?)null);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);

            Assert.Multiple(() =>
            {
                Assert.That(StaminaDamage(targetUid), Is.Zero, "Shoves must do no stamina damage.");
                var velocity = SEntMan.GetComponent<PhysicsComponent>(targetUid).LinearVelocity;
                Assert.That(velocity.X, Is.GreaterThan(0f), $"The shove must push the target away from the shover, got {velocity}.");
                Assert.That(IsKnockedDown(targetUid), Is.False, "A shove in the open must not knock down.");
                Assert.That(IsStaggered(targetUid), "A shove in the open must stagger.");
            });
        });
    }

    /// <summary>
    ///     A shove into a wall knocks the target down for the shover's blocked knockdown, with bare hands or a shield.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task WallShoveKnocksDown(bool withShield)
    {
        var (shoverUid, targetUid, _) = await SetUp(_human, behind: _wall, held: withShield ? Shield : (EntProtoId?)null);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);

            var shove = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid);
            Assert.Multiple(() =>
            {
                Assert.That(StaminaDamage(targetUid), Is.Zero, "Shoves must do no stamina damage.");
                Assert.That(IsKnockedDown(targetUid), "A shove into a wall must knock down.");
                Assert.That(KnockdownLeft(targetUid), Is.EqualTo(shove.BlockedKnockdownTime.TotalSeconds).Within(0.1),
                    "The knockdown must last the blocked knockdown time.");
            });
        });
    }

    /// <summary>
    ///     A shove into a table puts the target on it and knocks them down.
    /// </summary>
    [Test]
    public async Task TableShovePutsOnTable()
    {
        var (shoverUid, targetUid, tableUid) = await SetUp(_human, behind: Table);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);

            var obstacle = SEntMan.GetComponent<KsShoveObstacleComponent>(tableUid!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(IsKnockedDown(targetUid), "A shove into a table must knock down.");
                Assert.That(KnockdownLeft(targetUid), Is.EqualTo(obstacle.KnockdownTime.TotalSeconds).Within(0.1));
                Assert.That(SEntMan.GetComponent<ClimbingComponent>(targetUid).IsClimbing,
                    "A shove into a table must put the target onto it.");
                Assert.That(IsStaggered(targetUid), Is.False, "Knocking someone over must replace the stagger.");
            });
        });
    }

    /// <summary>
    ///     A shove into another person knocks both over, the person behind for less time.
    /// </summary>
    [Test]
    public async Task PersonBehindKnocksBothDown()
    {
        var (shoverUid, targetUid, behindUid) = await SetUp(_human, behind: _human);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);

            var obstacle = SEntMan.GetComponent<KsShoveObstacleComponent>(behindUid!.Value);
            Assert.That(obstacle.CollateralKnockdownTime, Is.Not.Null, "People must be knocked over when shoved into.");
            Assert.Multiple(() =>
            {
                Assert.That(IsKnockedDown(targetUid), "The shoved person must be knocked down.");
                Assert.That(IsKnockedDown(behindUid.Value), "The person shoved into must be knocked down.");
                Assert.That(KnockdownLeft(targetUid), Is.EqualTo(obstacle.KnockdownTime.TotalSeconds).Within(0.1));
                Assert.That(KnockdownLeft(behindUid.Value), Is.EqualTo(obstacle.CollateralKnockdownTime!.Value.TotalSeconds).Within(0.1));
            });
        });
    }

    /// <summary>
    ///     Shoving someone already down paralyzes them. Shoving them again, while paralyzed or after, until they stand
    ///         up, neither paralyzes them again nor extends it.
    /// </summary>
    [Test]
    public async Task DownedTargetIsParalyzedOnce()
    {
        var (shoverUid, targetUid, _) = await SetUp(_human);
        var timing = Server.ResolveDependency<IGameTiming>();
        TimeSpan? firstEnd = null;

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedStunSystem>().TryKnockdown(targetUid, TimeSpan.FromSeconds(30), drop: false));
            Shove(shoverUid, targetUid);

            var shove = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid);
            Assert.That(SEntMan.HasComponent<StunnedComponent>(targetUid), "Shoving someone down must paralyze them.");
            Assert.That(SEntMan.HasComponent<KsShoveParalyzedComponent>(targetUid));
            Assert.That(SEntMan.System<StatusEffectsSystem>().TryGetTime(targetUid, SharedStunSystem.StunId, out var time));
            firstEnd = time.EndEffectTime;
            Assert.That(firstEnd!.Value - timing.CurTime, Is.EqualTo(shove.ChainParalyzeTime).Within(TimeSpan.FromSeconds(0.1)));
        });

        await WaitForNextShove(shoverUid);

        await Server.WaitAssertion(() =>
        {
            PutBackInReach(shoverUid, targetUid);
            Shove(shoverUid, targetUid);
            Assert.That(SEntMan.System<StatusEffectsSystem>().TryGetTime(targetUid, SharedStunSystem.StunId, out var time));
            Assert.That(time.EndEffectTime, Is.EqualTo(firstEnd), "A second shove must not extend the paralysis.");
        });

        // Once the paralysis is over but they are still down, it still can't be renewed.
        var stunTicks = (int)Math.Ceiling(3.2 * timing.TickRate);
        await Pair.RunTicksSync(stunTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StunnedComponent>(targetUid), Is.False, "The paralysis must have worn off.");
            Assert.That(IsKnockedDown(targetUid), "This test needs the target still down.");
            PutBackInReach(shoverUid, targetUid);
            Shove(shoverUid, targetUid);
            Assert.That(SEntMan.HasComponent<StunnedComponent>(targetUid), Is.False,
                "Someone paralyzed by a shove can't be paralyzed again before standing up.");
        });
    }

    /// <summary>
    ///     Someone down is kicked over by the first shove. The next makes them drop everything they hold, but only
    ///         against a wall; after that they are only pushed. None of it staggers them, and none of it repeats before
    ///         they stand up.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task DownedTargetIsKickedThenDisarmedOnceAgainstAWall(bool againstWall)
    {
        var (shoverUid, targetUid, _) = await SetUp(_human, behind: againstWall ? _wall : (EntProtoId?)null);
        var items = new List<EntityUid>();

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var coordinates = SEntMan.GetComponent<TransformComponent>(targetUid).Coordinates;
            foreach (var hand in hands.EnumerateHands(targetUid).ToList())
            {
                var itemUid = SEntMan.SpawnEntity(Item, coordinates);
                Assert.That(hands.TryPickup(targetUid, itemUid, hand));
                items.Add(itemUid);
            }

            Assert.That(items, Has.Count.GreaterThan(1), "This test needs the target holding something in every hand.");
            Assert.That(SEntMan.System<SharedStunSystem>().TryKnockdown(targetUid, TimeSpan.FromSeconds(30), drop: false));

            Shove(shoverUid, targetUid);
            Assert.That(SEntMan.HasComponent<StunnedComponent>(targetUid), "The first shove must kick them over.");
            Assert.That(items.All(item => hands.IsHolding(targetUid, item)), "The kick itself must not disarm.");
            Assert.That(IsStaggered(targetUid), Is.False, "Someone down must not be staggered.");
        });

        await WaitForNextShove(shoverUid);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            PutBackInReach(shoverUid, targetUid);
            Shove(shoverUid, targetUid);
            Assert.That(IsStaggered(targetUid), Is.False, "Someone down must not be staggered.");

            if (!againstWall)
            {
                Assert.That(items.All(item => hands.IsHolding(targetUid, item)),
                    "Only a shove against a wall can knock things out of their hands.");
                return;
            }

            Assert.That(items.Any(item => hands.IsHolding(targetUid, item)), Is.False,
                "The shove after the kick, against a wall, must make them drop everything they hold.");

            // Pick one back up: the disarm must not happen twice.
            Assert.That(hands.TryPickup(targetUid, items[0], checkActionBlocker: false));
        });

        if (!againstWall)
            return;

        await WaitForNextShove(shoverUid);

        await Server.WaitAssertion(() =>
        {
            PutBackInReach(shoverUid, targetUid);
            Shove(shoverUid, targetUid);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(targetUid, items[0]),
                "Only one shove disarms them before they stand up.");
            Assert.That(IsStaggered(targetUid), Is.False, "Someone down must not be staggered.");
        });
    }

    /// <summary>
    ///     Someone in stamina crit is only pushed: not kicked, not disarmed, not knocked into the wall behind them.
    /// </summary>
    [Test]
    public async Task StaminaCritTargetIsOnlyPushed()
    {
        var (shoverUid, targetUid, _) = await SetUp(_human, behind: _wall);
        EntityUid itemUid = default;

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            itemUid = SEntMan.SpawnEntity(Item, SEntMan.GetComponent<TransformComponent>(targetUid).Coordinates);
            Assert.That(hands.TryPickup(targetUid, itemUid));

            var stamina = SEntMan.GetComponent<StaminaComponent>(targetUid);
            SEntMan.System<SharedStaminaSystem>().TakeStaminaDamage(targetUid, stamina.CritThreshold * 2, stamina);
            Assert.That(stamina.Critical, "This test needs the target in stamina crit.");
            // Whatever stamina crit does to their hands isn't the shove's doing.
            if (!hands.IsHolding(targetUid, itemUid))
                Assert.That(hands.TryPickup(targetUid, itemUid, checkActionBlocker: false));
        });

        for (var i = 0; i < 2; i++)
        {
            await WaitForNextShove(shoverUid);
            await Server.WaitAssertion(() =>
            {
                PutBackInReach(shoverUid, targetUid);
                Shove(shoverUid, targetUid);
                Assert.That(SEntMan.HasComponent<KsShoveParalyzedComponent>(targetUid), Is.False,
                    "Someone in stamina crit must not be kicked over.");
                Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(targetUid, itemUid),
                    "Someone in stamina crit must not be disarmed by a shove.");
            });
        }
    }

    /// <summary>
    ///     A shover whose shoves always fail lands every one with a shield.
    /// </summary>
    [Test]
    public async Task ShieldShoveNeverMisses()
    {
        var (shoverUid, targetUid, _) = await SetUp(_fumbler);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(IsStaggered(targetUid), Is.False, "The fumbler's bare-handed shove must fail.");
        });

        await PickUp(shoverUid, Shield);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(IsStaggered(targetUid), "A shield shove must never miss.");
        });
    }

    /// <summary>
    ///     An energy shield that is off doesn't shove at all, and shoves once it is on.
    /// </summary>
    [Test]
    public async Task EnergyShieldShovesOnlyWhileOn()
    {
        var (shoverUid, targetUid, _) = await SetUp(_human);
        var energyShieldUid = await PickUp(shoverUid, EnergyShield);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<ItemToggleSystem>().IsActivated(energyShieldUid), Is.False, "The energy shield must start off.");

            Shove(shoverUid, targetUid);
            Assert.That(IsStaggered(targetUid), Is.False, "An energy shield that is off must not shove.");

            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(energyShieldUid, shoverUid));
        });

        await WaitForNextShove(shoverUid);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(IsStaggered(targetUid), "An energy shield that is on must shove.");
        });
    }

    /// <summary>
    ///     Shoving again refreshes the stagger instead of adding to it, so it can't be stacked into a permanent slowdown.
    /// </summary>
    [Test]
    public async Task StaggerRefreshesInsteadOfStacking()
    {
        var (shoverUid, targetUid, _) = await SetUp(_human);
        var timing = Server.ResolveDependency<IGameTiming>();
        var shieldUid = await PickUp(shoverUid, Shield);
        var shoves = 0;

        await Server.WaitPost(() =>
        {
            var shoveWeapon = SEntMan.GetComponent<KsShoveWeaponComponent>(shieldUid);
            var weapon = SEntMan.GetComponent<MeleeWeaponComponent>(shieldUid);
            // Enough shoves inside one stagger that, stacked, they would outlast it several times over.
            shoves = (int)MathF.Ceiling((float)shoveWeapon.StaggerDuration.TotalSeconds * weapon.AttackRate) + 2;
        });

        for (var i = 0; i < shoves; i++)
        {
            await Server.WaitAssertion(() =>
            {
                PutBackInReach(shoverUid, targetUid);
                Shove(shoverUid, targetUid);
                Assert.That(IsStaggered(targetUid), $"Shield shove {i + 1} must land.");
            });
            await WaitForNextShove(shoverUid);
        }

        await Server.WaitAssertion(() =>
        {
            var shoveWeapon = SEntMan.GetComponent<KsShoveWeaponComponent>(shieldUid);
            Assert.That(SEntMan.System<StatusEffectsSystem>().TryGetTime(targetUid, shoveWeapon.StaggerEffect, out var time),
                "The target must be staggered.");
            Assert.That(time.EndEffectTime, Is.Not.Null);
            Assert.That(time.EndEffectTime!.Value - timing.CurTime, Is.LessThanOrEqualTo(shoveWeapon.StaggerDuration),
                $"{shoves} shoves must not stagger for longer than one.");
        });
    }
}
