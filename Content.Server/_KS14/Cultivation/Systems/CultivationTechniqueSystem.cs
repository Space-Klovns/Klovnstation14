using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The techniques of the five laws. Each one is paid for through <see cref="CultivationSystem.TryUseTechnique"/>,
///         and each law's Root twist is checked with <see cref="SharedCultivationSystem.HasRoot"/>.
/// </summary>
public sealed partial class CultivationTechniqueSystem : EntitySystem
{
    private static readonly SoundSpecifier SwordSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/Magic/staff_chaos.ogg");
    private static readonly SoundSpecifier FireSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    private static readonly SoundSpecifier WaterSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier HealSound = new SoundPathSpecifier("/Audio/Magic/staff_healing.ogg");
    private static readonly SoundSpecifier EarthSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");

    /// <summary>
    ///     How far a Water Root's released wave reaches.
    /// </summary>
    private const float WardWaveRange = 1.5f;

    private const float WardWaveMaxDamage = 30f;

    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private CultivationStanceSystem _cultivationStanceSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    #region Helpers

    /// <summary>
    ///     Living mobs in range, not counting the user.
    /// </summary>
    private List<EntityUid> MobsAround(EntityUid user, float range)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(user).Coordinates, range, _mobs);

        var found = new List<EntityUid>();
        foreach (var mob in _mobs)
        {
            if (mob.Owner != user && !_mobStateSystem.IsDead(mob.Owner, mob.Comp))
                found.Add(mob.Owner);
        }

        return found;
    }

    private void FireBlade(EntityUid user, EntProtoId projectile, Vector2 direction, float speed)
    {
        var fromMap = _transformSystem.GetMapCoordinates(user);
        var bladeUid = Spawn(projectile, fromMap);
        _gunSystem.ShootProjectile(bladeUid, direction, _physicsSystem.GetMapLinearVelocity(user), user, user, speed);
    }

    private Vector2 DirectionTo(EntityUid user, EntityCoordinates target)
    {
        var direction = _transformSystem.ToMapCoordinates(target).Position - _transformSystem.GetMapCoordinates(user).Position;
        return direction.LengthSquared() > 0.0001f ? direction : new Vector2(0f, 1f);
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        return new Angle(MathHelper.DegreesToRadians(degrees)).RotateVec(vector);
    }

    /// <summary>
    ///     Throw someone away from a point.
    /// </summary>
    private void ThrowAway(EntityUid victim, EntityUid from, float distance, EntityUid user)
    {
        var direction = _transformSystem.GetMapCoordinates(victim).Position - _transformSystem.GetMapCoordinates(from).Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(victim, Vector2.Normalize(direction) * distance, baseThrowSpeed: 8f, user: user, recoil: false);
    }

    #endregion

    #region Metal

    [SubscribeLocalEvent]
    private void OnFlyingSword(Entity<CultivatorComponent> entity, ref CultivationFlyingSwordActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var direction = DirectionTo(entity, args.Target);

        FireBlade(entity, args.Projectile, direction, args.Speed);
        if (_sharedCultivationSystem.HasRoot(entity, CultivationElement.Metal))
        {
            FireBlade(entity, args.Projectile, Rotate(direction, args.RootSpread), args.Speed);
            FireBlade(entity, args.Projectile, Rotate(direction, -args.RootSpread), args.Speed);
        }

        _audioSystem.PlayPvs(SwordSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnSwordRain(Entity<CultivatorComponent> entity, ref CultivationSwordRainActionEvent args)
    {
        if (args.Handled || args.Count <= 0 || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var direction = DirectionTo(entity, args.Target);

        for (var i = 0; i < args.Count; i++)
        {
            var offset = args.Count == 1 ? 0f : -args.Spread / 2f + args.Spread * i / (args.Count - 1);
            FireBlade(entity, args.Projectile, Rotate(direction, offset), args.Speed);
        }

        _audioSystem.PlayPvs(SwordSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-sword-rain", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    #endregion

    #region Fire

    [SubscribeLocalEvent]
    private void OnBurningPalm(Entity<CultivatorComponent> entity, ref CultivationBurningPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var stacks = _sharedCultivationSystem.HasRoot(entity, CultivationElement.Fire) ? args.FireStacks * 2f : args.FireStacks;

        _damageableSystem.TryChangeDamage(args.Target, CultivationSystem.Damage("Heat", args.HeatDamage), origin: entity);
        _flammableSystem.AdjustFireStacks(args.Target, stacks, ignite: true);

        _audioSystem.PlayPvs(FireSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-burning-palm", ("user", entity.Owner), ("target", args.Target)), args.Target, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnSeaOfFlames(Entity<CultivatorComponent> entity, ref CultivationSeaOfFlamesActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        foreach (var victim in MobsAround(entity, args.Range))
        {
            if (!_sharedCultivationSystem.IsAlly(entity, victim))
                _flammableSystem.AdjustFireStacks(victim, args.FireStacks, ignite: true);
        }

        _audioSystem.PlayPvs(FireSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-sea-of-flames", ("user", entity.Owner)), entity, PopupType.LargeCaution);
    }

    #endregion

    #region Water

    [SubscribeLocalEvent]
    private void OnStillWaterWard(Entity<CultivatorComponent> entity, ref CultivationStillWaterWardActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var releaseWave = _sharedCultivationSystem.HasRoot(entity, CultivationElement.Water);
        _cultivationStanceSystem.ApplyWard(entity, args.Duration, args.DamageMultiplier, releaseWave);

        _audioSystem.PlayPvs(WaterSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-still-water", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnWardReleased(Entity<CultivatorComponent> entity, ref StillWaterWardReleasedEvent args)
    {
        if (args.Stored < 5f)
            return;

        var damage = CultivationSystem.Damage("Blunt", MathF.Min(args.Stored, WardWaveMaxDamage));
        foreach (var victim in MobsAround(entity, WardWaveRange))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victim))
                continue;

            _damageableSystem.TryChangeDamage(victim, damage, origin: entity);
            ThrowAway(victim, entity, 2f, entity);
        }

        _audioSystem.PlayPvs(WaterSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-ward-wave", ("user", entity.Owner)), entity, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnTidalSurge(Entity<CultivatorComponent> entity, ref CultivationTidalSurgeActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        foreach (var victim in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victim))
                continue;

            ThrowAway(victim, entity, args.ThrowDistance, entity);
            _stunSystem.TryKnockdown(victim, args.Knockdown);
        }

        _audioSystem.PlayPvs(WaterSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-tidal-surge", ("user", entity.Owner)), entity, PopupType.LargeCaution);
    }

    #endregion

    #region Wood

    [SubscribeLocalEvent]
    private void OnSpringMending(Entity<CultivatorComponent> entity, ref CultivationSpringMendingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _damageableSystem.HealEvenly(args.Target, FixedPoint2.New(-args.Heal), origin: entity);

        if (_sharedCultivationSystem.HasRoot(entity, CultivationElement.Wood))
        {
            _sharedCultivationSystem.HealInternalInjury(args.Target);
            if (args.Target != entity.Owner)
                _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-args.Heal / 2f), origin: entity);
        }

        _audioSystem.PlayPvs(HealSound, args.Target);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-spring-mending", ("user", entity.Owner), ("target", args.Target)), args.Target, PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnMyriadSpring(Entity<CultivatorComponent> entity, ref CultivationMyriadSpringActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-args.Heal), origin: entity);
        foreach (var friend in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, friend))
                _damageableSystem.HealEvenly(friend, FixedPoint2.New(-args.Heal), origin: entity);
        }

        _audioSystem.PlayPvs(HealSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-myriad-spring", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    #endregion

    #region Earth

    [SubscribeLocalEvent]
    private void OnRootedStance(Entity<CultivatorComponent> entity, ref CultivationRootedStanceActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var damageMultiplier = _sharedCultivationSystem.HasRoot(entity, CultivationElement.Earth)
            ? args.RootDamageMultiplier
            : args.DamageMultiplier;

        _cultivationStanceSystem.ApplyRootedStance(entity, args.Duration, damageMultiplier, args.SpeedMultiplier);

        _audioSystem.PlayPvs(EarthSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-rooted-stance", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnMountainCollapse(Entity<CultivatorComponent> entity, ref CultivationMountainCollapseActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var damage = CultivationSystem.Damage("Blunt", args.BluntDamage);
        foreach (var victim in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victim))
                continue;

            _damageableSystem.TryChangeDamage(victim, damage, origin: entity);
            _stunSystem.TryKnockdown(victim, args.Knockdown);
        }

        _audioSystem.PlayPvs(EarthSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-mountain-collapse", ("user", entity.Owner)), entity, PopupType.LargeCaution);
    }

    #endregion
}
