using System.Numerics;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
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
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
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
    private static readonly TimeSpan PassiveTemperingInterval = TimeSpan.FromMinutes(1);
    private const float PassiveTempering = 6f;
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
        ("ActionJianghuDuel", 0),
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

    [Dependency] private CultivationDurabilitySystem _durabilitySystem = default!;
    [Dependency] private Content.Shared._Mono.PhosphorNightVision.SharedPhosphorNightVisionSystem _nightVisionSystem = default!;
    [Dependency] private Content.Server._KS14.Cultivation.Systems.CultivationLoadoutSystem _loadoutSystem = default!;
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
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private CultivationSiteSystem _siteSystem = default!;
    [Dependency] private WuxiaWorldRuleSystem _wuxiaWorldSystem = default!;
    [Dependency] private WuxiaAscensionRuleSystem _ascensionRuleSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
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
            // Counted as training, so the same company and gamemode bonuses that speed up fighting speed this up too.
            _bodySystem.AddTempering(entity.AsNullable(), PassiveTempering * TrainingRate, "living", PassiveTemperingInterval);
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

        var (critical, dead) = _durabilitySystem.BaseThresholds(entity);
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
        _loadoutSystem.RefreshSlots(entity);
        if (args.NewStage >= BodyCultivatorComponent.MaxStage && args.OldStage < BodyCultivatorComponent.MaxStage)
        {
            var peak = new CultivationPeakReachedEvent(entity);
            RaiseLocalEvent(ref peak);
        }
    }

    [SubscribeLocalEvent]
    private void OnPowersChanged(Entity<BodyCultivatorComponent> entity, ref BodyPowersChangedEvent args)
    {
        ApplyHealthBonus(entity);

        foreach (var power in args.NewlyUnlocked)
        {
            EntityManager.AddComponents(entity, power.Components, removeExisting: power.ReplaceComponents);
            foreach (var name in power.RemoveComponents)
            {
                if (EntityManager.ComponentFactory.TryGetRegistration(name, out var registration))
                    RemComp(entity, registration.Type);
            }
            // Tempered eyes see in the dark from the moment they open.
            if (TryComp<Content.Shared._Mono.Overlays.PhosphorNightVisionComponent>(entity, out var nightVision) && !nightVision.Enabled)
                _nightVisionSystem.SetEnabled((entity, nightVision), true, entity);
            _noticeSystem.Notify(entity, Loc.GetString("body-power-unlocked",
                ("power", Loc.GetString(power.Name)), ("description", Loc.GetString(power.Description))), type: PopupType.Medium, alsoToChat: true);
        }
    }

    /// <summary>
    ///     Raise the thresholds for critical condition and death by the body's health bonus.
    /// </summary>
    private void ApplyHealthBonus(Entity<BodyCultivatorComponent> entity)
    {
        _durabilitySystem.Refresh(entity);
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

            // Forging and the Tribulation always stay on the bar; the arts go through the Techniques menu.
            if (action.Id is not ("ActionBodyForge" or "ActionBodyTribulation"))
                _loadoutSystem.Learned(entity, actionUid.Value);
        }

        EnsureCoreArts(entity);
        ScaleCooldowns(entity);
    }

    /// <summary>
    ///     Forge the Body and the Tribulation of Flesh are what the whole path runs on: make sure they exist, are pinned
    ///         to the bar and are listed in the Techniques menu.
    /// </summary>
    public void EnsureCoreArts(Entity<BodyCultivatorComponent> entity)
    {
        foreach (var core in new EntProtoId[] { "ActionBodyForge", "ActionBodyTribulation" })
        {
            var actionUid = _bodySystem.GetTechnique(entity.Comp, core) ?? EntityUid.Invalid;
            if (!actionUid.IsValid() || TerminatingOrDeleted(actionUid))
            {
                EntityUid? added = null;
                if (!_actionsSystem.AddAction(entity, ref added, core))
                    continue;

                actionUid = added.Value;
                _bodySystem.RecordTechnique(entity.Comp, core, actionUid);
            }

            _loadoutSystem.Pin(entity, actionUid);
        }
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

    // How a body cultivator tempers themselves. Each source pays a solid chunk and then rests for a few seconds, so
    // training is about doing things, not clicking fast. Roughly per minute of effort, before the Ascension bonus:
    // living 6, training on walls and rock ~48, a fist fight ~120 (half with a weapon), standing up to blows about as
    // much, body arts a few each. Beating someone down is the big lesson, once per victim every ten minutes.
    // Tuned so a casual body cultivator in Ascension reaches stage 9 about five minutes after a casual qi cultivator
    // reaches Nascent Soul (~25-30 minutes), without needing a fight every few minutes on a quiet server.
    /// <summary>
    ///     Every training gain below is scaled by this. Forging is quick in the wuxia gamemodes, so earning is a third
    ///         slower to keep the climb to stage 9 at about the same pace.
    /// </summary>
    public const float TrainingRate = 2f / 3f;

    private const float FistHitTempering = 8f;
    private const float WeaponHitTempering = 4f;
    private static readonly TimeSpan FightCooldown = TimeSpan.FromSeconds(4);
    private const float StrikeTempering = 4f;
    private static readonly TimeSpan StrikeCooldown = TimeSpan.FromSeconds(5);
    private const float BeatenPerDamage = 1f;
    private const float BeatenMax = 15f;
    private static readonly TimeSpan BeatenCooldown = TimeSpan.FromSeconds(4);
    private const float HardshipPerDamage = 0.4f;
    private const float HardshipMax = 6f;
    private static readonly TimeSpan HardshipCooldown = TimeSpan.FromSeconds(15);
    private const float DefeatPlayerTempering = 25f;
    private const float DefeatCultivatorBonus = 10f;
    private const float DefeatMobTempering = 5f;
    private static readonly TimeSpan DefeatCooldown = TimeSpan.FromMinutes(10);

    [SubscribeLocalEvent]
    private void OnDamaged(Entity<BodyCultivatorComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        var total = delta.GetTotal().Float();
        if (args.Origin is { } origin && origin != entity.Owner)
        {
            // Standing up to someone's blows: the harder they land, the more they teach.
            Train(entity, MathF.Min(total * BeatenPerDamage, BeatenMax), "beaten", BeatenCooldown);
        }
        else
        {
            // Fire, cold, falls and heaven's own lightning: hardship tempers too, if slowly.
            Train(entity, MathF.Min(total * HardshipPerDamage, HardshipMax), "hardship", HardshipCooldown);
        }
    }

    /// <summary>
    ///     Fighting with a weapon trains the body too, at half the rate of bare fists.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWeaponHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (args.Weapon == args.User || !args.IsHit || args.HitEntities.Count == 0
            || !TryComp<BodyCultivatorComponent>(args.User, out var bodyComponent))
            return;

        TrainSwing((args.User, bodyComponent), args.HitEntities, WeaponHitTempering);
    }

    private void TrainSwing(Entity<BodyCultivatorComponent> entity, IReadOnlyList<EntityUid> hitEntities, float fightAmount)
    {
        var fought = false;
        var struck = false;
        foreach (var hitUid in hitEntities)
        {
            if (hitUid == entity.Owner)
                continue;

            if (_mobStateQuery.HasComp(hitUid))
                fought |= _mobStateSystem.IsAlive(hitUid);
            else
                struck = true;
        }

        if (fought)
            Train(entity, fightAmount, "fight", FightCooldown);
        else if (struck)
            Train(entity, StrikeTempering, "strike", StrikeCooldown);
    }

    /// <summary>
    ///     Tempering from training: sped up by good company and the gamemode, then that source rests for a while.
    /// </summary>
    private float Train(Entity<BodyCultivatorComponent> entity, float amount, string source, TimeSpan cooldown)
    {
        return amount > 0f ? _bodySystem.AddTempering(entity.AsNullable(), amount * TrainingRate, trainingSource: source, cooldown: cooldown) : 0f;
    }

    /// <summary>
    ///     Beating someone down with your own body is the hardest lesson there is. Each victim only teaches it once
    ///         every ten minutes; a player teaches far more than an animal, and a fellow cultivator more still.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAnyMobStateChanged(MobStateChangedEvent args)
    {
        if (args.OldMobState != MobState.Alive || args.NewMobState == MobState.Alive
            || args.Origin is not { } victorUid || victorUid == args.Target
            || !TryComp<BodyCultivatorComponent>(victorUid, out var victorComponent))
            return;

        if (!_bodySystem.TryMarkDefeated(victorComponent, args.Target, _gameTiming.CurTime + DefeatCooldown))
            return;
        var amount = DefeatMobTempering;
        if (HasComp<ActorComponent>(args.Target))
        {
            amount = DefeatPlayerTempering;
            if (_cultivatorQuery.HasComp(args.Target) || TryComp<BodyCultivatorComponent>(args.Target, out var victimBody) && victimBody.Stage > 0)
                amount += DefeatCultivatorBonus;
        }

        var gained = Train((victorUid, victorComponent), amount, "defeat", TimeSpan.Zero);
        if (gained > 0f)
            _noticeSystem.Notify(victorUid, Loc.GetString("body-tempering-defeat", ("amount", (int)MathF.Round(gained)), ("victim", args.Target)), type: PopupType.Medium);
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

        // One swing teaches once, however many it catches. Only a fight teaches much: a body that's already down can't
        // push back, and punching rock, walls and machinery is honest labour but a slow teacher.
        TrainSwing(entity, args.HitEntities, FistHitTempering);

        foreach (var hitUid in args.HitEntities)
        {
            if (hitUid == entity.Owner)
                continue;

            if (_mobStateQuery.HasComp(hitUid))
            {
                ForgedStrike(entity, hitUid);
                continue;
            }

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
                _noticeSystem.Announce(victimUid, Loc.GetString("body-punch-hurl", ("user", entity.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
                return;
            }
        }

        if (entity.Comp.PunchKnockdownChance > 0f && _robustRandom.Prob(entity.Comp.PunchKnockdownChance))
        {
            _stunSystem.TryKnockdown(victimUid, TimeSpan.FromSeconds(1.5));
            _noticeSystem.Announce(victimUid, Loc.GetString("body-punch-topple", ("user", entity.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
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
        if (entity.Comp.Stage >= BodyCultivatorComponent.MaxStage)
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-forge-peak"), PopupType.Medium);
            return;
        }

        if (_bodySystem.WeakestGroup(entity.Comp) is not { } group)
        {
            _noticeSystem.Notify(entity, Loc.GetString(_bodySystem.ReadyForTribulation(entity.Comp)
                ? "body-forge-ready-for-tribulation"
                : "body-forge-all-capped"));
            return;
        }

        if (entity.Comp.Tempering < 1f)
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-forge-no-tempering"));
            return;
        }

        _bodySystem.SetForgeTarget(entity.Comp, group);
        StartForgeCycle(entity, group);
    }

    private void StartForgeCycle(Entity<BodyCultivatorComponent> entity, BodyGroup group)
    {
        // In the wuxia gamemodes, where cultivation is the whole round, forging goes twice as fast.
        var cycle = _wuxiaWorldSystem.WuxiaActive() ? ForgeCycle / 2 : ForgeCycle;
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, cycle, new BodyForgeDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
        {
            _noticeSystem.Announce(entity, Loc.GetString("body-forge-start", ("user", entity.Owner),
                ("part", Loc.GetString($"body-group-{group}"))), type: PopupType.Small);
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
        _visualsSystem.Motes(entity, FxColour.Earth, TimeSpan.FromSeconds(1.5));

        if (gained > 0)
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-forge-level", ("part", Loc.GetString($"body-group-{group}")),
                ("level", SharedBodyCultivationSystem.GetLevel(entity.Comp, group))), type: PopupType.Medium);
        }

        // Carry on into the next weakest part while there's tempering left to pour.
        if (entity.Comp.Tempering < 1f || _bodySystem.WeakestGroup(entity.Comp) is not { } next)
        {
            _bodySystem.SetForgeTarget(entity.Comp, null);
            if (_bodySystem.ReadyForTribulation(entity.Comp))
                _noticeSystem.Notify(entity, Loc.GetString("body-forge-ready-for-tribulation"), type: PopupType.Medium);
            return;
        }

        _bodySystem.SetForgeTarget(entity.Comp, next);
        args.Repeat = true;
    }

    #endregion

    #region Tribulation of Flesh

    /// <summary>
    ///     How ready the body is for its Tribulation: fed, rested, rooted in the Mountain stance and not badly hurt. The
    ///         stage's own penalty is taken off when the chance is worked out.
    /// </summary>
    public int TribulationReadiness(Entity<BodyCultivatorComponent> entity)
    {
        var readiness = 50;
        if (_hungerQuery.TryComp(entity, out var hungerComponent)
            && !_hungerSystem.IsHungerBelowState(entity, HungerThreshold.Okay, comp: hungerComponent))
            readiness += 10;

        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            readiness += 10;

        if (entity.Comp.Exhaustion < 10f)
            readiness += 10;

        readiness += _siteSystem.BodyReadinessBonus(entity);

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
            _noticeSystem.Notify(entity, Loc.GetString(entity.Comp.Stage >= SharedBodyCultivationSystem.StageCap(entity.Comp)
                ? (entity.Comp.Committed ? "body-tribulation-peak" : "body-tribulation-mortal-cap")
                : "body-tribulation-not-ready"));
            return;
        }

        StartTribulation(entity);
    }

    public bool StartTribulation(Entity<BodyCultivatorComponent> entity)
    {
        // In Ascension, a contender takes the last step only at the place heaven chose, for twice as long, with the whole
        // station told where.
        var lastStep = _ascensionRuleSystem.IsContenderFinalStep(entity, entity.Comp.Stage == BodyCultivatorComponent.MaxStage - 1);
        if (lastStep && !_ascensionRuleSystem.CheckFinalSite(entity))
            return false;

        // Ten seconds plus two per stage, in cycles of bones breaking and resetting.
        var duration = 10 + 2 * entity.Comp.Stage;
        var cycles = Math.Max(1, (int)Math.Ceiling((double)duration / TribulationCycle.TotalSeconds));

        var readiness = TribulationReadiness(entity);

        var cycle = lastStep ? TribulationCycle * WuxiaAscensionRuleSystem.FinalStepSlowdown : TribulationCycle;
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, cycle, new BodyTribulationDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        _bodySystem.SetTribulation(entity.Comp, cycles, readiness);
        if (lastStep)
            _ascensionRuleSystem.AnnounceFinalStep(entity);
        var chance = (int)(SharedBodyCultivationSystem.TribulationChance(readiness, entity.Comp.Stage) * 100);
        _noticeSystem.Announce(entity, Loc.GetString("body-tribulation-start", ("user", entity.Owner)), type: PopupType.LargeCaution);
        _noticeSystem.Notify(entity, Loc.GetString("body-tribulation-odds",
            ("readiness", readiness - SharedBodyCultivationSystem.TribulationPenalty(entity.Comp.Stage)), ("chance", chance)), type: PopupType.Medium);
        var (strikeChance, strikeDamage) = SharedBodyCultivationSystem.TribulationLightning(entity.Comp.Stage);
        if (strikeChance > 0f)
            _noticeSystem.Notify(entity, Loc.GetString(entity.Comp.Stage >= BodyCultivatorComponent.MaxStage - 1 ? "cultivation-storm-warning-final" : "cultivation-storm-warning",
                ("chance", (int)(strikeChance * 100)), ("damage", (int)strikeDamage)), type: PopupType.Medium);
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
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", 3f + 0.5f * entity.Comp.Stage), ignoreResistances: true, interruptsDoAfters: false);
        _staminaSystem.TakeStaminaDamage(entity, 6f);
        _audioSystem.PlayPvs(CrackSound, entity);
        _visualsSystem.Spark(entity, FxColour.Earth);

        var left = entity.Comp.TribulationCyclesLeft - 1;
        var (strikeChance, strikeDamage) = SharedBodyCultivationSystem.TribulationLightning(entity.Comp.Stage);
        if (left <= 0 && entity.Comp.Stage >= BodyCultivatorComponent.MaxStage - 1)
            strikeChance = 1f;
        if (strikeChance > 0f)
            _cultivationSystem.CallLightning(entity.Owner, strikeChance, strikeDamage);

        _bodySystem.SetTribulation(entity.Comp, left, entity.Comp.TribulationReadiness);
        if (left > 0)
        {
            args.Repeat = true;
            return;
        }

        var readiness = entity.Comp.TribulationReadiness;
        if (_robustRandom.Prob(SharedBodyCultivationSystem.TribulationChance(readiness, entity.Comp.Stage)))
            SucceedTribulation(entity);
        else
            FailTribulation(entity);
    }

    public void SucceedTribulation(Entity<BodyCultivatorComponent> entity)
    {
        _bodySystem.SetTribulation(entity.Comp, 0, 0);
        _bodySystem.AdvanceStage(entity.AsNullable());

        _visualsSystem.Breakthrough(entity);
        var stageName = Loc.GetString($"body-stage-{entity.Comp.Stage}");
        _audioSystem.PlayPvs(StageSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("body-tribulation-success", ("user", entity.Owner), ("stage", stageName)), type: PopupType.Large);
        _noticeSystem.Notify(entity, Loc.GetString($"body-stage-{entity.Comp.Stage}-benefits"), type: PopupType.Medium);
    }

    private void FailTribulation(Entity<BodyCultivatorComponent> entity)
    {
        _bodySystem.SetTribulation(entity.Comp, 0, 0);
        _bodySystem.AddExhaustion(entity.AsNullable(), 30f);
        _stunSystem.TryKnockdown(entity.Owner, TimeSpan.FromSeconds(3));
        // The later the stage, the worse a body that comes apart is hurt.
        if (entity.Comp.Stage >= 4)
            _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", 2f * entity.Comp.Stage), ignoreResistances: true);
        _noticeSystem.Announce(entity, Loc.GetString("body-tribulation-fail", ("user", entity.Owner)), type: PopupType.LargeCaution);
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
            _noticeSystem.Notify(entity, Loc.GetString("body-eternal-heart-recovering"));
            return;
        }

        _bodySystem.SetEternalHeartReady(entity.Comp, _gameTiming.CurTime + entity.Comp.EternalHeartCooldown);
        _bodySystem.SetEternalHeartRevive(entity.Comp, _gameTiming.CurTime + EternalHeartDelay);
        _noticeSystem.Notify(entity, Loc.GetString("body-eternal-heart-refuses"), type: PopupType.Large);
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
        _noticeSystem.Announce(entity, Loc.GetString("body-eternal-heart-restarts", ("user", entity.Owner)), type: PopupType.Large);
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
            _noticeSystem.Notify(userUid, Loc.GetString("body-manual-qi-cultivator"));
            return;
        }

        if (_bodyCultivatorQuery.TryComp(userUid, out var bodyComponent) && (bodyComponent.Committed || !manualEntity.Comp.Commits))
        {
            // Already on the path: the book at least reminds them how to forge, putting the core arts back on the bar.
            EnsureCoreArts((userUid, bodyComponent));
            _noticeSystem.Notify(userUid, Loc.GetString("body-manual-known"));
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
            _noticeSystem.Announce(userUid, Loc.GetString("cultivation-manual-start", ("user", userUid), ("manual", manualEntity.Owner)), type: PopupType.Small);
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
            _noticeSystem.Notify(args.User, Loc.GetString("body-manual-committed"), type: PopupType.Large);
        }
        else
        {
            _noticeSystem.Notify(args.User, Loc.GetString("body-manual-primer"), type: PopupType.Medium);
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
