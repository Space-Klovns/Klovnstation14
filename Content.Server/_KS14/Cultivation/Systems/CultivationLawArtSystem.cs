using System.Numerics;
using Content.Server._KS14.Cultivation.Body;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Botany.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The rest of each law's techniques (Returning Iron's bound artifact, Still Lake's calm, Scorching Sun's
///         furnace, Immovable Mountain's golden bell and Buddha palm, Evergreen Spring's vines), and the techniques
///         two laws open together.
/// </summary>
public sealed partial class CultivationLawArtSystem : EntitySystem
{
    private static readonly SoundSpecifier WaterSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier FireSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    private static readonly SoundSpecifier MetalHitSound = new SoundCollectionSpecifier("MetalSlam");
    private static readonly SoundSpecifier SwordDrawSound = new SoundPathSpecifier("/Audio/Items/unsheath.ogg");
    private static readonly SoundSpecifier SloshSound = new SoundCollectionSpecifier("KsCultivationSlosh");
    private static readonly SoundSpecifier RustleSound = new SoundCollectionSpecifier("KsCultivationRustle");
    private static readonly SoundSpecifier BellSound = new SoundPathSpecifier("/Audio/Effects/church_bell3.ogg");
    private static readonly SoundSpecifier PalmSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly SoundSpecifier IdolPalmSound = new SoundCollectionSpecifier("Explosion");
    private static readonly SoundSpecifier GrowthSound = new SoundPathSpecifier("/Audio/Magic/staff_healing.ogg");
    private static readonly EntProtoId BuddhaPalmShadowPrototype = "KsBuddhaPalmShadow";
    private static readonly EntProtoId VinesEffect = "KsFxVines";
    private static readonly EntProtoId MoltenStepEffect = "KsFxMoltenStep";
    private static readonly EntProtoId[] PalmEffects = { "KsFxBuddhaPalm", "KsFxBuddhaPalmBig", "KsFxBuddhaPalmHuge" };
    private static readonly EntProtoId[] PalmShadowEffects = { "KsFxPalmShadow", "KsFxPalmShadowBig", "KsFxPalmShadowHuge" };

    /// <summary>
    ///     Half the width of each palm as drawn, in tiles: anyone under it is crushed.
    /// </summary>
    private static readonly float[] PalmRadius = { 1.6f, 2.4f, 3.2f };

    /// <summary>
    ///     How long the palm takes to fall: the baked animation fades in for 0.4 seconds and drops for 0.8.
    /// </summary>
    private static readonly TimeSpan PalmFallTime = TimeSpan.FromSeconds(1.2);
    private static readonly EntProtoId SteamPrototype = "KsSteamVeil";
    private static readonly EntProtoId ChemicalFirePrototype = "ChemicalFire";

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationStanceSystem _stanceSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private BodyArtSystem _bodyArtSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedProjectileSystem _projectileSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly HashSet<Entity<PlantHolderComponent>> _plants = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;

        var ridingEnumerator = EntityQueryEnumerator<SwordRidingComponent>();
        while (ridingEnumerator.MoveNext(out var uid, out var ridingComponent))
        {
            if (currentTime < ridingComponent.EndTime)
                continue;

            if (ridingComponent.AddedProtection)
                RemComp<ProtectedFromStepTriggersComponent>(uid);
            RemCompDeferred(uid, ridingComponent);
        }

        var formationEnumerator = EntityQueryEnumerator<SwordFormationComponent>();
        while (formationEnumerator.MoveNext(out var uid, out var formationComponent))
        {
            if (currentTime >= formationComponent.EndTime)
            {
                RemCompDeferred(uid, formationComponent);
                continue;
            }

            if (currentTime < formationComponent.NextSlash)
                continue;

            formationComponent.NextSlash = currentTime + TimeSpan.FromSeconds(1);
            foreach (var victimUid in Hostiles(uid, 2f))
                _damageableSystem.TryChangeDamage(victimUid, _cultivationSystem.Empowered(uid, CultivationSystem.Damage("Slash", 8f)), origin: uid);
        }

        var mirrorEnumerator = EntityQueryEnumerator<MirrorLakeComponent>();
        while (mirrorEnumerator.MoveNext(out var uid, out var mirrorComponent))
        {
            if (currentTime >= mirrorComponent.EndTime)
                RemCompDeferred(uid, mirrorComponent);
        }

        var palmEnumerator = EntityQueryEnumerator<BuddhaPalmShadowComponent>();
        while (palmEnumerator.MoveNext(out var uid, out var palmComponent))
        {
            if (currentTime < palmComponent.SlamAt)
                continue;

            SlamBuddhaPalm((uid, palmComponent));
            QueueDel(uid);
        }

        var moltenEnumerator = EntityQueryEnumerator<MoltenStepComponent>();
        while (moltenEnumerator.MoveNext(out var uid, out var moltenComponent))
        {
            if (currentTime >= moltenComponent.EndTime)
            {
                RemCompDeferred(uid, moltenComponent);
                continue;
            }

            LeaveMagma((uid, moltenComponent));
        }
    }

    #region Helpers

    /// <summary>
    ///     Living mobs in range that aren't the user or their friends.
    /// </summary>
    private List<EntityUid> Hostiles(EntityUid userUid, float range)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(userUid).Coordinates, range, _mobs);
        var found = new List<EntityUid>();
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner != userUid && !_sharedCultivationSystem.IsAlly(userUid, mobEntity.Owner))
                found.Add(mobEntity.Owner);
        }

        return found;
    }

    #endregion

    #region Metal: the bound artifact

    [SubscribeLocalEvent]
    private void OnBindArtifact(Entity<CultivatorComponent> entity, ref BindArtifactActionEvent args)
    {
        if (args.Handled)
            return;

        var bearerComponent = EnsureComp<ArtifactBearerComponent>(entity);

        // An item in hand: bind it. Empty hands: call the bound artifact back.
        if (_handsSystem.TryGetActiveItem(entity.Owner, out var heldUid))
        {
            if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
                return;

            args.Handled = true;
            _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, entity);
            bearerComponent.Artifact = heldUid;
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-artifact-bound", ("item", heldUid.Value)), type: PopupType.Medium);
            return;
        }

        if (bearerComponent.Artifact is not { } artifactUid || TerminatingOrDeleted(artifactUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-artifact-none"));
            return;
        }

        var recallRange = args.BaseRecallRange + 3f * (float)entity.Comp.Realm;
        if (!_transformSystem.InRange((artifactUid, null), (entity.Owner, null), recallRange))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-artifact-too-far"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _transformSystem.SetCoordinates(artifactUid, Transform(entity).Coordinates);
        _handsSystem.PickupOrDrop(entity, artifactUid, checkActionBlocker: false);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-artifact-recalled", ("item", artifactUid)));
    }

    [SubscribeLocalEvent]
    private void OnArtifactStrike(Entity<CultivatorComponent> entity, ref ArtifactStrikeActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ArtifactBearerComponent>(entity, out var bearerComponent) || bearerComponent.Artifact is not { } artifactUid
            || TerminatingOrDeleted(artifactUid) || !_handsSystem.IsHolding(entity.Owner, artifactUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-artifact-must-hold"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.MetalSparks(args.Target, 4);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _throwingSystem.TryThrow(artifactUid, Transform(args.Target).Coordinates, baseThrowSpeed: 20f, user: entity);
        _damageableSystem.TryChangeDamage(args.Target, _cultivationSystem.Empowered(entity, CultivationSystem.Damage("Slash", args.BonusDamage)), origin: entity);
        _audioSystem.PlayPvs(MetalHitSound, args.Target);
        _audioSystem.PlayPvs(SwordDrawSound, entity);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-artifact-strike", ("user", entity.Owner), ("item", artifactUid), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnSwordRiding(Entity<CultivatorComponent> entity, ref SwordRidingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Play(CultivationVisualsSystem.QinggongSound, entity);
        var ridingComponent = EnsureComp<SwordRidingComponent>(entity);
        ridingComponent.EndTime = _gameTiming.CurTime + args.Duration;

        // Riding above the ground: chasms and lava pass beneath.
        if (!HasComp<ProtectedFromStepTriggersComponent>(entity))
        {
            AddComp<ProtectedFromStepTriggersComponent>(entity);
            ridingComponent.AddedProtection = true;
        }

        _stanceSystem.ApplyHaste(entity, args.Duration, 1.5f);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-sword-riding", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnSwordFormation(Entity<CultivatorComponent> entity, ref SwordFormationActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var formationComponent = EnsureComp<SwordFormationComponent>(entity);
        formationComponent.EndTime = _gameTiming.CurTime + args.Duration;
        _audioSystem.PlayPvs(SwordDrawSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-sword-formation", ("user", entity.Owner)), type: PopupType.Medium);
    }

    /// <summary>
    ///     The circling swords bat aside some of what's fired in.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSwordFormationProjectile(Entity<SwordFormationComponent> entity, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !_robustRandom.Prob(0.3f))
            return;

        args.Cancelled = true;
        QueueDel(args.ProjUid);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-sword-formation-parry"), type: PopupType.Small);
    }

    #endregion

    #region Water: Still Lake

    [SubscribeLocalEvent]
    private void OnCalmHeart(Entity<CultivatorComponent> entity, ref CalmHeartActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.WaterSplash(args.Target, 3);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _stunSystem.TryUnstun(args.Target);
        _stunSystem.TryStanding(args.Target);
        RemComp<CultivationSnaredComponent>(args.Target);
        _audioSystem.PlayPvs(SloshSound, args.Target);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-calm-heart", ("user", entity.Owner), ("target", args.Target)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnTurtleBreathing(Entity<CultivatorComponent> entity, ref TurtleBreathingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _stanceSystem.ApplyTurtleBreathing(entity, args.Duration);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-turtle-breathing", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnMirrorLake(Entity<CultivatorComponent> entity, ref MirrorLakeActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        EnsureComp<MirrorLakeComponent>(entity).EndTime = _gameTiming.CurTime + args.Duration;
        _visualsSystem.WaterSplash(entity, 3);
        _audioSystem.PlayPvs(SloshSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-mirror-lake", ("user", entity.Owner)), type: PopupType.Medium);
    }

    /// <summary>
    ///     A still lake gives back what's thrown into it, half again as hard.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMirrorLakeProjectile(Entity<MirrorLakeComponent> entity, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || entity.Comp.LifeStage > ComponentLifeStage.Running
            || !TryComp<PhysicsComponent>(args.ProjUid, out var physicsComponent))
            return;

        _physicsSystem.SetLinearVelocity(args.ProjUid, -physicsComponent.LinearVelocity, body: physicsComponent);
        _transformSystem.SetLocalRotation(args.ProjUid, Transform(args.ProjUid).LocalRotation + Angle.FromDegrees(180));
        _projectileSystem.SetShooter(args.ProjUid, args.Component, entity);
        args.Component.Damage *= 1.5f;
        args.Cancelled = true;
    }

    #endregion

    #region Fire: Scorching Sun

    [SubscribeLocalEvent]
    private void OnKindle(Entity<CultivatorComponent> entity, ref KindleActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.FireBurst(args.Target, 2);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _flammableSystem.AdjustFireStacks(args.Target, 1f, ignite: true);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-kindle", ("user", entity.Owner), ("target", args.Target)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnFurnaceBurst(Entity<CultivatorComponent> entity, ref FurnaceBurstActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.FireRing(entity, args.Range, 12, 12);
        _visualsSystem.Wave(entity, FxColour.Fire, big: true);
        _visualsSystem.Shockwave(_transformSystem.GetMapCoordinates(entity), strength: 1.5f);
        foreach (var victimUid in Hostiles(entity, args.Range))
            _flammableSystem.AdjustFireStacks(victimUid, 2f * _cultivationSystem.Power(entity), ignite: true);

        // It's hot for you too.
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Heat", 5f), ignoreResistances: true, interruptsDoAfters: false);

        // Fire Root: the blast flings you back out of the flames.
        if (_sharedCultivationSystem.HasRoot(entity, CultivationElement.Fire))
        {
            var facing = Transform(entity).LocalRotation.ToWorldVec();
            _throwingSystem.TryThrow(entity, -facing * 3f, baseThrowSpeed: 10f, recoil: false, doSpin: false);
        }

        _audioSystem.PlayPvs(FireSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-furnace-burst", ("user", entity.Owner)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnBurningBlood(Entity<CultivatorComponent> entity, ref BurningBloodActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _visualsSystem.Mist(entity, CultivationVisualsSystem.MistBloodEffect);
        _visualsSystem.FireBurst(entity, 3);
        _visualsSystem.Motes(entity, FxColour.Fire, args.Duration);
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", args.SelfDamage), ignoreResistances: true, interruptsDoAfters: false);
        _sharedCultivationSystem.AddQi(entity.AsNullable(), args.QiGain);
        _stanceSystem.ApplyHaste(entity, args.Duration, 1.3f);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-burning-blood", ("user", entity.Owner)), type: PopupType.MediumCaution);
    }

    #endregion

    #region Earth: Immovable Mountain

    [SubscribeLocalEvent]
    private void OnGoldenBellQi(Entity<CultivatorComponent> entity, ref GoldenBellQiActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _stanceSystem.ApplyGoldenBellQi(entity, TimeSpan.FromSeconds(args.BaseSeconds + (float)entity.Comp.Realm));
        _audioSystem.PlayPvs(BellSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-golden-bell-qi", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnDharmaIdol(Entity<CultivatorComponent> entity, ref DharmaIdolActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _stanceSystem.ApplyDharmaIdol(entity, args.Duration, 0.6f);
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-dharma-idol", ("user", entity.Owner)), type: PopupType.Large);
    }

    /// <summary>
    ///     As in SS13: under a Dharma Idol, bare-handed blows become the idol's giant golden palms. People are slammed
    ///         away and floored; walls, doors and machines are smashed to pieces (reinforced walls too, at Nascent Soul).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnIdolMeleeHit(Entity<DharmaIdolComponent> entity, ref MeleeHitEvent args)
    {
        if (args.Weapon != entity.Owner || !args.IsHit || args.HitEntities.Count == 0)
            return;

        var power = _cultivationSystem.Power(entity);
        var origin = _transformSystem.GetMapCoordinates(entity).Position;
        foreach (var hitUid in args.HitEntities)
        {
            if (hitUid == entity.Owner)
                continue;

            var hitCoordinates = _transformSystem.GetMapCoordinates(hitUid);
            _visualsSystem.Art(CultivationVisualsSystem.ArtPalmQi, hitUid);
            _visualsSystem.WaveAt(hitCoordinates, FxColour.Gold);
            _visualsSystem.Shockwave(hitCoordinates, strength: 1f);
            _visualsSystem.Play(IdolPalmSound, hitUid, volume: -2f);

            if (HasComp<MobStateComponent>(hitUid))
            {
                _damageableSystem.TryChangeDamage(hitUid, CultivationSystem.Damage("Blunt", 25f * power), origin: entity);
                _staminaSystem.TakeStaminaDamage(hitUid, 25f * power, source: entity);
                _stunSystem.TryKnockdown(hitUid, TimeSpan.FromSeconds(1));
                var away = hitCoordinates.Position - origin;
                away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : new Vector2(0f, 1f);
                _throwingSystem.TryThrow(hitUid, away * 4f, baseThrowSpeed: 14f, user: entity, recoil: false);
                _noticeSystem.Announce(hitUid, Loc.GetString("cultivation-idol-palm", ("target", hitUid)), type: PopupType.MediumCaution);
                continue;
            }

            var wallTier = power >= CultivationSystem.PowerFor(CultivationRealm.NascentSoul) ? 2 : 1;
            _bodyArtSystem.Smash(hitCoordinates, 0.4f, 80f * power, wallTier, entity, wallChip: 80f * power);
        }
    }

    [SubscribeLocalEvent]
    private void OnBuddhaPalm(Entity<CultivatorComponent> entity, ref BuddhaPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;

        // Under a Dharma Idol it is the idol's own hand that comes down: twice the size and twice the reach.
        var size = HasComp<DharmaIdolComponent>(entity) ? 2 : 1;
        var target = _transformSystem.ToMapCoordinates(args.Target);

        // As in SS13: the shadow spreads on the floor while the palm falls out of the sky onto it.
        Spawn(PalmShadowEffects[size], target);
        Spawn(PalmEffects[size], target);
        _audioSystem.PlayPvs(PalmSound, entity);

        var markerUid = Spawn(BuddhaPalmShadowPrototype, target);
        var palmComponent = EnsureComp<BuddhaPalmShadowComponent>(markerUid);
        palmComponent.Caster = entity;
        palmComponent.Damage = args.Damage;
        palmComponent.Size = size;
        palmComponent.SlamAt = _gameTiming.CurTime + PalmFallTime;
        _noticeSystem.Announce(markerUid, Loc.GetString("cultivation-buddha-palm-shadow"), type: PopupType.LargeCaution);
    }

    /// <summary>
    ///     The palm lands: a great bell, the inner tiles crushed flat, a shockwave hurling back everyone a ring further
    ///         out, and whatever stood there smashed into the floor.
    /// </summary>
    private void SlamBuddhaPalm(Entity<BuddhaPalmShadowComponent> entity)
    {
        var casterUid = entity.Comp.Caster;
        var centre = _transformSystem.GetMapCoordinates(entity);
        var size = Math.Clamp(entity.Comp.Size, 0, PalmRadius.Length - 1);
        var inner = PalmRadius[size];
        var outer = inner + 2f;
        var damage = _cultivationSystem.Power(casterUid) * entity.Comp.Damage * (size >= 2 ? 1.5f : 1f);

        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, outer, _mobs);
        foreach (var mobEntity in _mobs)
        {
            if (casterUid is { } caster && (mobEntity.Owner == caster || _sharedCultivationSystem.IsAlly(caster, mobEntity.Owner)))
                continue;

            var offset = _transformSystem.GetMapCoordinates(mobEntity.Owner).Position - centre.Position;
            if (offset.Length() <= inner)
            {
                // Under the palm itself: crushed into the floor.
                _damageableSystem.TryChangeDamage(mobEntity.Owner, CultivationSystem.Damage("Blunt", damage), origin: casterUid);
                _staminaSystem.TakeStaminaDamage(mobEntity.Owner, 60f, source: casterUid);
                _stunSystem.TryKnockdown(mobEntity.Owner, TimeSpan.FromSeconds(4));
                _visualsSystem.Rubble(_transformSystem.GetMapCoordinates(mobEntity.Owner));
                continue;
            }

            _damageableSystem.TryChangeDamage(mobEntity.Owner, CultivationSystem.Damage("Blunt", damage * 0.35f), origin: casterUid);
            _stunSystem.TryKnockdown(mobEntity.Owner, TimeSpan.FromSeconds(1.5));
            var away = offset.LengthSquared() > 0.0001f ? Vector2.Normalize(offset) : new Vector2(0f, 1f);
            _throwingSystem.TryThrow(mobEntity.Owner, away * 3f, baseThrowSpeed: 10f, user: casterUid, recoil: false);
        }

        if (casterUid is { } smasherUid)
            _bodyArtSystem.Smash(centre, inner, 150f * size, wallTier: size, smasherUid);

        _bodyArtSystem.Impact(centre, outer, strength: 1.5f + entity.Comp.Size, crater: entity.Comp.Size);
        _visualsSystem.WaveAt(centre, FxColour.Gold, big: true);
        // The bell of a whole temple, heard far across the station.
        _visualsSystem.Boom(CultivationVisualsSystem.GreatBellSound, centre, volume: 10f, range: 40f);
        _visualsSystem.Boom(CultivationVisualsSystem.MountainFallSound, centre, volume: 4f, range: 20f);
        _audioSystem.PlayPvs(PalmSound, Transform(entity).Coordinates);
    }

    #endregion

    #region Wood: Evergreen Spring

    [SubscribeLocalEvent]
    private void OnVerdantGrowth(Entity<CultivatorComponent> entity, ref VerdantGrowthActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.WoodBloom(entity, 1.2f, 8);
        _plants.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, args.Range, _plants);
        foreach (var plantEntity in _plants)
        {
            // Weeds and pests wither; the plants drink the qi in.
            plantEntity.Comp.WeedLevel = 0f;
            plantEntity.Comp.PestLevel = 0f;
            plantEntity.Comp.Health += 20f;
            plantEntity.Comp.Age += 2;
            plantEntity.Comp.UpdateSpriteAfterUpdate = true;
        }

        _audioSystem.PlayPvs(GrowthSound, entity);
        _audioSystem.PlayPvs(RustleSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-verdant-growth", ("user", entity.Owner), ("count", _plants.Count)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnBindingVines(Entity<CultivatorComponent> entity, ref BindingVinesActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        SpawnAttachedTo(VinesEffect, new EntityCoordinates(args.Target, Vector2.Zero));
        _visualsSystem.WoodBloom(args.Target, 0f, 4);
        _audioSystem.PlayPvs(RustleSound, args.Target);
        _stanceSystem.ApplySnare(args.Target, args.Duration, 0f);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-binding-vines", ("user", entity.Owner), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    #endregion

    #region Combinations

    [SubscribeLocalEvent]
    private void OnSteamVeil(Entity<CultivatorComponent> entity, ref SteamVeilActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Mist(entity, CultivationVisualsSystem.MistWaterEffect);
        var centre = Transform(entity).Coordinates;
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                Spawn(args.Smoke, centre.Offset(new Vector2((float)x, (float)y)));
        }

        _audioSystem.PlayPvs(WaterSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-steam-veil", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnThousandThorns(Entity<CultivatorComponent> entity, ref ThousandThornsActionEvent args)
    {
        if (args.Handled || args.Count <= 0 || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var fromMap = _transformSystem.GetMapCoordinates(entity);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - fromMap.Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        for (var i = 0; i < args.Count; i++)
        {
            var offset = args.Count == 1 ? 0f : -args.Spread / 2f + args.Spread * (float)i / (float)(args.Count - 1);
            var thornUid = Spawn(args.Projectile, fromMap);
            _cultivationSystem.EmpowerProjectile(thornUid, entity);
            var thornDirection = new Angle(MathHelper.DegreesToRadians(offset)).RotateVec(direction);
            _gunSystem.ShootProjectile(thornUid, thornDirection, _physicsSystem.GetMapLinearVelocity(entity), entity, user: entity, speed: 20f);
        }

        _audioSystem.PlayPvs(RustleSound, entity);
        _audioSystem.PlayPvs(SwordDrawSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnMoltenStep(Entity<CultivatorComponent> entity, ref MoltenStepActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Flames(entity, 0.5f, 4);
        var moltenComponent = EnsureComp<MoltenStepComponent>(entity);
        moltenComponent.EndTime = _gameTiming.CurTime + args.Duration;
        moltenComponent.LastPosition = _transformSystem.GetMapCoordinates(entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-molten-step", ("user", entity.Owner)), type: PopupType.Medium);
    }

    /// <summary>
    ///     Every tile left behind keeps a glowing footprint for a while.
    /// </summary>
    private void LeaveMagma(Entity<MoltenStepComponent> entity)
    {
        var here = _transformSystem.GetMapCoordinates(entity);
        if (here.MapId != entity.Comp.LastPosition.MapId || (here.Position - entity.Comp.LastPosition.Position).LengthSquared() < 1f)
            return;

        var behind = entity.Comp.LastPosition;
        entity.Comp.LastPosition = here;
        // Real fire where they stepped, and a scorch of embers glowing as it dies down.
        _chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, _transformSystem.ToCoordinates(behind), duration: TimeSpan.FromSeconds(4));
        Spawn(MoltenStepEffect, behind);
    }

    [SubscribeLocalEvent]
    private void OnMudPrison(Entity<CultivatorComponent> entity, ref MudPrisonActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistDust, args.Target);
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                Spawn(args.Mud, args.Target.Offset(new Vector2((float)x, (float)y)));
        }

        // Whoever stands right at the centre is stuck for a moment.
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(args.Target, 0.6f, _mobs);
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner != entity.Owner)
                _stanceSystem.ApplySnare(mobEntity.Owner, TimeSpan.FromSeconds(2), 0f);
        }

        _audioSystem.PlayPvs(WaterSound, args.Target);
    }

    #endregion
}

/// <summary>
///     The one item a Returning Iron cultivator has bound to their soul.
/// </summary>
[RegisterComponent, Access(typeof(CultivationLawArtSystem))]
public sealed partial class ArtifactBearerComponent : Component
{
    [DataField]
    public EntityUid? Artifact;
}

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationLawArtSystem))]
public sealed partial class SwordRidingComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    ///     Whether riding added the step-trigger protection, so it only takes away what it gave.
    /// </summary>
    [DataField]
    public bool AddedProtection;
}

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationLawArtSystem))]
public sealed partial class SwordFormationComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSlash;
}

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationLawArtSystem))]
public sealed partial class MirrorLakeComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;
}

/// <summary>
///     The shadow of Buddha's Palm, a moment before the palm itself lands.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationLawArtSystem))]
public sealed partial class BuddhaPalmShadowComponent : Component
{
    [DataField]
    public EntityUid? Caster;

    [DataField]
    public float Damage;

    /// <summary>
    ///     0, 1 or 2: how big a palm came down.
    /// </summary>
    [DataField]
    public int Size = 1;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan SlamAt;
}

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationLawArtSystem))]
public sealed partial class MoltenStepComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public MapCoordinates LastPosition;
}
