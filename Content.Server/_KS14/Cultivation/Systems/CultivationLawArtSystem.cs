using System.Numerics;
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
    private static readonly SoundSpecifier SwordSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/Magic/staff_chaos.ogg");
    private static readonly SoundSpecifier WaterSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier FireSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    private static readonly SoundSpecifier BellSound = new SoundPathSpecifier("/Audio/Effects/church_bell3.ogg");
    private static readonly SoundSpecifier PalmSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly SoundSpecifier GrowthSound = new SoundPathSpecifier("/Audio/Magic/staff_healing.ogg");
    private static readonly EntProtoId BuddhaPalmShadowPrototype = "KsBuddhaPalmShadow";
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
                _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Slash", 8f), origin: uid);
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
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _throwingSystem.TryThrow(artifactUid, Transform(args.Target).Coordinates, baseThrowSpeed: 20f, user: entity);
        _damageableSystem.TryChangeDamage(args.Target, CultivationSystem.Damage("Slash", args.BonusDamage), origin: entity);
        _audioSystem.PlayPvs(SwordSound, entity);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-artifact-strike", ("user", entity.Owner), ("item", artifactUid), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnSwordRiding(Entity<CultivatorComponent> entity, ref SwordRidingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
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
        _audioSystem.PlayPvs(SwordSound, entity);
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
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        _stunSystem.TryUnstun(args.Target);
        _stunSystem.TryStanding(args.Target);
        RemComp<CultivationSnaredComponent>(args.Target);
        _audioSystem.PlayPvs(WaterSound, args.Target);
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
        _audioSystem.PlayPvs(WaterSound, entity);
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
        _visualsSystem.Flames(args.Target, 0.3f, 2);
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
        _visualsSystem.Flames(entity, args.Range, 14);
        _visualsSystem.Wave(entity, FxColour.Fire, big: true);
        _visualsSystem.Shockwave(_transformSystem.GetMapCoordinates(entity), strength: 1.5f);
        foreach (var victimUid in Hostiles(entity, args.Range))
            _flammableSystem.AdjustFireStacks(victimUid, 2f, ignite: true);

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
        _visualsSystem.Flames(entity, 0.4f, 4);
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
        _audioSystem.PlayPvs(BellSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-dharma-idol", ("user", entity.Owner)), type: PopupType.Large);
    }

    [SubscribeLocalEvent]
    private void OnBuddhaPalm(Entity<CultivatorComponent> entity, ref BuddhaPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;

        // The palm's shadow falls first; a moment later the palm.
        var shadowUid = Spawn(BuddhaPalmShadowPrototype, args.Target);
        var palmComponent = EnsureComp<BuddhaPalmShadowComponent>(shadowUid);
        palmComponent.Caster = entity;
        palmComponent.Damage = args.Damage;
        palmComponent.SlamAt = _gameTiming.CurTime + TimeSpan.FromSeconds(1);
        _noticeSystem.Announce(shadowUid, Loc.GetString("cultivation-buddha-palm-shadow"), type: PopupType.LargeCaution);
    }

    private void SlamBuddhaPalm(Entity<BuddhaPalmShadowComponent> entity)
    {
        var casterUid = entity.Comp.Caster;
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, 1.5f, _mobs);
        foreach (var mobEntity in _mobs)
        {
            if (casterUid is { } caster && (mobEntity.Owner == caster || _sharedCultivationSystem.IsAlly(caster, mobEntity.Owner)))
                continue;

            _damageableSystem.TryChangeDamage(mobEntity.Owner, CultivationSystem.Damage("Blunt", entity.Comp.Damage), origin: casterUid);
            _stunSystem.TryKnockdown(mobEntity.Owner, TimeSpan.FromSeconds(3));
        }

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
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-verdant-growth", ("user", entity.Owner), ("count", _plants.Count)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnBindingVines(Entity<CultivatorComponent> entity, ref BindingVinesActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
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
            var thornDirection = new Angle(MathHelper.DegreesToRadians(offset)).RotateVec(direction);
            _gunSystem.ShootProjectile(thornUid, thornDirection, _physicsSystem.GetMapLinearVelocity(entity), entity, user: entity, speed: 20f);
        }

        _audioSystem.PlayPvs(SwordSound, entity);
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
        _chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, _transformSystem.ToCoordinates(behind), duration: TimeSpan.FromSeconds(4));
    }

    [SubscribeLocalEvent]
    private void OnMudPrison(Entity<CultivatorComponent> entity, ref MudPrisonActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
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
