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
using Robust.Shared.Log;

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
        if (comp.PenetrationThreshold > 0) damageRequired /= _gunPenetrationMinShots;

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return;
        //KS14 pen end

        // KS14 Impact start
        if (_net.IsServer &&
            ent.Comp2.FixturesMass > float.Epsilon)
            _physicsSystem.ApplyLinearImpulse(target, ent.Comp2.Momentum * _gunImpulse);
        // KS14 Impact end

        // KS14 - demonic penetrationcode start
        var multiplier = FixedPoint2.Zero;
        var maxmultiplier = FixedPoint2.Zero;
        var adjustedDamage = new DamageSpecifier();
        if (comp.PenetrationThreshold > 0)
        {
            DamageModifierSetPrototype? targetDamageModifiers = null;
            if (damageable?.DamageModifierSetId != null)
            {
                _prototypeManager.Resolve(damageable.DamageModifierSetId, out targetDamageModifiers);
            }

            multiplier = CalculateMultiplier(ev.Damage, targetDamageModifiers, damageRequired.Float());
            maxmultiplier = CalculateMultiplier(ev.Damage, targetDamageModifiers, comp.PenetrationThreshold.Float()-comp.PenetrationAmount.Float());
            adjustedDamage = ev.Damage * FixedPoint2.Min(multiplier, maxmultiplier);
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
    /// Calculates the minimum multiplier needed for a projectile to deal
    /// a desired amount of post-mitigation damage after all DamageSpecifier
    /// interactions are applied.
    ///
    /// This exists because projectile penetration (passing through objects)
    /// becomes inconsistent once multiple damage types, flat reductions,
    /// percentile reductions, and penetration values are all interacting.
    ///
    /// The function reproduces DamageSpecifier mitigation logic, measures how
    /// much damage would actually survive armor, then calculates the multiplier
    /// required to reach the requested final damage amount.
    /// </summary>
    public static float CalculateMultiplier(
    DamageSpecifier damage,
    DamageModifierSet? modifierSet,
    float requiredEffectiveDamage)
    {
        Logger.Info($"calculating mul. required damage = {requiredEffectiveDamage}");
        if (requiredEffectiveDamage <= 0f)
            return 0f;

        //Don't know how to name these. I'll try explain in comments what each one is, but I am bad at explaining.
        //Basically I made equations out of all the damage specifier behaviors and solved for x and these sums are needed.
        float sum1 = 0f; // sumof(flatdamagereductionperdamagetype*(1-percentilepenetrationperdamagetype) - flatpenetrationperdamagetype)
        float sum2 = 0f; // sumof(damageperdamagetype)
        float sum3 = 0f; // sumof(damageperdamagetype*(1-percentiledamagereductionperdamagetype*(1-percentilepentrationperdamagetype)))

        foreach (var (type, baseFp) in damage.DamageDict)
        {
            float baseDamage = baseFp.Float();
            if (baseDamage <= 0f)
                continue;

            // just assign things
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

            // sum 1
            if (!damage.disableCrossInteraction)
                flatReduction *= 1f - percentPen;
            sum1 += flatReduction - flatPen;

            // sum 2
            sum2 += baseDamage;

            // sum 3
            sum3 += baseDamage * (1f - percentileResistCoeff * (1f - percentPen));
        }

        float mulFlat = (requiredEffectiveDamage + sum1) / sum2;
        float mulPerc = requiredEffectiveDamage / sum3;

        var totalDamFlat = mulFlat * damage;
        var totalDamPerc = mulPerc * damage;
        Logger.Info($"calculated muls. mul flat: {mulFlat} mul perc: {mulPerc} total damage flat {totalDamFlat.GetTotal()} total damage percentile {totalDamPerc.GetTotal()}");

        return Math.Max(mulFlat, mulPerc);
    }
    //KS14 penetration demoncode end
}
