#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.ShoveWeapon;
using Content.Shared.CombatMode;
using Content.Server.Gravity;
using Content.Shared.Damage.Components;
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
///     Shoves through the real melee path (<see cref="SharedMeleeWeaponSystem.AttemptDisarmAttack"/>): a bare-handed
///         shove, a wallshove, and a shove with a held shield, which must keep doing both.
/// </summary>
[TestFixture]
[TestOf(typeof(ComplexShoveSystem))]
[TestOf(typeof(KsShoveWeaponSystem))]
public sealed class KsShoveTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Shield = "RiotShield";
    private static readonly EntProtoId EnergyShield = "EnergyShield";

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
    ///     The shover stands at x 0.5, the target one tile east, and a wall, if any, a tile beyond the target: well
    ///         inside <see cref="ComplexShoveComponent.WallshoveRange"/>.
    /// </summary>
    private async Task<(EntityUid Shover, EntityUid Target)> SetUp(EntProtoId shoverPrototype, bool wall, EntProtoId? held = null)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid shoverUid = default;
        EntityUid targetUid = default;

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
            if (wall)
                SEntMan.SpawnEntity(_wall, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));

            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(shoverUid, true);
        });

        // Let everything settle into the broadphase before anything casts a ray.
        await Pair.RunTicksSync(5);

        if (held is { } heldPrototype)
            await PickUp(shoverUid, heldPrototype);

        return (shoverUid, targetUid);
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

    /// <summary>
    ///     A shove in the open is a basic shove: a little stamina damage and a push away from the shover. With a shield,
    ///         the same, and the shield staggers them as well; bare hands don't.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task BasicShoveDrainsStaminaAndPushesAway(bool withShield)
    {
        var (shoverUid, targetUid) = await SetUp(_human, wall: false, held: withShield ? Shield : (EntProtoId?)null);

        await Server.WaitAssertion(() =>
        {
            var complexShove = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid);
            var stamina = SEntMan.GetComponent<StaminaComponent>(targetUid);
            var expected = stamina.CritThreshold * complexShove.BasicShoveStaminaDamageFraction;

            Shove(shoverUid, targetUid);

            Assert.Multiple(() =>
            {
                Assert.That(StaminaDamage(targetUid), Is.EqualTo(expected).Within(0.01f),
                    "A shove in the open must deal the basic shove's stamina damage.");
                var velocity = SEntMan.GetComponent<PhysicsComponent>(targetUid).LinearVelocity;
                Assert.That(velocity.X, Is.GreaterThan(0f), $"The shove must push the target away from the shover, got {velocity}.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(targetUid), Is.False, "A basic shove must not knock down.");
                Assert.That(IsStaggered(shoverUid, targetUid), Is.EqualTo(withShield),
                    withShield ? "A shield shove must stagger." : "A bare-handed shove must not stagger.");
            });
        });
    }

    /// <summary>
    ///     A shove into a wall is a wallshove: much more stamina damage, and a knockdown once the target is worn down.
    ///         A shield shove must still wallshove.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task WallshoveDrainsMoreStaminaAndKnocksDown(bool withShield)
    {
        var (shoverUid, targetUid) = await SetUp(_human, wall: true, held: withShield ? Shield : (EntProtoId?)null);
        var maxShoves = 0;

        await Server.WaitAssertion(() =>
        {
            var complexShove = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid);
            var stamina = SEntMan.GetComponent<StaminaComponent>(targetUid);
            var expected = stamina.CritThreshold * complexShove.WallshoveStaminaDamageFraction;
            Assert.That(expected, Is.GreaterThan(stamina.CritThreshold * complexShove.BasicShoveStaminaDamageFraction),
                "This test needs a wallshove to hurt more than a basic shove.");

            // The wallshove's own knockdown has to come before stamina crit, which knocks down by itself.
            maxShoves = (int)MathF.Ceiling(stamina.CritThreshold / expected);
            Assert.That(complexShove.WallshoveKnockdownStaminaDamageThreshold, Is.LessThan(stamina.CritThreshold),
                "This test needs the wallshove knockdown to come before stamina crit.");

            Shove(shoverUid, targetUid);

            Assert.Multiple(() =>
            {
                Assert.That(StaminaDamage(targetUid), Is.EqualTo(expected).Within(0.01f),
                    "A shove into a wall must deal the wallshove's stamina damage.");
                var velocity = SEntMan.GetComponent<PhysicsComponent>(targetUid).LinearVelocity;
                Assert.That(velocity.X, Is.GreaterThan(0f), $"The wallshove must push the target into the wall, got {velocity}.");
            });
        });

        for (var shoves = 1; shoves < maxShoves; shoves++)
        {
            var knockedDown = false;
            await Server.WaitPost(() => knockedDown = SEntMan.HasComponent<KnockedDownComponent>(targetUid));
            if (knockedDown)
                break;

            await WaitForNextShove(shoverUid);
            await Server.WaitPost(() => Shove(shoverUid, targetUid));
        }

        await Server.WaitAssertion(() =>
        {
            var stamina = SEntMan.GetComponent<StaminaComponent>(targetUid);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(targetUid) && !stamina.Critical,
                $"Wallshoves must knock the target down before stamina crit (stamina damage {stamina.StaminaDamage} of {stamina.CritThreshold}).");
        });
    }

    /// <summary>
    ///     The control for the knockdown: the same number of shoves in the open never knocks down.
    /// </summary>
    [Test]
    public async Task BasicShovesNeverKnockDown()
    {
        var (shoverUid, targetUid) = await SetUp(_human, wall: false);
        var shoves = 0;

        await Server.WaitPost(() =>
        {
            var complexShove = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid);
            var stamina = SEntMan.GetComponent<StaminaComponent>(targetUid);
            shoves = (int)MathF.Ceiling(complexShove.WallshoveKnockdownStaminaDamageThreshold / (stamina.CritThreshold * complexShove.WallshoveStaminaDamageFraction)) + 3;
        });

        for (var i = 0; i < shoves; i++)
        {
            await Server.WaitAssertion(() =>
            {
                PutBackInReach(shoverUid, targetUid);
                var before = StaminaDamage(targetUid);
                Shove(shoverUid, targetUid);
                Assert.That(StaminaDamage(targetUid), Is.GreaterThan(before), $"Basic shove {i + 1} must land.");
            });
            await WaitForNextShove(shoverUid);
        }

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(targetUid), Is.False,
                $"{shoves} shoves in the open must not knock down."));
    }

    /// <summary>
    ///     A shover whose shoves always fail lands every one with a shield.
    /// </summary>
    [Test]
    public async Task ShieldShoveNeverMisses()
    {
        var (shoverUid, targetUid) = await SetUp(_fumbler, wall: false);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(StaminaDamage(targetUid), Is.EqualTo(0f), "The fumbler's bare-handed shove must fail.");
        });

        await PickUp(shoverUid, Shield);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(StaminaDamage(targetUid), Is.GreaterThan(0f), "A shield shove must never miss.");
            Assert.That(IsStaggered(shoverUid, targetUid), "A shield shove must stagger.");
        });
    }

    /// <summary>
    ///     An energy shield that is off doesn't shove at all, and shoves once it is on.
    /// </summary>
    [Test]
    public async Task EnergyShieldShovesOnlyWhileOn()
    {
        var (shoverUid, targetUid) = await SetUp(_human, wall: false);
        var energyShieldUid = await PickUp(shoverUid, EnergyShield);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<ItemToggleSystem>().IsActivated(energyShieldUid), Is.False, "The energy shield must start off.");

            Shove(shoverUid, targetUid);
            Assert.That(StaminaDamage(targetUid), Is.EqualTo(0f), "An energy shield that is off must not shove.");
            Assert.That(IsStaggered(shoverUid, targetUid), Is.False, "An energy shield that is off must not stagger.");

            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(energyShieldUid, shoverUid));
        });

        await WaitForNextShove(shoverUid);

        await Server.WaitAssertion(() =>
        {
            Shove(shoverUid, targetUid);
            Assert.That(StaminaDamage(targetUid), Is.GreaterThan(0f), "An energy shield that is on must shove.");
            Assert.That(IsStaggered(shoverUid, targetUid), "An energy shield that is on must stagger.");
        });
    }

    /// <summary>
    ///     Shoving again refreshes the stagger instead of adding to it, so it can't be stacked into a permanent slowdown.
    /// </summary>
    [Test]
    public async Task StaggerRefreshesInsteadOfStacking()
    {
        var (shoverUid, targetUid) = await SetUp(_human, wall: false);
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
                var before = StaminaDamage(targetUid);
                Shove(shoverUid, targetUid);
                Assert.That(StaminaDamage(targetUid), Is.GreaterThan(before), $"Shield shove {i + 1} must land.");
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

    /// <summary>
    ///     Whether the target carries the stagger of what the shover holds, and is slowed by it.
    /// </summary>
    private bool IsStaggered(EntityUid shoverUid, EntityUid targetUid)
    {
        if (SEntMan.System<SharedHandsSystem>().GetActiveItem(shoverUid) is not { } heldUid ||
            !SEntMan.TryGetComponent<KsShoveWeaponComponent>(heldUid, out var shoveWeapon))
        {
            // Bare hands: the shield's stagger must not be on them.
            Assert.That(SProtoMan.Index(Shield).TryComp<KsShoveWeaponComponent>(out var shieldShoveWeapon, SEntMan.ComponentFactory));
            return SEntMan.System<StatusEffectsSystem>().HasStatusEffect(targetUid, shieldShoveWeapon!.StaggerEffect);
        }

        var movementSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(targetUid);
        return SEntMan.System<StatusEffectsSystem>().HasStatusEffect(targetUid, shoveWeapon.StaggerEffect) &&
               movementSpeed.SprintSpeedModifier < 1f;
    }
}
