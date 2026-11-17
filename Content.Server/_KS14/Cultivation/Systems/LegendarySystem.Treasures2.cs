using System.Numerics;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Six more legendary treasures, each with one clear gimmick and one clear counter: the Xuanyuan Sword, the Green
///         Dragon Crescent Blade, the Seven-Star Sword, the Linglong Pagoda, the Universe Ring and the Wind Fire Wheels.
/// </summary>
public sealed partial class LegendarySystem
{
    private static readonly EntProtoId ChemicalFirePrototype = "ChemicalFire";
    private static readonly EntProtoId MoltenStepEffect = "KsFxMoltenStep";

    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private Robust.Server.GameObjects.PointLightSystem _pointLightSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;

    #region Xuanyuan Sword

    public bool IsSonOfHeaven(EntityUid uid)
    {
        return HasComp<SonOfHeavenComponent>(uid);
    }

    /// <summary>
    ///     The sword settles into the mandated hand; anyone else is burned by its authority.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnXuanyuanEquipped(Entity<XuanyuanSwordComponent> entity, ref GotEquippedHandEvent args)
    {
        if (IsSonOfHeaven(args.User))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-xuanyuan-accepts"), PopupType.Medium);
            _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, args.User);
            return;
        }

        _damageableSystem.TryChangeDamage(args.User, CultivationSystem.Damage("Heat", entity.Comp.UnworthyBurn), ignoreResistances: true);
        _noticeSystem.Notify(args.User, Loc.GetString("legendary-xuanyuan-rejects"), PopupType.LargeCaution);
    }

    #endregion

    #region The treasures' own blows

    [SubscribeLocalEvent]
    private void OnTreasureMeleeHit(Entity<LegendaryTreasureHitComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var victimUid in args.HitEntities)
        {
            if (!HasComp<MobStateComponent>(victimUid))
                continue;

            if (TryComp<XuanyuanSwordComponent>(entity, out var xuanyuanComponent))
            {
                if (!IsSonOfHeaven(args.User) || !LegendaryHit(args.User, victimUid, xuanyuanComponent.WorthyDamage, TimeSpan.FromSeconds(1), entity))
                    continue;

                _staminaSystem.TakeStaminaDamage(victimUid, 20f, source: args.User);
                _visualsSystem.Wave(victimUid, FxColour.Gold);
                _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, victimUid, -8f);
            }
            else if (HasComp<GreenDragonBladeComponent>(entity))
            {
                if (!TryComp<WieldableComponent>(entity, out var wieldableComponent) || !wieldableComponent.Wielded)
                    continue;

                if (LegendaryHit(args.User, victimUid, HonourDamage(args.User), TimeSpan.Zero, entity))
                    _visualsSystem.Spark(victimUid, FxColour.Wood);
            }
            else if (TryComp<SevenStarSwordComponent>(entity, out var starComponent))
            {
                if (!LegendaryHit(args.User, victimUid, 8f, TimeSpan.Zero, entity) || starComponent.Stars >= SevenStarSwordComponent.MaxStars)
                    continue;

                starComponent.Stars++;
                _pointLightSystem.SetEnergy(entity, 0.3f * starComponent.Stars);
                _visualsSystem.Spark(args.User, FxColour.Gold);
                _noticeSystem.Notify(args.User, starComponent.Stars == SevenStarSwordComponent.MaxStars
                    ? Loc.GetString("legendary-seven-star-all")
                    : Loc.GetString("legendary-seven-star-lit", ("stars", starComponent.Stars)));
            }
        }
    }

    #endregion

    #region Green Dragon Crescent Blade

    /// <summary>
    ///     The blade's bite follows its wielder's honour: 4 for the disgraced, up to 24 for a martial legend.
    /// </summary>
    public float HonourDamage(EntityUid userUid)
    {
        return Math.Clamp(10f + _jianghuSystem.GetFace(userUid) / 2f, 4f, 24f);
    }

    [SubscribeLocalEvent]
    private void OnBladeWielded(Entity<GreenDragonBladeComponent> entity, ref ItemWieldedEvent args)
    {
        SetReach(entity, entity.Comp.WieldedReach);
    }

    [SubscribeLocalEvent]
    private void OnBladeUnwielded(Entity<GreenDragonBladeComponent> entity, ref ItemUnwieldedEvent args)
    {
        SetReach(entity, entity.Comp.UnwieldedReach);
    }

    private void SetReach(EntityUid uid, float reach)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var meleeComponent))
            return;

        meleeComponent.Range = reach;
        Dirty(uid, meleeComponent);
    }

    [SubscribeLocalEvent]
    private void OnBladeExamined(Entity<GreenDragonBladeComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("legendary-dragon-blade-honour", ("damage", (int)HonourDamage(args.Examiner)),
            ("face", _jianghuSystem.GetFace(args.Examiner))));
    }

    #endregion

    #region Seven-Star Sword

    [SubscribeLocalEvent]
    private void OnSevenStars(Entity<SevenStarSwordComponent> entity, ref SevenStarsActionEvent args)
    {
        if (args.Handled)
            return;

        if (entity.Comp.Stars < SevenStarSwordComponent.MaxStars)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-seven-star-unlit", ("stars", entity.Comp.Stars)));
            return;
        }

        var from = _transformSystem.GetMapCoordinates(args.Performer);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        args.Handled = true;
        entity.Comp.Stars = 0;
        _pointLightSystem.SetEnergy(entity, 0f);
        Shout(args.Performer, "legendary-seven-star-shout");
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, args.Performer);
        direction = Vector2.Normalize(direction);
        var hit = new HashSet<EntityUid>();
        for (var step = 1; step <= SevenStarSwordComponent.MaxStars; step++)
        {
            var point = new MapCoordinates(from.Position + direction * step, from.MapId);
            _visualsSystem.Art(CultivationVisualsSystem.ArtGoldMotes, point);
            _visualsSystem.WaveAt(point, FxColour.Gold);
            _mobs.Clear();
            _entityLookupSystem.GetEntitiesInRange(point, 0.6f, _mobs);
            foreach (var victim in _mobs)
            {
                if (hit.Add(victim.Owner))
                    LegendaryHit(args.Performer, victim.Owner, 22f, TimeSpan.FromSeconds(1), entity);
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnSevenStarExamined(Entity<SevenStarSwordComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("legendary-seven-star-examine", ("stars", entity.Comp.Stars)));
    }

    #endregion

    #region Linglong Pagoda

    /// <summary>
    ///     Thrown at someone, it swells into a towering pagoda and slams down over them. A qi ward or the Golden Bell
    ///         turns it aside.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPagodaHit(Entity<LinglongPagodaComponent> entity, ref ThrowDoHitEvent args)
    {
        if (entity.Comp.Sealed != null || _gameTiming.CurTime < entity.Comp.NextSeal || !HasComp<MobStateComponent>(args.Target))
            return;

        if (HasComp<StillWaterWardComponent>(args.Target) || HasComp<GoldenBellQiComponent>(args.Target) || HasComp<GoldenBellComponent>(args.Target))
        {
            _noticeSystem.Announce(args.Target, Loc.GetString("legendary-pagoda-bounces", ("item", entity.Owner), ("target", args.Target)), type: PopupType.Small);
            return;
        }

        Seal(entity, args.Target);
    }

    public void Seal(Entity<LinglongPagodaComponent> entity, EntityUid victimUid)
    {
        var container = _containerSystem.EnsureContainer<Robust.Shared.Containers.ContainerSlot>(entity, LinglongPagodaComponent.ContainerId);
        var coordinates = Transform(victimUid).Coordinates;
        _transformSystem.SetCoordinates(entity, coordinates);
        if (!_containerSystem.Insert(victimUid, container, force: true))
            return;

        var now = _gameTiming.CurTime;
        entity.Comp.Sealed = victimUid;
        entity.Comp.ReleaseAt = now + entity.Comp.SealTime;
        entity.Comp.NextSeal = now + entity.Comp.Cooldown;
        _transformSystem.AnchorEntity(entity);
        _visualsSystem.Play(CultivationVisualsSystem.GreatBellSound, entity, -6f);
        _visualsSystem.Wave(entity, FxColour.Gold, big: true);
        _noticeSystem.Announce(entity, Loc.GetString("legendary-pagoda-seals", ("item", entity.Owner), ("target", victimUid)), type: PopupType.LargeCaution);
    }

    public void ReleasePagoda(Entity<LinglongPagodaComponent> entity)
    {
        var freedUid = entity.Comp.Sealed;
        entity.Comp.Sealed = null;
        if (Transform(entity).Anchored)
            _transformSystem.Unanchor(entity);

        if (!_containerSystem.TryGetContainer(entity, LinglongPagodaComponent.ContainerId, out var container) || container.ContainedEntities.Count == 0)
            return;

        _containerSystem.EmptyContainer(container, force: true);
        if (freedUid is { } uid)
        {
            // Out they stagger, their head ringing.
            _staminaSystem.TakeStaminaDamage(uid, 40f);
            _noticeSystem.Notify(uid, Loc.GetString("legendary-pagoda-released"), PopupType.Medium);
        }
    }

    private void UpdatePagodas(TimeSpan now)
    {
        var query = EntityQueryEnumerator<LinglongPagodaComponent>();
        while (query.MoveNext(out var uid, out var pagodaComponent))
        {
            if (pagodaComponent.Sealed != null && now >= pagodaComponent.ReleaseAt)
                ReleasePagoda((uid, pagodaComponent));
        }
    }

    [SubscribeLocalEvent]
    private void OnPagodaExamined(Entity<LinglongPagodaComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.Sealed != null)
            args.PushMarkup(Loc.GetString("legendary-pagoda-occupied"));
        else if (_gameTiming.CurTime < entity.Comp.NextSeal)
            args.PushMarkup(Loc.GetString("legendary-pagoda-closed"));
    }

    #endregion

    #region Universe Ring

    [SubscribeLocalEvent]
    private void OnRingHit(Entity<UniverseRingComponent> entity, ref ThrowDoHitEvent args)
    {
        // A fresh throw by someone new starts the ricochets over.
        if (args.Component.Thrower is { } throwerUid && !entity.Comp.Returning && entity.Comp.Thrower != throwerUid)
        {
            entity.Comp.Thrower = throwerUid;
            entity.Comp.BouncesLeft = 2;
            entity.Comp.HitThisThrow.Clear();
        }

        if (entity.Comp.Thrower is not { } ownerUid || args.Target == ownerUid || !HasComp<MobStateComponent>(args.Target))
            return;

        if (entity.Comp.Returning)
            return;

        entity.Comp.HitThisThrow.Add(args.Target);
        if (LegendaryHit(ownerUid, args.Target, entity.Comp.Damage, TimeSpan.FromSeconds(0.5), entity))
        {
            _visualsSystem.Wave(args.Target, FxColour.Gold);
            _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, args.Target, -10f);
        }

        entity.Comp.NextFlight = _gameTiming.CurTime + TimeSpan.FromSeconds(0.2);
    }

    [SubscribeLocalEvent]
    private void OnRingLanded(Entity<UniverseRingComponent> entity, ref LandEvent args)
    {
        if (entity.Comp.Thrower == null)
        {
            if (args.User is { } throwerUid)
            {
                entity.Comp.Thrower = throwerUid;
                entity.Comp.BouncesLeft = 0;
            }
            else
            {
                return;
            }
        }

        entity.Comp.NextFlight = _gameTiming.CurTime + TimeSpan.FromSeconds(0.2);
    }

    /// <summary>
    ///     Ricochet to the next victim, or fly home and into a free hand.
    /// </summary>
    private void UpdateRings(TimeSpan now)
    {
        var query = EntityQueryEnumerator<UniverseRingComponent>();
        while (query.MoveNext(out var uid, out var ringComponent))
        {
            if (ringComponent.NextFlight == TimeSpan.Zero || now < ringComponent.NextFlight || ringComponent.Thrower is not { } throwerUid)
                continue;

            ringComponent.NextFlight = TimeSpan.Zero;
            if (_containerSystem.IsEntityInContainer(uid))
                continue;

            if (ringComponent.Returning)
            {
                ringComponent.Returning = false;
                ringComponent.Thrower = null;
                if (!TerminatingOrDeleted(throwerUid) && _transformSystem.InRange((uid, null), (throwerUid, null), 1.5f))
                    _handsSystem.PickupOrDrop(throwerUid, uid);

                continue;
            }

            if (ringComponent.BouncesLeft > 0 && NextRingVictim((uid, ringComponent), throwerUid) is { } nextUid)
            {
                ringComponent.BouncesLeft--;
                _throwingSystem.TryThrow(uid, Transform(nextUid).Coordinates, baseThrowSpeed: 12f, user: throwerUid, recoil: false);
                continue;
            }

            ringComponent.BouncesLeft = 0;
            ringComponent.HitThisThrow.Clear();
            if (TerminatingOrDeleted(throwerUid) || !_transformSystem.InRange((uid, null), (throwerUid, null), 10f))
            {
                ringComponent.Thrower = null;
                continue;
            }

            ringComponent.Returning = true;
            _throwingSystem.TryThrow(uid, Transform(throwerUid).Coordinates, baseThrowSpeed: 12f, recoil: false);
        }
    }

    private EntityUid? NextRingVictim(Entity<UniverseRingComponent> entity, EntityUid throwerUid)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, 4f, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner != throwerUid && !entity.Comp.HitThisThrow.Contains(mob.Owner) && _mobStateSystem.IsAlive(mob.Owner)
                && _examineSystem.InRangeUnOccluded(entity, mob.Owner, 4f))
                return mob.Owner;
        }

        return null;
    }

    #endregion

    #region Wind Fire Wheels

    /// <summary>
    ///     Worn, they leave burning footprints behind, and sometimes carry their rider a step further than meant.
    /// </summary>
    private void UpdateWheels()
    {
        var query = EntityQueryEnumerator<WindFireWheelsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wheelsComponent, out var transformComponent))
        {
            var wearerUid = transformComponent.ParentUid;
            if (!_inventorySystem.TryGetContainingSlot((uid, transformComponent, null), out var slot) || slot.Name != "shoes"
                || !HasComp<MobStateComponent>(wearerUid))
            {
                wheelsComponent.LastPosition = null;
                continue;
            }

            var here = _transformSystem.GetMapCoordinates(wearerUid);
            if (wheelsComponent.LastPosition is not { } last || last.MapId != here.MapId)
            {
                wheelsComponent.LastPosition = here;
                continue;
            }

            var moved = here.Position - last.Position;
            if (moved.LengthSquared() < 1f)
                continue;

            wheelsComponent.LastPosition = here;
            _chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, _transformSystem.ToCoordinates(last), duration: TimeSpan.FromSeconds(3));
            _visualsSystem.Art(MoltenStepEffect, last);
            if (_robustRandom.Prob(wheelsComponent.SkidChance))
                _throwingSystem.TryThrow(wearerUid, Vector2.Normalize(moved) * 1.5f, baseThrowSpeed: 8f, recoil: false, doSpin: false);
        }
    }

    #endregion
}
