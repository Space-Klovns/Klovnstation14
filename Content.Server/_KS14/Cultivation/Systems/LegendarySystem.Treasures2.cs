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
                // Every lit star sharpens the blade a little more.
                if (!LegendaryHit(args.User, victimUid, 8f + StarBladeBonus * starComponent.Stars, TimeSpan.Zero, entity)
                    || starComponent.Stars >= SevenStarSwordComponent.MaxStars)
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

    private const float StarBladeBonus = 2f;
    private const float LesserStarDamage = 15f;
    private const float LesserStarRadius = 1f;
    private const float GreatStarDamage = 25f;
    private const float GreatStarRadius = 2f;
    private static readonly TimeSpan StarInterval = TimeSpan.FromSeconds(0.18);

    /// <summary>
    ///     The Northern Dipper around where the stars are called down, handle to bowl, in tiles. The seventh, at the end
    ///         of the bowl, is the great star.
    /// </summary>
    private static readonly Vector2[] Dipper =
    {
        new(-3f, 1.2f), new(-2f, 0.7f), new(-1f, 0.3f), new(0f, 0f), new(0.3f, -1.2f), new(1.7f, -1.3f), new(2f, 0f),
    };

    /// <summary>
    ///     Release the lit stars: from three up, each falls in turn along the Northern Dipper around the target. With
    ///         all seven lit, the last is a great star that knocks everyone around it down.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSevenStars(Entity<SevenStarSwordComponent> entity, ref SevenStarsActionEvent args)
    {
        if (args.Handled)
            return;

        if (entity.Comp.Stars < SevenStarSwordComponent.MinStarsToRelease || entity.Comp.Falling.Count > 0)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-seven-star-unlit", ("stars", entity.Comp.Stars),
                ("needed", SevenStarSwordComponent.MinStarsToRelease)));
            return;
        }

        args.Handled = true;
        var target = _transformSystem.ToMapCoordinates(args.Target);
        var stars = entity.Comp.Stars;
        var now = _gameTiming.CurTime;
        entity.Comp.Stars = 0;
        entity.Comp.Wielder = args.Performer;
        entity.Comp.Struck.Clear();
        entity.Comp.Falling.Clear();
        for (var i = 0; i < stars; i++)
        {
            var great = i == SevenStarSwordComponent.MaxStars - 1;
            entity.Comp.Falling.Add(new SevenStarStrike(now + StarInterval * (i + 1),
                new MapCoordinates(target.Position + Dipper[i], target.MapId), great));
        }

        _pointLightSystem.SetEnergy(entity, 0f);
        Shout(args.Performer, stars == SevenStarSwordComponent.MaxStars ? "legendary-seven-star-shout" : "legendary-seven-star-shout-partial");
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, args.Performer);
        _visualsSystem.Wave(args.Performer, FxColour.Gold);
    }

    private void UpdateSevenStars(TimeSpan now)
    {
        var query = EntityQueryEnumerator<SevenStarSwordComponent>();
        while (query.MoveNext(out var uid, out var sword))
        {
            while (sword.Falling.Count > 0 && sword.Falling[0].At <= now)
            {
                var strike = sword.Falling[0];
                sword.Falling.RemoveAt(0);
                if (sword.Wielder is { } wielder && !TerminatingOrDeleted(wielder))
                    StarFalls((uid, sword), wielder, strike);
            }
        }
    }

    private void StarFalls(Entity<SevenStarSwordComponent> sword, EntityUid wielder, SevenStarStrike strike)
    {
        _visualsSystem.Art(CultivationVisualsSystem.ArtFallingStar, strike.Point);
        _visualsSystem.WaveAt(strike.Point, FxColour.Gold, big: strike.Great);
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(strike.Point, strike.Great ? GreatStarRadius : LesserStarRadius, _mobs);
        foreach (var victim in _mobs)
        {
            if (strike.Great)
            {
                LegendaryHit(wielder, victim.Owner, GreatStarDamage, TimeSpan.FromSeconds(2), sword);
                continue;
            }

            // A lesser star cuts each foe once per release, however close together they fall.
            if (sword.Comp.Struck.Add(victim.Owner))
                LegendaryHit(wielder, victim.Owner, LesserStarDamage, TimeSpan.FromSeconds(0.5), sword);
        }

        if (strike.Great)
        {
            _visualsSystem.Crater(strike.Point, 1);
            _visualsSystem.Shockwave(strike.Point, strength: 1.5f);
            _visualsSystem.Boom(CultivationVisualsSystem.ThunderclapSound, strike.Point, volume: 4f);
        }
        else
        {
            _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, strike.Point, -6f);
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
