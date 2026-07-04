using Content.Shared.Administration.Logs;
using Content.Shared.Destructible;
using Content.Shared.Effects;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Prototypes; // KS14
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems; // KS14
using Robust.Shared.Configuration; // KS14
using Robust.Shared.Prototypes; // KS14
using Content.Shared._KS14.CCVar; // KS14
using Content.Shared.Movement.Components; //KS14
using System.Linq; //KS14
using Robust.Shared.Log;
using Content.Shared.Standing; // KS14

namespace Content.Shared._Trauma.Projectiles;

/// <summary>
/// Handles predicting projectile hits.
/// This was previously only done serverside.
/// </summary>
public sealed class PredictedProjectileSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly SharedDestructibleSystem _destructible = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedProjectileSystem _projectile = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!; // KS14
    [Dependency] private readonly IConfigurationManager _config = default!; // KS14
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!; // KS14
    [Dependency] private readonly StandingStateSystem _standingStateSystem = default!; // KS14

    private EntityQuery<ProjectileComponent> _query;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;

    /// <summary>
    /// KS14, impulse cvar <see cref="KsCCVars"/>.
    /// </summary>
    private float _gunImpulse = 0f;
    /// <summary>
    /// KS14, penetration cvar <see cref="KsCCVars"/>.
    /// </summary>
    private float _gunPenetrationMinShots = 1f;

    public override void Initialize()
    {
        base.Initialize();
        _config.OnValueChanged(KsCCVars.GunImpulseMultiplier, x => _gunImpulse = x, invokeImmediately: true);
        _config.OnValueChanged(KsCCVars.GunPenetrationMinShots, x => _gunPenetrationMinShots = x, invokeImmediately: true);
        _query = GetEntityQuery<ProjectileComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();

        SubscribeLocalEvent<ProjectileComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(EntityUid uid, ProjectileComponent component, ref StartCollideEvent args)
    {
        // This is so entities that shouldn't get a collision are ignored.
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture || !args.OtherFixture.Hard)
            return;

        DoHit((uid, component, args.OurBody), args.OtherEntity, args.OtherFixture);
    }

    /// <summary>
    /// Process a hit for a projectile and a target entity.
    /// This overload uses the first hard fixture on the target,
    /// there should only be 1 hard fixture on a given entity.
    /// Checking multiple hard fixtures would need a collision layer to check against, CBF.
    /// </summary>
    public void DoHit(EntityUid uid, EntityUid target)
    {
        if (!_query.TryComp(uid, out var comp) ||
            !_physicsQuery.TryComp(uid, out var physics) ||
            FindHardFixture(target) is not { } otherFixture)
            return;

        DoHit((uid, comp, physics), target, otherFixture);
    }

    private Fixture? FindHardFixture(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var comp))
            return null;

        foreach (var fixture in comp.Fixtures.Values)
        {
            if (fixture.Hard)
                return fixture;
        }

        return null;
    }

    /// <summary>
    /// Process a hit for a projectile and a target entity.
    /// </summary>
    public void DoHit(Entity<ProjectileComponent, PhysicsComponent> ent, EntityUid target, Fixture otherFixture)
    {
        var (uid, comp, ourBody) = ent;
        if (comp.ProjectileSpent || comp is { Weapon: null, OnlyCollideWhenShot: true })
            return;

        // it's here so this check is only done once before possible hit
        var attemptEv = new ProjectileReflectAttemptEvent(uid, comp, false);
        RaiseLocalEvent(target, ref attemptEv);
        if (attemptEv.Cancelled)
        {
            _projectile.SetShooter(uid, comp, target);
            return;
        }

        var shooter = comp.Shooter;
        var ev = new ProjectileHitEvent(comp.Damage * _damageable.UniversalProjectileDamageModifier, target, shooter);
        RaiseLocalEvent(uid, ref ev);

        var otherName = ToPrettyString(target);
        var damageRequired = _destructible.DestroyedAt(target);

        //KS14 pen start
        bool isAMob = HasComp<MobCollisionComponent>(target);
        if (comp.PenetrationThreshold > 0 && !isAMob) damageRequired /= _gunPenetrationMinShots;

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return;
        //KS14 pen end

        // KS14 Impact start
        // dont really like this but whatever
        if (_net.IsServer &&
            ent.Comp2.FixturesMass > float.Epsilon &&
            _standingStateSystem.IsDown(target))
            _physicsSystem.ApplyLinearImpulse(target, ent.Comp2.Momentum * _gunImpulse);
        // KS14 Impact end

        // KS14 - demonic penetrationcode start
        var multiplier = FixedPoint2.Zero;
        var maxmultiplier = FixedPoint2.Zero;
        var adjustedDamage = new DamageSpecifier();
        if (comp.PenetrationThreshold > 0 && !isAMob)
        {
            DamageModifierSetPrototype? targetDamageModifiers = null;
            if (damageable?.DamageModifierSetId != null)
            {
                _prototypeManager.Resolve(damageable.DamageModifierSetId, out targetDamageModifiers);
            }

            var multiplierValue = CalculateMultiplier(ev.Damage, targetDamageModifiers, damageRequired.Float());
            var maxMultiplierValue = CalculateMultiplier(ev.Damage, targetDamageModifiers, comp.PenetrationThreshold.Float() - comp.PenetrationAmount.Float());

            if (!IsFinite(multiplierValue) || !IsFinite(maxMultiplierValue))
            {
                Logger.Error(
                    "Encountered non-finite penetration multipliers for projectile {Projectile} hitting {Target}: required={Required}, multiplier={Multiplier}, maxMultiplier={MaxMultiplier}",
                    uid,
                    target,
                    damageRequired.Float(),
                    multiplierValue,
                    maxMultiplierValue);

                multiplierValue = 0f;
                maxMultiplierValue = 0f;
            }

            multiplier = multiplierValue;
            maxmultiplier = maxMultiplierValue;
            var quantizedMultiplier = QuantizeMultiplier(multiplierValue);
            var quantizedMaxMultiplier = QuantizeMultiplier(maxMultiplierValue);
            adjustedDamage = ev.Damage * FixedPoint2.Min(quantizedMultiplier, quantizedMaxMultiplier);
        }
        // KS14 - demonic penetrationcode end

        var deleted = Deleted(target);

        if (_damageable.TryChangeDamage((target, damageable), !adjustedDamage.Empty ? adjustedDamage : ev.Damage, out var damage, comp.IgnoreResistances, origin: shooter) && Exists(shooter)) //KS14
        {
            if (!deleted && _net.IsServer) // intentionally not predicting so you know if color flashes its 100% a hit
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
            }

            _adminLogger.Add(LogType.BulletHit,
                LogImpact.Medium,
                $"Projectile {ToPrettyString(uid):projectile} shot by {ToPrettyString(shooter):user} hit {otherName:target} and dealt {damage:damage} damage");

            comp.ProjectileSpent = !TryPenetrate((uid, comp), target, damage, damageRequired, multiplier); //KS14
        }
        else
        {
            comp.ProjectileSpent = true;
        }

        if (!deleted)
        {
            _gun.PlayImpactSound(target, damage, comp.SoundHit, comp.ForceSound);

            if (!ourBody.LinearVelocity.IsLengthZero() && _timing.IsFirstTimePredicted)
                _recoil.KickCamera(target, ourBody.LinearVelocity.Normalized());
        }

        if (comp.DeleteOnCollide && comp.ProjectileSpent)
            PredictedQueueDel(uid);

        if (comp.ImpactEffect != null && TryComp(uid, out TransformComponent? xform) && _timing.IsFirstTimePredicted)
        {
            RaiseLocalEvent(new ImpactEffectEvent(comp.ImpactEffect, GetNetCoordinates(xform.Coordinates)));
        }
    }
    private bool TryPenetrate(Entity<ProjectileComponent> projectile, EntityUid target, DamageSpecifier damage, FixedPoint2 damageRequired, FixedPoint2 multiplier) //KS14 - added multiplier
    {
        // If penetration is to be considered, we need to do some checks to see if the projectile should stop.
        if (projectile.Comp.PenetrationThreshold == 0)
            return false;

        // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
        if (projectile.Comp.PenetrationDamageTypeRequirement != null)
        {
            foreach (var requiredDamageType in projectile.Comp.PenetrationDamageTypeRequirement)
            {
                if (damage.DamageDict.Keys.Contains(requiredDamageType))
                    continue;

                return false;
            }
        }

        // If the object won't be destroyed, it "tanks" the penetration hit.
        if (FixedPoint2.Abs(damageRequired - damage.GetTotal()) > FixedPoint2.New(1)) //KS14, ugly code accounting for inaccuracies
        {
            return false;
        }

        if (!projectile.Comp.ProjectileSpent)
        {
            projectile.Comp.PenetrationAmount += damageRequired;
            // The projectile has dealt enough damage to be spent.
            if (projectile.Comp.PenetrationAmount >= projectile.Comp.PenetrationThreshold)
            {
                return false;
            }
        }

        return true;
    }
    // KS14 penetration demoncode start
    /// <summary>
    /// Calculates the multiplier needed to reach the requested effective damage.
    /// This uses the same exact per-damage-type branch logic as the Python reference solver.
    /// </summary>
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static FixedPoint2 QuantizeMultiplier(float multiplier)
    {
        if (!IsFinite(multiplier) || multiplier <= 0f)
            return FixedPoint2.Zero;

        return FixedPoint2.NewCeiling(multiplier);
    }

    public float CalculateMultiplier(
        DamageSpecifier damage,
        DamageModifierSet? modifierSet,
        float requiredEffectiveDamage)
    {
        if (!IsFinite(requiredEffectiveDamage) || requiredEffectiveDamage <= 0f)
            return 0f;

        if (modifierSet == null)
        {
            var totalDamage = damage.GetTotal().Float();
            return IsFinite(totalDamage) ? totalDamage / requiredEffectiveDamage : 0f;
        }

        var typeData = new List<(float BaseDamage, float FlatReduction, float PercentileMultiplier, float ZeroThreshold, float SwitchThreshold)>();
        var breakpoints = new HashSet<float> { 0f };

        foreach (var (type, baseFp) in damage.DamageDict)
        {
            float baseDamage = baseFp.Float();
            if (baseDamage <= 0f)
                continue;

            float flatReduction = 0f;
            float percentileResistCoeff = 0f;

            if (modifierSet != null)
            {
                modifierSet.FlatReduction.TryGetValue(type, out flatReduction);
                modifierSet.Coefficients.TryGetValue(type, out percentileResistCoeff);
            }

            float flatPen = 0f;
            float percentPen = 0f;

            damage.FlatPenetration?.TryGetValue(type, out flatPen);
            damage.PercentilePenetration?.TryGetValue(type, out percentPen);

            if (!damage.disableCrossInteraction)
                flatReduction *= 1f - percentPen;

            if (flatReduction > 0f)
                flatReduction = Math.Max(0f, flatReduction - flatPen);
            else
                flatReduction -= flatPen;

            float percentileMultiplier = 1f;
            if (percentileResistCoeff != 0f)
                percentileMultiplier = 1f - (1f - percentileResistCoeff) * (1f - percentPen);

            float zeroThreshold = flatReduction > 0f ? flatReduction / baseDamage : 0f;
            float switchThreshold = flatReduction > 0f && percentileMultiplier < 1f
                ? flatReduction / (baseDamage * (1f - percentileMultiplier))
                : float.PositiveInfinity;

            breakpoints.Add(zeroThreshold);
            if (switchThreshold != float.PositiveInfinity && switchThreshold > 0f)
                breakpoints.Add(switchThreshold);

            typeData.Add((baseDamage, flatReduction, percentileMultiplier, zeroThreshold, switchThreshold));
        }

        var sortedBreakpoints = breakpoints
            .Where(x => x >= 0f && x != float.PositiveInfinity)
            .OrderBy(x => x)
            .ToList();

        if (sortedBreakpoints.Count == 0)
            return 0f;

        float EvaluateTotalDamage(float multiplier)
        {
            if (!IsFinite(multiplier))
                return float.NaN;

            float totalDamage = 0f;
            foreach (var (baseDamage, flatReduction, percentileMultiplier, _, _) in typeData)
            {
                float flatValue = Math.Max(0f, baseDamage * multiplier - flatReduction);
                float percentileValue = baseDamage * multiplier * percentileMultiplier;
                float contribution = Math.Min(flatValue, percentileValue);
                if (!IsFinite(contribution))
                    return float.NaN;

                totalDamage += contribution;
            }

            return IsFinite(totalDamage) ? totalDamage : float.NaN;
        }

        float previousMultiplier = 0f;
        float previousDamage = EvaluateTotalDamage(previousMultiplier);
        if (!IsFinite(previousDamage))
        {
            Logger.Error("Encountered non-finite initial damage evaluation while calculating penetration multiplier for damage {Damage} and required {Required}", damage.GetTotal().Float(), requiredEffectiveDamage);
            return 0f;
        }

        foreach (var nextMultiplier in sortedBreakpoints)
        {
            if (nextMultiplier <= previousMultiplier)
                continue;

            float nextDamage = EvaluateTotalDamage(nextMultiplier);
            if (!IsFinite(nextDamage))
            {
                Logger.Error("Encountered non-finite damage evaluation at breakpoint {Breakpoint} while calculating penetration multiplier for damage {Damage} and required {Required}", nextMultiplier, damage.GetTotal().Float(), requiredEffectiveDamage);
                break;
            }

            if (previousDamage >= requiredEffectiveDamage)
                return previousMultiplier;

            if (requiredEffectiveDamage <= nextDamage)
            {
                if (Math.Abs(nextDamage - previousDamage) < 1e-6f)
                    return nextMultiplier;

                var interpolatedMultiplier = previousMultiplier + (requiredEffectiveDamage - previousDamage) * (nextMultiplier - previousMultiplier) / (nextDamage - previousDamage);
                return IsFinite(interpolatedMultiplier) ? interpolatedMultiplier : 0f;
            }

            previousMultiplier = nextMultiplier;
            previousDamage = nextDamage;
        }

        if (previousDamage >= requiredEffectiveDamage)
            return previousMultiplier;

        float stepMultiplier = previousMultiplier + Math.Max(1f, requiredEffectiveDamage);
        float stepDamage = EvaluateTotalDamage(stepMultiplier);
        if (!IsFinite(stepDamage))
        {
            Logger.Error("Encountered non-finite damage evaluation at step multiplier {StepMultiplier} while calculating penetration multiplier for damage {Damage} and required {Required}", stepMultiplier, damage.GetTotal().Float(), requiredEffectiveDamage);
            return 0f;
        }

        if (Math.Abs(stepDamage - previousDamage) < 1e-6f)
            return previousMultiplier;

        var finalMultiplier = previousMultiplier + (requiredEffectiveDamage - previousDamage) * (stepMultiplier - previousMultiplier) / (stepDamage - previousDamage);
        return IsFinite(finalMultiplier) ? finalMultiplier : 0f;
    }
    //KS14 penetration demoncode end
}
