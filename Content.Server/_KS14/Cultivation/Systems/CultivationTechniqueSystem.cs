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
    private static readonly SoundSpecifier FireSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    private static readonly SoundSpecifier WaterSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier HealSound = new SoundPathSpecifier("/Audio/Magic/staff_healing.ogg");
    private static readonly SoundSpecifier WaterRushSound = new SoundPathSpecifier("/Audio/_KS14/Weapons/Guns/FarsoundGunshots/water_spray.ogg");
    private static readonly SoundSpecifier WindSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/qinggong.ogg");
    private static readonly SoundSpecifier SwordDrawSound = new SoundPathSpecifier("/Audio/Items/unsheath.ogg");
    private static readonly SoundSpecifier SloshSound = new SoundCollectionSpecifier("KsCultivationSlosh");
    private static readonly SoundSpecifier RustleSound = new SoundCollectionSpecifier("KsCultivationRustle");
    private static readonly SoundSpecifier EarthSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly EntProtoId TideEffect = "KsFxTide";
    private static readonly EntProtoId FallingRockEffect = "KsFxFallingRock";

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
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    #region Helpers

    /// <summary>
    ///     Living mobs in range, not counting the user.
    /// </summary>
    private List<EntityUid> MobsAround(EntityUid userUid, float range)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(userUid).Coordinates, range, _mobs);

        var found = new List<EntityUid>();
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner != userUid && !_mobStateSystem.IsDead(mobEntity.Owner, component: mobEntity.Comp))
                found.Add(mobEntity.Owner);
        }

        return found;
    }

    private void FireBlade(EntityUid userUid, EntProtoId projectile, Vector2 direction, float speed)
    {
        var fromMap = _transformSystem.GetMapCoordinates(userUid);
        var bladeUid = Spawn(projectile, fromMap);
        _cultivationSystem.EmpowerProjectile(bladeUid, userUid);
        _gunSystem.ShootProjectile(bladeUid, direction, _physicsSystem.GetMapLinearVelocity(userUid), userUid, user: userUid, speed: speed);
    }

    private Vector2 DirectionTo(EntityUid userUid, EntityCoordinates targetUid)
    {
        var direction = _transformSystem.ToMapCoordinates(targetUid).Position - _transformSystem.GetMapCoordinates(userUid).Position;
        return direction.LengthSquared() > 0.0001f ? direction : new Vector2(0f, 1f);
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        return new Angle(MathHelper.DegreesToRadians(degrees)).RotateVec(vector);
    }

    /// <summary>
    ///     Throw someone away from a point.
    /// </summary>
    private void ThrowAway(EntityUid victimUid, EntityUid from, float distance, EntityUid userUid)
    {
        var direction = _transformSystem.GetMapCoordinates(victimUid).Position - _transformSystem.GetMapCoordinates(from).Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(victimUid, Vector2.Normalize(direction) * distance, baseThrowSpeed: 8f, user: userUid, recoil: false);
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

        _audioSystem.PlayPvs(SwordDrawSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnSwordRain(Entity<CultivatorComponent> entity, ref CultivationSwordRainActionEvent args)
    {
        if (args.Handled || args.Count <= 0 || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtSwordRain, args.Target);
        _visualsSystem.Wave(entity, FxColour.Metal, big: true);
        var direction = DirectionTo(entity, args.Target);

        for (var i = 0; i < args.Count; i++)
        {
            var offset = args.Count == 1 ? 0f : -args.Spread / 2f + args.Spread * (float)i / (float)(args.Count - 1);
            FireBlade(entity, args.Projectile, Rotate(direction, offset), args.Speed);
        }

        _audioSystem.PlayPvs(SwordDrawSound, entity);
        _audioSystem.PlayPvs(WindSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-sword-rain", ("user", entity.Owner)), type: PopupType.Medium);
    }

    #endregion

    #region Fire

    [SubscribeLocalEvent]
    private void OnBurningPalm(Entity<CultivatorComponent> entity, ref CultivationBurningPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.FireBurst(args.Target, 5);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        var stacks = (_sharedCultivationSystem.HasRoot(entity, CultivationElement.Fire) ? args.FireStacks * 2f : args.FireStacks) * _cultivationSystem.Power(entity);

        _damageableSystem.TryChangeDamage(args.Target, _cultivationSystem.Empowered(entity, CultivationSystem.Damage("Heat", args.HeatDamage)), origin: entity);
        _flammableSystem.AdjustFireStacks(args.Target, stacks, ignite: true);

        _audioSystem.PlayPvs(FireSound, entity);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-burning-palm", ("user", entity.Owner), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnSeaOfFlames(Entity<CultivatorComponent> entity, ref CultivationSeaOfFlamesActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.FireRing(entity, 3f, 16, 22);
        _visualsSystem.Wave(entity, FxColour.Fire, big: true);
        foreach (var victimUid in MobsAround(entity, args.Range))
        {
            if (!_sharedCultivationSystem.IsAlly(entity, victimUid))
                _flammableSystem.AdjustFireStacks(victimUid, args.FireStacks * _cultivationSystem.Power(entity), ignite: true);
        }

        _audioSystem.PlayPvs(FireSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-sea-of-flames", ("user", entity.Owner)), type: PopupType.LargeCaution);
    }

    #endregion

    #region Water

    [SubscribeLocalEvent]
    private void OnStillWaterWard(Entity<CultivatorComponent> entity, ref CultivationStillWaterWardActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Mist(entity, CultivationVisualsSystem.MistWaterEffect);
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, entity);
        var releaseWave = _sharedCultivationSystem.HasRoot(entity, CultivationElement.Water);
        _cultivationStanceSystem.ApplyWard(entity, args.Duration, args.DamageMultiplier, releaseWave);

        _audioSystem.PlayPvs(SloshSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-still-water", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnWardReleased(Entity<CultivatorComponent> entity, ref StillWaterWardReleasedEvent args)
    {
        if (args.Stored < 5f)
            return;

        var damage = _cultivationSystem.Empowered(entity, CultivationSystem.Damage("Blunt", MathF.Min(args.Stored, WardWaveMaxDamage)));
        foreach (var victimUid in MobsAround(entity, WardWaveRange))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victimUid))
                continue;

            _damageableSystem.TryChangeDamage(victimUid, damage, origin: entity);
            ThrowAway(victimUid, entity, 2f, entity);
        }

        _audioSystem.PlayPvs(WaterSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-ward-wave", ("user", entity.Owner)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnTidalSurge(Entity<CultivatorComponent> entity, ref CultivationTidalSurgeActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Wave(entity, FxColour.Water, big: true);
        _visualsSystem.WaterRing(entity, args.Range, 14);
        SpawnAttachedTo(TideEffect, new EntityCoordinates(entity, Vector2.Zero));
        foreach (var victimUid in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victimUid))
                continue;

            ThrowAway(victimUid, entity, args.ThrowDistance, entity);
            _stunSystem.TryKnockdown(victimUid, args.Knockdown * MathF.Sqrt(_cultivationSystem.Power(entity)));
            _damageableSystem.TryChangeDamage(victimUid, _cultivationSystem.Empowered(entity, CultivationSystem.Damage("Blunt", 8f)), origin: entity);
        }

        _audioSystem.PlayPvs(WaterSound, entity);
        _audioSystem.PlayPvs(WaterRushSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-tidal-surge", ("user", entity.Owner)), type: PopupType.LargeCaution);
    }

    #endregion

    #region Wood

    [SubscribeLocalEvent]
    private void OnSpringMending(Entity<CultivatorComponent> entity, ref CultivationSpringMendingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.WoodBloom(args.Target, 0f, 5);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _damageableSystem.HealEvenly(args.Target, FixedPoint2.New(-args.Heal * _cultivationSystem.Power(entity)), origin: entity);

        if (_sharedCultivationSystem.HasRoot(entity, CultivationElement.Wood))
        {
            _sharedCultivationSystem.HealInternalInjury(args.Target);
            if (args.Target != entity.Owner)
                _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-args.Heal * _cultivationSystem.Power(entity) / 2f), origin: entity);
        }

        _audioSystem.PlayPvs(HealSound, args.Target);
        _audioSystem.PlayPvs(RustleSound, args.Target);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-spring-mending", ("user", entity.Owner), ("target", args.Target)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnMyriadSpring(Entity<CultivatorComponent> entity, ref CultivationMyriadSpringActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.WoodBloom(entity, 2.5f, 18);
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, entity);
        _visualsSystem.Wave(entity, FxColour.Wood, big: true);
        _visualsSystem.Motes(entity, FxColour.Wood, TimeSpan.FromSeconds(3));
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-args.Heal * _cultivationSystem.Power(entity)), origin: entity);
        foreach (var friendUid in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, friendUid))
                _damageableSystem.HealEvenly(friendUid, FixedPoint2.New(-args.Heal * _cultivationSystem.Power(entity)), origin: entity);
        }

        _audioSystem.PlayPvs(HealSound, entity);
        _audioSystem.PlayPvs(RustleSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-myriad-spring", ("user", entity.Owner)), type: PopupType.Medium);
    }

    #endregion

    #region Earth

    [SubscribeLocalEvent]
    private void OnRootedStance(Entity<CultivatorComponent> entity, ref CultivationRootedStanceActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtFissures, entity);
        var damageMultiplier = _sharedCultivationSystem.HasRoot(entity, CultivationElement.Earth)
            ? args.RootDamageMultiplier
            : args.DamageMultiplier;

        _cultivationStanceSystem.ApplyRootedStance(entity, args.Duration, damageMultiplier, args.SpeedMultiplier);

        _audioSystem.PlayPvs(EarthSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-rooted-stance", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnMountainCollapse(Entity<CultivatorComponent> entity, ref CultivationMountainCollapseActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Wave(entity, FxColour.Earth, big: true);
        _visualsSystem.Shockwave(_transformSystem.GetMapCoordinates(entity), strength: 2f);
        _visualsSystem.Play(CultivationVisualsSystem.MountainFallSound, entity);
        var damage = _cultivationSystem.Empowered(entity, CultivationSystem.Damage("Blunt", args.BluntDamage));
        foreach (var victimUid in MobsAround(entity, args.Range))
        {
            if (_sharedCultivationSystem.IsAlly(entity, victimUid))
                continue;

            // A boulder comes down on each of them out of nowhere.
            var landing = _transformSystem.GetMapCoordinates(victimUid);
            Spawn(FallingRockEffect, landing);
            _visualsSystem.Rubble(landing);
            _damageableSystem.TryChangeDamage(victimUid, damage, origin: entity);
            _stunSystem.TryKnockdown(victimUid, args.Knockdown);
        }

        _audioSystem.PlayPvs(EarthSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-mountain-collapse", ("user", entity.Owner)), type: PopupType.LargeCaution);
    }

    #endregion
}
