using System.Numerics;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Body;

/// <summary>
///     Drives the Body Molding Art: earning tempering from training, forging it into the body, the Tribulation of
///         Flesh, the powers each tempered part grants, forged fists, and the arts each stage opens.
/// </summary>
public sealed partial class BodyCultivationSystem : EntitySystem
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PassiveTemperingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ForgeCycle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TribulationCycle = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan EternalHeartDelay = TimeSpan.FromSeconds(5);
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    private static readonly SoundSpecifier CrackSound = new SoundPathSpecifier("/Audio/Effects/bone_rattle.ogg");
    private static readonly SoundSpecifier StageSound = new SoundPathSpecifier("/Audio/Effects/church_bell1.ogg");
    private static readonly SoundSpecifier ForgeSound = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");
    private static readonly SoundSpecifier HeartSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    /// <summary>
    ///     The arts each stage opens, as in the original: the forge and tribulation from the start.
    /// </summary>
    public static readonly (EntProtoId Action, int Stage)[] StageArts =
    {
        ("ActionBodyForge", 0),
        ("ActionBodyTribulation", 0),
        ("ActionBodyBreathOfRenewal", 1),
        ("ActionBodyIronShirt", 2),
        ("ActionBodyMountainLeap", 2),
        ("ActionBodyEarthStomp", 2),
        ("ActionBodyShatteringFist", 3),
        ("ActionBodyHundredFists", 3),
        ("ActionBodyBoneSetting", 3),
        ("ActionBodyStanceThunderclap", 3),
        ("ActionBodyStanceMountain", 3),
        ("ActionBodyDisciple", 3),
        ("ActionBodyRemold", 4),
        ("ActionBodyBullCharge", 4),
        ("ActionBodyBloodBoil", 5),
        ("ActionBodyFallingMountain", 5),
        ("ActionBodyMountainHurl", 5),
        ("ActionBodySkySplittingPalm", 6),
        ("ActionBodyVajra", 7),
        ("ActionBodyHeavenQuake", 8),
        ("ActionBodyPrimordialRoar", 9),
    };

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private HungerSystem _hungerSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<HungerComponent> _hungerQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        var bodyEnumerator = EntityQueryEnumerator<BodyCultivatorComponent>();
        while (bodyEnumerator.MoveNext(out var uid, out var bodyComponent))
        {
            if (bodyComponent.EternalHeartReviveAt is { } reviveAt && currentTime >= reviveAt)
                EternalHeartRevive((uid, bodyComponent));

            if (currentTime < bodyComponent.NextTick)
                continue;

            _bodySystem.SetNextTick(bodyComponent, currentTime + TickInterval);
            Tick((uid, bodyComponent), currentTime);
        }
    }

    private void Tick(Entity<BodyCultivatorComponent> entity, TimeSpan currentTime)
    {
        var alive = !_mobStateQuery.TryComp(entity, out var mobStateComponent) || _mobStateSystem.IsAlive(entity, component: mobStateComponent);
        if (!alive)
            return;

        // Exhaustion drains back: faster at higher stages, with a strong heart or deep lungs, and in Blood Boil.
        var recovery = 3f + 0.4f * (float)entity.Comp.Stage;
        if (SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Heart) >= 3)
            recovery += 1f;
        if (SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Lungs) >= 6)
            recovery += 1f;
        if (_bodySystem.InBloodBoil(entity))
            recovery *= 4f;
        _bodySystem.AddExhaustion(entity.AsNullable(), -recovery * (float)TickInterval.TotalSeconds);

        // A body on the path tempers itself a little just by living.
        if (currentTime >= entity.Comp.NextPassiveTempering)
        {
            _bodySystem.SetNextPassiveTempering(entity.Comp, currentTime + PassiveTemperingInterval);
            _bodySystem.AddTempering(entity.AsNullable(), 1f);
        }

        if (entity.Comp.RegenPerSecond > 0f)
            _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-entity.Comp.RegenPerSecond * (float)TickInterval.TotalSeconds));

        // A trained stomach burns food slower: give back the share it didn't need.
        if (entity.Comp.HungerMultiplier < 1f && _hungerQuery.TryComp(entity, out var hungerComponent))
        {
            var saved = hungerComponent.ActualDecayRate * (1f - entity.Comp.HungerMultiplier) * (float)TickInterval.TotalSeconds;
            _hungerSystem.ModifyHunger(entity, saved, component: hungerComponent);
        }
    }

    #region Lifecycle

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<BodyCultivatorComponent> entity, ref MapInitEvent args)
    {
        var currentTime = _gameTiming.CurTime;
        _bodySystem.SetNextTick(entity.Comp, currentTime + TickInterval);
        _bodySystem.SetNextPassiveTempering(entity.Comp, currentTime + PassiveTemperingInterval);

        float? critical = _mobThresholdSystem.TryGetThresholdForState(entity, MobState.Critical, out var criticalThreshold)
            ? criticalThreshold.Value.Float()
            : null;
        float? dead = _mobThresholdSystem.TryGetThresholdForState(entity, MobState.Dead, out var deadThreshold)
            ? deadThreshold.Value.Float()
            : null;
        _bodySystem.SetBaseThresholds(entity.Comp, critical, dead);

        _bodySystem.Recalculate(entity);
        GrantStageArts(entity);
        _bodySystem.UpdateAlerts(entity);
    }

    [SubscribeLocalEvent]
    private void OnStageChanged(Entity<BodyCultivatorComponent> entity, ref BodyStageChangedEvent args)
    {
        GrantStageArts(entity);
        ScaleCooldowns(entity);
    }

    [SubscribeLocalEvent]
    private void OnPowersChanged(Entity<BodyCultivatorComponent> entity, ref BodyPowersChangedEvent args)
    {
        ApplyHealthBonus(entity);

        foreach (var power in args.NewlyUnlocked)
        {
            EntityManager.AddComponents(entity, power.Components, removeExisting: false);
            _popupSystem.PopupEntity(Loc.GetString("body-power-unlocked",
                ("power", Loc.GetString(power.Name)), ("description", Loc.GetString(power.Description))), entity, entity, type: PopupType.Medium);
        }
    }

    /// <summary>
    ///     Raise the thresholds for critical condition and death by the body's health bonus.
    /// </summary>
    private void ApplyHealthBonus(Entity<BodyCultivatorComponent> entity)
    {
        if (entity.Comp.BaseCriticalThreshold is { } critical)
            _mobThresholdSystem.SetMobStateThreshold(entity, FixedPoint2.New(critical + entity.Comp.HealthBonus), MobState.Critical);
        if (entity.Comp.BaseDeadThreshold is { } dead)
            _mobThresholdSystem.SetMobStateThreshold(entity, FixedPoint2.New(dead + entity.Comp.HealthBonus), MobState.Dead);
    }

    public void GrantStageArts(Entity<BodyCultivatorComponent> entity)
    {
        foreach (var (action, stage) in StageArts)
        {
            if (stage > entity.Comp.Stage || _bodySystem.HasTechnique(entity.Comp, action))
                continue;

            EntityUid? actionUid = null;
            if (!_actionsSystem.AddAction(entity, ref actionUid, action))
                continue;

            _bodySystem.RecordTechnique(entity.Comp, action, actionUid.Value);
            if (TryComp<ActionComponent>(actionUid, out var actionComponent) && actionComponent.UseDelay is { } useDelay)
                _bodySystem.RecordBaseCooldown(entity.Comp, action, useDelay);
        }

        ScaleCooldowns(entity);
    }

    /// <summary>
    ///     Body arts come back 5% faster for every stage.
    /// </summary>
    private void ScaleCooldowns(Entity<BodyCultivatorComponent> entity)
    {
        var multiplier = MathF.Max(0.5f, 1f - 0.05f * (float)entity.Comp.Stage);
        foreach (var (action, actionUid) in entity.Comp.Techniques)
        {
            if (_bodySystem.GetBaseCooldown(entity.Comp, action) is { } baseCooldown)
                _actionsSystem.SetUseDelay(actionUid, baseCooldown * multiplier);
        }
    }

    #endregion

    #region Training

    [SubscribeLocalEvent]
    private void OnDamaged(Entity<BodyCultivatorComponent> entity, ref DamageChangedEvent args)
    {
        // Taking a beating is training too.
        if (args.DamageIncreased && args.DamageDelta is { } delta && delta.GetTotal() >= 5)
            _bodySystem.AddTempering(entity.AsNullable(), 5f, trainingSource: "beaten", cooldown: TimeSpan.FromSeconds(15));
    }

    /// <summary>
    ///     Forged fists: every punch carries the arms' tempering, and trains the body.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMeleeHit(Entity<BodyCultivatorComponent> entity, ref MeleeHitEvent args)
    {
        // Only bare fists: the weapon is the body itself.
        if (args.Weapon != entity.Owner || !args.IsHit || args.HitEntities.Count == 0)
            return;

        if (entity.Comp.PunchDamage > 0f)
            args.BonusDamage += CultivationSystem.Damage("Blunt", entity.Comp.PunchDamage);

        foreach (var hitUid in args.HitEntities)
        {
            if (hitUid == entity.Owner)
                continue;

            if (_mobStateQuery.HasComp(hitUid))
            {
                _bodySystem.AddTempering(entity.AsNullable(), 6f, trainingSource: "fight", cooldown: TimeSpan.FromSeconds(15));
                ForgedStrike(entity, hitUid);
                continue;
            }

            _bodySystem.AddTempering(entity.AsNullable(), 4f, trainingSource: "strike", cooldown: TimeSpan.FromSeconds(15));
            if (entity.Comp.PunchStructuralDamage <= 0f)
                continue;

            var isWall = _tagSystem.HasTag(hitUid, WallTag);
            if (!isWall || entity.Comp.PunchBreaksWalls)
                _damageableSystem.TryChangeDamage(hitUid, CultivationSystem.Damage("Structural", entity.Comp.PunchStructuralDamage), origin: entity);
        }
    }

    private void ForgedStrike(Entity<BodyCultivatorComponent> entity, EntityUid victimUid)
    {
        if (entity.Comp.PunchStaminaDamage > 0f)
            _staminaSystem.TakeStaminaDamage(victimUid, entity.Comp.PunchStaminaDamage, source: entity);

        if (entity.Comp.PunchThrowChance > 0f && _robustRandom.Prob(entity.Comp.PunchThrowChance))
        {
            var direction = _transformSystem.GetMapCoordinates(victimUid).Position - _transformSystem.GetMapCoordinates(entity).Position;
            if (direction.LengthSquared() > 0.0001f)
            {
                _throwingSystem.TryThrow(victimUid, Vector2.Normalize(direction) * 3f, baseThrowSpeed: 8f, user: entity, recoil: false);
                _popupSystem.PopupEntity(Loc.GetString("body-punch-hurl", ("user", entity.Owner), ("target", victimUid)), victimUid, type: PopupType.MediumCaution);
                return;
            }
        }

        if (entity.Comp.PunchKnockdownChance > 0f && _robustRandom.Prob(entity.Comp.PunchKnockdownChance))
        {
            _stunSystem.TryKnockdown(victimUid, TimeSpan.FromSeconds(1.5));
            _popupSystem.PopupEntity(Loc.GetString("body-punch-topple", ("user", entity.Owner), ("target", victimUid)), victimUid, type: PopupType.MediumCaution);
        }
    }

    #endregion

    #region Forge the Body

    [SubscribeLocalEvent]
    private void OnForgeAction(Entity<BodyCultivatorComponent> entity, ref BodyForgeActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_bodySystem.WeakestGroup(entity.Comp) is not { } group)
        {
            _popupSystem.PopupEntity(Loc.GetString(_bodySystem.ReadyForTribulation(entity.Comp)
                ? "body-forge-ready-for-tribulation"
                : "body-forge-all-capped"), entity, entity);
            return;
        }

        if (entity.Comp.Tempering < 1f)
        {
            _popupSystem.PopupEntity(Loc.GetString("body-forge-no-tempering"), entity, entity);
            return;
        }

        _bodySystem.SetForgeTarget(entity.Comp, group);
        StartForgeCycle(entity, group);
    }

    private void StartForgeCycle(Entity<BodyCultivatorComponent> entity, BodyGroup group)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, ForgeCycle, new BodyForgeDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-forge-start", ("user", entity.Owner),
                ("part", Loc.GetString($"body-group-{group}"))), entity, type: PopupType.Small);
        }
    }

    [SubscribeLocalEvent]
    private void OnForgeDoAfter(Entity<BodyCultivatorComponent> entity, ref BodyForgeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || entity.Comp.ForgeTarget is not { } group)
            return;

        args.Handled = true;

        // Each punishing cycle pours in up to five tempering, at the cost of a little stamina.
        var gained = _bodySystem.Forge(entity.AsNullable(), group, 5f);
        _staminaSystem.TakeStaminaDamage(entity, 8f);
        _audioSystem.PlayPvs(ForgeSound, entity);

        if (gained > 0)
        {
            _popupSystem.PopupEntity(Loc.GetString("body-forge-level", ("part", Loc.GetString($"body-group-{group}")),
                ("level", SharedBodyCultivationSystem.GetLevel(entity.Comp, group))), entity, entity, type: PopupType.Medium);
        }

        // Carry on into the next weakest part while there's tempering left to pour.
        if (entity.Comp.Tempering < 1f || _bodySystem.WeakestGroup(entity.Comp) is not { } next)
        {
            _bodySystem.SetForgeTarget(entity.Comp, null);
            if (_bodySystem.ReadyForTribulation(entity.Comp))
                _popupSystem.PopupEntity(Loc.GetString("body-forge-ready-for-tribulation"), entity, entity, type: PopupType.Medium);
            return;
        }

        _bodySystem.SetForgeTarget(entity.Comp, next);
        args.Repeat = true;
    }

    #endregion

    #region Tribulation of Flesh

    /// <summary>
    ///     How likely the Tribulation is to succeed: 70 or more always does.
    /// </summary>
    public int TribulationReadiness(Entity<BodyCultivatorComponent> entity)
    {
        var readiness = 50;
        if (_hungerQuery.TryComp(entity, out var hungerComponent)
            && !_hungerSystem.IsHungerBelowState(entity, HungerThreshold.Okay, comp: hungerComponent))
            readiness += 10;

        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            readiness += 10;

        if (_mobThresholdSystem.TryGetThresholdForState(entity, MobState.Critical, out var critical)
            && _damageableSystem.GetTotalDamage(entity.Owner) > critical.Value * 0.3)
            readiness -= 20;

        var ev = new BodyTribulationReadinessEvent(readiness);
        RaiseLocalEvent(entity, ref ev);
        return ev.Readiness;
    }

    [SubscribeLocalEvent]
    private void OnTribulationAction(Entity<BodyCultivatorComponent> entity, ref BodyTribulationActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!_bodySystem.ReadyForTribulation(entity.Comp))
        {
            _popupSystem.PopupEntity(Loc.GetString(entity.Comp.Stage >= SharedBodyCultivationSystem.StageCap(entity.Comp)
                ? (entity.Comp.Committed ? "body-tribulation-peak" : "body-tribulation-mortal-cap")
                : "body-tribulation-not-ready"), entity, entity);
            return;
        }

        StartTribulation(entity);
    }

    public bool StartTribulation(Entity<BodyCultivatorComponent> entity)
    {
        // Ten seconds plus two per stage, in cycles of bones breaking and resetting.
        var duration = 10 + 2 * entity.Comp.Stage;
        var cycles = Math.Max(1, (int)Math.Ceiling((double)duration / TribulationCycle.TotalSeconds));
        var readiness = TribulationReadiness(entity);

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, TribulationCycle, new BodyTribulationDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        _bodySystem.SetTribulation(entity.Comp, cycles, readiness);
        _popupSystem.PopupEntity(Loc.GetString("body-tribulation-start", ("user", entity.Owner), ("readiness", readiness)), entity, type: PopupType.LargeCaution);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnTribulationDoAfter(Entity<BodyCultivatorComponent> entity, ref BodyTribulationDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled || !_mobStateSystem.IsAlive(entity) || _mobStateSystem.IsCritical(entity))
        {
            FailTribulation(entity);
            return;
        }

        // Bones snap and knit back together.
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", (float)(3 + entity.Comp.Stage)), ignoreResistances: true, interruptsDoAfters: false);
        _staminaSystem.TakeStaminaDamage(entity, 6f);
        _audioSystem.PlayPvs(CrackSound, entity);

        var left = entity.Comp.TribulationCyclesLeft - 1;
        _bodySystem.SetTribulation(entity.Comp, left, entity.Comp.TribulationReadiness);
        if (left > 0)
        {
            args.Repeat = true;
            return;
        }

        var readiness = entity.Comp.TribulationReadiness;
        var chance = readiness >= 70 ? 1f : (float)Math.Clamp(readiness, 5, 95) / 100f;
        if (_robustRandom.Prob(chance))
            SucceedTribulation(entity);
        else
            FailTribulation(entity);
    }

    public void SucceedTribulation(Entity<BodyCultivatorComponent> entity)
    {
        _bodySystem.SetTribulation(entity.Comp, 0, 0);
        _bodySystem.AdvanceStage(entity.AsNullable());

        var stageName = Loc.GetString($"body-stage-{entity.Comp.Stage}");
        _audioSystem.PlayPvs(StageSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("body-tribulation-success", ("user", entity.Owner), ("stage", stageName)), entity, type: PopupType.Large);
        _popupSystem.PopupEntity(Loc.GetString($"body-stage-{entity.Comp.Stage}-benefits"), entity, entity, type: PopupType.Medium);
    }

    private void FailTribulation(Entity<BodyCultivatorComponent> entity)
    {
        _bodySystem.SetTribulation(entity.Comp, 0, 0);
        _bodySystem.AddExhaustion(entity.AsNullable(), 30f);
        _stunSystem.TryKnockdown(entity.Owner, TimeSpan.FromSeconds(3));
        _popupSystem.PopupEntity(Loc.GetString("body-tribulation-fail", ("user", entity.Owner)), entity, type: PopupType.LargeCaution);
    }

    #endregion

    #region Eternal Heart

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<BodyCultivatorComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !entity.Comp.EternalHeart)
            return;

        if (_gameTiming.CurTime < entity.Comp.EternalHeartReady)
        {
            _popupSystem.PopupEntity(Loc.GetString("body-eternal-heart-recovering"), entity, entity);
            return;
        }

        _bodySystem.SetEternalHeartReady(entity.Comp, _gameTiming.CurTime + entity.Comp.EternalHeartCooldown);
        _bodySystem.SetEternalHeartRevive(entity.Comp, _gameTiming.CurTime + EternalHeartDelay);
        _popupSystem.PopupEntity(Loc.GetString("body-eternal-heart-refuses"), entity, entity, type: PopupType.Large);
    }

    private void EternalHeartRevive(Entity<BodyCultivatorComponent> entity)
    {
        _bodySystem.SetEternalHeartRevive(entity.Comp, null);
        if (!_mobStateSystem.IsDead(entity))
            return;

        // Heal back to just short of critical, then start the heart.
        var total = _damageableSystem.GetTotalDamage(entity.Owner).Float();
        var targetDamage = (entity.Comp.BaseCriticalThreshold ?? 100f) + entity.Comp.HealthBonus - 10f;
        if (total > targetDamage)
            _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(targetDamage - total));

        _mobStateSystem.ChangeMobState(entity, MobState.Alive);
        _audioSystem.PlayPvs(HeartSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("body-eternal-heart-restarts", ("user", entity.Owner)), entity, type: PopupType.Large);
    }

    #endregion

    #region Manuals

    [SubscribeLocalEvent]
    private void OnBodyManualUsed(Entity<BodyManualComponent> manualEntity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var userUid = args.User;

        if (_cultivatorQuery.HasComp(userUid))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-manual-qi-cultivator"), userUid, userUid);
            return;
        }

        if (_bodyCultivatorQuery.TryComp(userUid, out var bodyComponent) && (bodyComponent.Committed || !manualEntity.Comp.Commits))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-manual-known"), userUid, userUid);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, userUid, manualEntity.Comp.ReadTime, new BodyManualDoAfterEvent(), manualEntity, used: manualEntity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _popupSystem.PopupEntity(Loc.GetString("cultivation-manual-start", ("user", userUid), ("manual", manualEntity.Owner)), userUid, type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnBodyManualRead(Entity<BodyManualComponent> manualEntity, ref BodyManualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || _cultivatorQuery.HasComp(args.User))
            return;

        args.Handled = true;
        var bodyComponent = EnsureComp<BodyCultivatorComponent>(args.User);
        if (manualEntity.Comp.Commits)
        {
            _bodySystem.Commit((args.User, bodyComponent));
            _popupSystem.PopupEntity(Loc.GetString("body-manual-committed"), args.User, args.User, type: PopupType.Large);
        }
        else
        {
            _popupSystem.PopupEntity(Loc.GetString("body-manual-primer"), args.User, args.User, type: PopupType.Medium);
        }
    }

    #endregion
}

/// <summary>
///     Raised on a body cultivator when their Tribulation of Flesh readiness is assessed, so pills, inheritances and
///         the like can add to it.
/// </summary>
[ByRefEvent]
public record struct BodyTribulationReadinessEvent(int Readiness);
