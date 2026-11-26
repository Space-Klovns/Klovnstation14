using Content.Server._KS14.Cultivation.Components;
using Content.Server.Lightning;
using Content.Server.Lightning.Components;
using Content.Shared.Projectiles;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Drives cultivation: qi recovery, meditation, breakthroughs, manuals, internal injury from heavy blows, and
///         granting each law's techniques as the cultivator reaches the realm that opens them.
/// </summary>
public sealed partial class CultivationSystem : EntitySystem
{
    /// <summary>
    ///     The widest any qi source reaches; sources say how far they really reach.
    /// </summary>
    private const float MaxQiSourceRange = 10f;

    private static readonly TimeSpan RegenInterval = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier BreakthroughSound = new SoundPathSpecifier("/Audio/Effects/church_bell1.ogg");
    private static readonly SoundSpecifier DeviationSound = new SoundPathSpecifier("/Audio/Magic/disintegrate.ogg");
    /// <summary>
    ///     Techniques every cultivator comes to, by the realm that opens them.
    /// </summary>
    public static (EntProtoId Action, CultivationRealm Realm)[] UniversalTechniques => CultivationTechniqueLists.Universal;

    private static readonly SoundSpecifier ThunderSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly EntProtoId TribulationCloud = "KsTribulationCloud";
    private static readonly EntProtoId TribulationLightning = "KsTribulationLightning";

    /// <summary>
    ///     Chance per breakthrough cycle that heaven's lightning falls.
    /// </summary>

    /// <summary>
    ///     Chance a qi deviation tears a heart demon loose.
    /// </summary>
    private const float HeartDemonChance = 0.5f;

    /// <summary>
    ///     The lightning-target priority of grounding rods (3) and Tesla coils (4).
    /// </summary>
    private const int GroundingPriority = 3;

    private static readonly SoundSpecifier LearnSound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    [Dependency] private CultivationDurabilitySystem _durabilitySystem = default!;
    [Dependency] private CultivationLoadoutSystem _loadoutSystem = default!;
    [Dependency] private CultivationSiteSystem _siteSystem = default!;
    [Dependency] private AncestralRingSystem _ringSystem = default!;
    [Dependency] private InheritanceSystem _inheritanceSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private SacredSiteSystem _sacredSiteSystem = default!;
    [Dependency] private CultivationReadinessSystem _readinessSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private HeartDemonSystem _heartDemonSystem = default!;
    [Dependency] private LightningSystem _lightningSystem = default!;
    [Dependency] private WuxiaAscensionRuleSystem _ascensionRuleSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedCultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<GreatSageComponent> _greatSageQuery = default!;
    [Dependency] private EntityQuery<MuscleTendonComponent> _muscleTendonQuery = default!;
    [Dependency] private EntityQuery<NineYangComponent> _nineYangQuery = default!;

    private readonly HashSet<Entity<CultivationQiSourceComponent>> _qiSources = new();
    private readonly HashSet<Entity<LightningTargetComponent>> _lightningTargets = new();

    /// <summary>
    ///     New cultivators still owed a starting manual.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingManuals = new();
    private readonly List<EntityUid> _drainedManuals = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        HandOutStartingManuals();

        var currentTime = _gameTiming.CurTime;
        var cultivatorEnumerator = EntityQueryEnumerator<CultivatorComponent>();
        while (cultivatorEnumerator.MoveNext(out var uid, out var cultivatorComponent))
        {
            if (currentTime < cultivatorComponent.NextRegen)
                continue;

            _cultivationSystem.SetNextRegen(cultivatorComponent, currentTime + RegenInterval);

            var regen = cultivatorComponent.QiRegenPerSecond
                * (float)RegenInterval.TotalSeconds
                * SharedCultivationSystem.RegenMultiplier(cultivatorComponent.InjuryRegenPenalty, cultivatorComponent.InternalInjury)
                * QiSourceMultiplier(uid)
                * (_nineYangQuery.TryComp(uid, out var nineYangComponent) ? nineYangComponent.RegenMultiplier : 1f)
                * (_greatSageQuery.TryComp(uid, out var greatSageComponent) ? greatSageComponent.QiRegenMultiplier : 1f);

            _cultivationSystem.AddQi((uid, cultivatorComponent), regen);
            RefreshReadyGlow((uid, cultivatorComponent));
            RefreshAffordable((uid, cultivatorComponent));
        }
    }

    /// <summary>
    ///     Grey out the techniques they can't pay for right now, and light them back up once their qi recovers.
    /// </summary>
    public void RefreshAffordable(Entity<CultivatorComponent> entity)
    {
        foreach (var actionUid in entity.Comp.TechniqueActions.Values)
        {
            if (_actionsSystem.GetEvent(actionUid) is IQiCostEvent { QiCost: > 0f } qiCost)
                _actionsSystem.SetEnabled(actionUid, entity.Comp.Qi >= qiCost.QiCost);
        }
    }

    private static readonly SpriteSpecifier MeditateIcon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_KS14/Cultivation/actions.rsi"), "meditate");
    private static readonly SpriteSpecifier BreakthroughIcon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_KS14/Cultivation/actions.rsi"), "breakthrough");
    private static readonly SoundSpecifier ReadySound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    /// <summary>
    ///     Once there's insight enough for the next realm, the Meditate button turns gold into Break Through, with a chime
    ///         and a word, once. It turns back after the breakthrough (or if the insight is lost).
    /// </summary>
    private void RefreshReadyGlow(Entity<CultivatorComponent> entity)
    {
        var ready = ReadyToBreakThrough(entity);
        if (ready == entity.Comp.ReadyGlow)
            return;

        _cultivationSystem.SetReadyGlow(entity.Comp, ready);
        if (entity.Comp.MeditateActionEntity is { } actionUid)
        {
            _actionsSystem.SetIcon(actionUid, ready ? BreakthroughIcon : MeditateIcon);
            _actionsSystem.SetIconColor(actionUid, ready ? Color.Gold : Color.White);
        }

        if (!ready)
            return;

        _audioSystem.PlayGlobal(ReadySound, entity);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-ready-to-break-through"), PopupType.Medium);
    }

    /// <summary>
    ///     How strongly heaven and earth's qi pools around someone: the best qi source in range, or one.
    /// </summary>
    public float QiSourceMultiplier(EntityUid uid)
    {
        _qiSources.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(uid).Coordinates, MaxQiSourceRange, _qiSources);

        var best = 1f;
        foreach (var qiSourceEntity in _qiSources)
        {
            if (_transformSystem.InRange((qiSourceEntity.Owner, null), (uid, null), qiSourceEntity.Comp.Range))
                best = MathF.Max(best, qiSourceEntity.Comp.Multiplier);
        }

        return best;
    }

    #region Lifecycle

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CultivatorComponent> entity, ref MapInitEvent args)
    {
        _cultivationSystem.SetNextRegen(entity.Comp, _gameTiming.CurTime + RegenInterval);

        // One button for both: it meditates, and once there's insight enough it breaks through instead.
        EntityUid? meditateAction = null;
        _actionsSystem.AddAction(entity, ref meditateAction, entity.Comp.MeditateAction);
        _cultivationSystem.SetMeditateAction(entity.Comp, meditateAction);

        GrantTechniques(entity);
        _cultivationSystem.UpdateAlerts(entity);
        _durabilitySystem.Refresh(entity);

        // Handed over next tick: a mob spawned already a cultivator may not have its hands set up yet.
        if (entity.Comp.StartingManuals.Count > 0)
            _pendingManuals.Add(entity.Owner);
    }

    private void HandOutStartingManuals()
    {
        if (_pendingManuals.Count == 0)
            return;

        // Drained into a scratch list first: spawning and picking up can raise events that queue more.
        _drainedManuals.Clear();
        _drainedManuals.AddRange(_pendingManuals);
        _pendingManuals.Clear();

        foreach (var uid in _drainedManuals)
        {
            if (TerminatingOrDeleted(uid) || !_cultivatorQuery.TryComp(uid, out var cultivatorComponent)
                || cultivatorComponent.StartingManuals.Count == 0)
                continue;

            var manualUid = Spawn(_robustRandom.Pick(cultivatorComponent.StartingManuals), Transform(uid).Coordinates);
            _handsSystem.PickupOrDrop(uid, manualUid, checkActionBlocker: false);
        }
    }

    [SubscribeLocalEvent]
    private void OnRealmChanged(Entity<CultivatorComponent> entity, ref CultivationRealmChangedEvent args)
    {
        GrantTechniques(entity);
        _durabilitySystem.Refresh(entity);
    }

    /// <summary>
    ///     Grant every technique the cultivator's laws teach at or below their realm that they don't have yet.
    /// </summary>
    public void GrantTechniques(Entity<CultivatorComponent> entity)
    {
        foreach (var (action, realm) in UniversalTechniques)
        {
            // No Concealment Art in Ascension.
            if (action == CultivationTechniqueLists.Concealment && _ascensionRuleSystem.Active())
                continue;

            if (realm <= entity.Comp.Realm)
                GrantTechnique(entity, action);
        }

        var elements = new HashSet<CultivationElement>();
        foreach (var lawId in entity.Comp.Laws)
        {
            if (!ProtoMan.TryIndex(lawId, out var law))
                continue;

            elements.Add(law.Element);
            foreach (var technique in law.Techniques)
            {
                if (technique.Realm <= entity.Comp.Realm)
                    GrantTechnique(entity, technique.Action);
            }
        }

        // The Demonic Path's arts, and for a master, the means to pass it on.
        if (TryComp<DemonicPathComponent>(entity, out var demonicComponent))
        {
            foreach (var (action, realm) in ForbiddenArtsSystem.ForbiddenTechniques)
            {
                if (realm <= entity.Comp.Realm)
                    GrantTechnique(entity, action);
            }

            if (demonicComponent.Master)
                GrantTechnique(entity, ForbiddenArtsSystem.TransmitAction);
        }

        // Two laws' qi resonating opens a technique of its own.
        foreach (var combo in ProtoMan.EnumeratePrototypes<CultivationComboPrototype>())
        {
            if (combo.Realm > entity.Comp.Realm || !elements.Contains(combo.ElementOne) || !elements.Contains(combo.ElementTwo)
                || _cultivationSystem.HasTechnique(entity.Comp, combo.Action))
                continue;

            if (GrantTechnique(entity, combo.Action))
            {
                _noticeSystem.Notify(entity, Loc.GetString("cultivation-combo-resonates",
                    ("one", Loc.GetString($"cultivation-element-{combo.ElementOne}")),
                    ("two", Loc.GetString($"cultivation-element-{combo.ElementTwo}"))), type: PopupType.Medium);
            }
        }

        RefreshReach(entity);
    }

    /// <returns>True if the technique was newly granted.</returns>
    private bool GrantTechnique(Entity<CultivatorComponent> entity, EntProtoId action)
    {
        // Outgrown: the stronger technique it grew into is already known.
        if (_cultivationSystem.HasTechnique(entity.Comp, action)
            || CultivationUpgrades.UpgradeOf(action) is { } upgrade && _cultivationSystem.HasTechnique(entity.Comp, upgrade))
            return false;

        EntityUid? actionUid = null;
        if (!_actionsSystem.AddAction(entity, ref actionUid, action))
            return false;

        _cultivationSystem.RecordTechnique(entity.Comp, action, actionUid.Value);

        // It grows out of one they had: it takes that one's place. Otherwise it's filed like any new technique.
        var replaced = false;
        foreach (var (chainUpgrade, replaces) in CultivationUpgrades.Chains)
        {
            if (chainUpgrade != action || _cultivationSystem.ForgetTechnique(entity.Comp, replaces) is not { } oldUid)
                continue;

            _loadoutSystem.Replace(entity, oldUid, actionUid.Value);
            replaced = true;
        }

        if (!replaced)
            _loadoutSystem.Learned(entity, actionUid.Value);

        return true;
    }

    #endregion

    #region Spending qi

    /// <summary>
    ///     Pay for a technique. Fails with a popup if there isn't enough qi. With internal injuries, paying can twist
    ///         the meridians and hurt you, but the technique still goes off.
    /// </summary>
    /// <summary>
    ///     Is their cultivation sealed by the eight trigrams? Tells them so if it is.
    /// </summary>
    public bool IsSealed(EntityUid uid)
    {
        if (!TryComp<CultivationSealedComponent>(uid, out var sealedComponent))
            return false;

        if (_gameTiming.CurTime >= sealedComponent.Until)
        {
            RemComp<CultivationSealedComponent>(uid);
            return false;
        }

        _noticeSystem.Notify(uid, Loc.GetString("legendary-sealed"), type: PopupType.SmallCaution);
        return true;
    }

    /// <summary>
    ///     Whether they could pay for a technique right now, without paying. Says why not if they can't.
    /// </summary>
    public bool HasQi(EntityUid userUid, float qiCost)
    {
        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent) || IsSealed(userUid))
            return false;

        if (cultivatorComponent.Qi >= qiCost)
            return true;

        _noticeSystem.Notify(userUid, Loc.GetString("cultivation-not-enough-qi-for", ("qi", (int)cultivatorComponent.Qi), ("cost", (int)qiCost)),
            type: PopupType.SmallCaution);
        return false;
    }

    public bool TryUseTechnique(EntityUid userUid, float qiCost)
    {
        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent))
            return false;

        if (IsSealed(userUid))
            return false;

        if (!_cultivationSystem.TrySpendQi((userUid, cultivatorComponent), qiCost))
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-not-enough-qi-for", ("qi", (int)cultivatorComponent.Qi), ("cost", (int)qiCost)),
                type: PopupType.SmallCaution);
            return false;
        }

        if (qiCost > 0f)
        {
            _cultivationSystem.SetLastTechnique(cultivatorComponent, _gameTiming.CurTime);
            RefreshAffordable((userUid, cultivatorComponent));
        }

        // Any technique gives away a concealed cultivator.
        if (RemComp<CultivationConcealedComponent>(userUid))
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-concealment-broken"), type: PopupType.SmallCaution);

        // Every technique shows: a ring of qi in the colour of the caster's Root Law.
        if (qiCost > 0f)
            _visualsSystem.Cast(userUid);

        var backlashChance = cultivatorComponent.BacklashChancePerInjury * (float)cultivatorComponent.InternalInjury;
        if (backlashChance > 0f && _robustRandom.Prob(backlashChance))
        {
            _damageableSystem.TryChangeDamage(userUid, Damage("Blunt", 5), ignoreResistances: true, interruptsDoAfters: false);
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-backlash"), type: PopupType.MediumCaution);
        }

        return true;
    }

    /// <summary>
    ///     How much stronger a qi cultivator's techniques hit at their realm: one and a half again at Foundation
    ///         Establishment, two and a half times at Golden Core and four times at Nascent Soul.
    /// </summary>
    public static float PowerFor(CultivationRealm realm) => realm switch
    {
        CultivationRealm.FoundationEstablishment => 1.5f,
        CultivationRealm.GoldenCore => 2.5f,
        CultivationRealm.NascentSoul => 4f,
        _ => 1f,
    };

    /// <summary>
    ///     The realm power of a caster; 1 for anyone who isn't a qi cultivator.
    /// </summary>
    public float Power(EntityUid? casterUid)
    {
        if (casterUid is not { } uid || !_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            return 1f;

        // Demonic qi is wild and vicious: every technique of someone on the Demonic Path hits harder.
        var power = PowerFor(cultivatorComponent.Realm);
        return HasComp<DemonicPathComponent>(uid) ? power * DemonicPowerMultiplier : power;
    }

    /// <summary>
    ///     How much harder the Demonic Path's techniques hit.
    /// </summary>
    public const float DemonicPowerMultiplier = 1.3f;

    /// <summary>
    ///     Technique damage, scaled by the caster's realm power.
    /// </summary>
    public DamageSpecifier Empowered(EntityUid? casterUid, DamageSpecifier damage)
    {
        var power = Power(casterUid);
        return power == 1f ? damage : damage * power;
    }

    /// <summary>
    ///     A technique projectile hits as hard as its caster's realm.
    /// </summary>
    public void EmpowerProjectile(EntityUid projectileUid, EntityUid casterUid)
    {
        if (TryComp<ProjectileComponent>(projectileUid, out var projectileComponent))
            projectileComponent.Damage = Empowered(casterUid, projectileComponent.Damage);
    }

    /// <summary>
    ///     Realms above Qi Condensation; 0 for anyone who isn't a qi cultivator.
    /// </summary>
    public int RealmSteps(EntityUid? casterUid)
    {
        return casterUid is { } uid && _cultivatorQuery.TryComp(uid, out var cultivatorComponent) ? (int)cultivatorComponent.Realm : 0;
    }

    /// <summary>
    ///     An area technique's radius: one tile wider for every realm above Qi Condensation.
    /// </summary>
    public float AreaRange(EntityUid? casterUid, float baseRange) => baseRange + RealmSteps(casterUid);

    /// <summary>
    ///     How far a technique flings someone: one tile further for every realm above Qi Condensation.
    /// </summary>
    public float ThrowReach(EntityUid? casterUid, float baseDistance) => baseDistance + RealmSteps(casterUid);

    /// <summary>
    ///     How much further a targeted technique can be aimed for every realm above Qi Condensation.
    /// </summary>
    public const float ReachPerRealm = 2f;

    /// <summary>
    ///     Every aimed technique the cultivator knows reaches as far as their realm allows.
    /// </summary>
    public void RefreshReach(Entity<CultivatorComponent> entity)
    {
        var extra = ReachPerRealm * (int)entity.Comp.Realm;
        foreach (var actionUid in entity.Comp.TechniqueActions.Values)
        {
            if (!TryComp<TargetActionComponent>(actionUid, out var targetComponent)
                || MetaData(actionUid).EntityPrototype is not { } prototype
                || !prototype.TryGetComponent<TargetActionComponent>(out var baseComponent, EntityManager.ComponentFactory)
                || baseComponent.Range <= 0f)
                continue;

            _actionsSystem.SetTargetRange((actionUid, targetComponent), baseComponent.Range + extra);
        }
    }

    public static DamageSpecifier Damage(string type, float amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add(type, FixedPoint2.New(amount));
        return damage;
    }

    #endregion

    #region Internal injury

    [SubscribeLocalEvent]
    private void OnDamageChanged(Entity<CultivatorComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        var dealt = FixedPoint2.Zero;
        foreach (var amount in delta.DamageDict.Values)
        {
            if (amount > 0)
                dealt += amount;
        }

        if (dealt < HeavyBlowThreshold(entity))
            return;

        if (_cultivationSystem.AddInternalInjury(entity.AsNullable()))
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-internal-injury"), type: PopupType.MediumCaution);
    }

    /// <summary>
    ///     How hard a single blow must be to bruise the meridians, after any arts that toughen them.
    /// </summary>
    public float HeavyBlowThreshold(Entity<CultivatorComponent> entity)
    {
        var threshold = entity.Comp.HeavyBlowThreshold;
        if (_nineYangQuery.TryComp(entity, out var nineYangComponent))
            threshold += nineYangComponent.HeavyBlowBonus;
        if (_muscleTendonQuery.TryComp(entity, out var muscleTendonComponent))
            threshold += muscleTendonComponent.HeavyBlowBonus;

        return threshold;
    }

    #endregion

    #region Meditation

    [SubscribeLocalEvent]
    private void OnMeditateAction(Entity<CultivatorComponent> entity, ref CultivationMeditateActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ReadyToBreakThrough(entity))
            TryBreakthrough(entity);
        else
            StartMeditation(entity);
    }

    /// <summary>
    ///     Whether they have all the insight their next step needs (and there is a next step).
    /// </summary>
    public bool ReadyToBreakThrough(Entity<CultivatorComponent> entity)
    {
        return _cultivationSystem.GetInsightRequired(entity.Comp) is { } required && !AtAscensionPeak(entity)
            && entity.Comp.Insight >= required;
    }

    public bool StartMeditation(Entity<CultivatorComponent> entity)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, entity.Comp.MeditateDuration, new CultivationMeditateDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        _noticeSystem.Announce(entity, Loc.GetString("cultivation-meditate-start", ("user", entity.Owner)), type: PopupType.Small);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnMeditateDoAfter(Entity<CultivatorComponent> entity, ref CultivationMeditateDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var multiplier = QiSourceMultiplier(entity);

        // Where they sit matters: a mat, the five elements around it, being alone, and the station's wild qi.
        var insightMultiplier = multiplier * _siteSystem.Evaluate(entity).Multiplier * _ringSystem.InsightMultiplier(entity)
            * _sacredSiteSystem.Multiplier(entity)
            + _siteSystem.StationQiCycle(entity);
        if (entity.Comp.ForcedCirculation)
        {
            // Twice the insight, driven past the safe limit.
            insightMultiplier *= 2f;
            if (_robustRandom.Prob(entity.Comp.ForcedCirculationRisk) && _cultivationSystem.AddInternalInjury(entity.AsNullable()))
                _noticeSystem.Notify(entity, Loc.GetString("cultivation-forced-circulation-backlash"), type: PopupType.MediumCaution);
        }

        var insightEvent = new CultivationInsightMultiplierEvent(insightMultiplier);
        RaiseLocalEvent(entity, ref insightEvent);
        // At the peak of an Ascension round there's no insight left to gain; meditation only restores qi.
        if (AtAscensionPeak(entity))
        {
            if (entity.Comp.Insight > 0f)
                _cultivationSystem.ScaleInsight(entity.AsNullable(), 0f);
        }
        else
        {
            _cultivationSystem.AddInsight(entity.AsNullable(), entity.Comp.MeditateInsight * insightEvent.Multiplier);
        }
        _cultivationSystem.AddQi(entity.AsNullable(), entity.Comp.MeditateQi * multiplier);

        if (_cultivationSystem.CountMeditation(entity.AsNullable()))
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-injury-eased"));

        _visualsSystem.Motes(entity, _visualsSystem.ColourOf(entity), TimeSpan.FromSeconds(2));
        var meditatedEvent = new CultivationMeditatedEvent();
        RaiseLocalEvent(entity, ref meditatedEvent);

        if (_cultivationSystem.ReadyToBreakThrough(entity.Comp))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-ready-to-break-through"), type: PopupType.Medium);
            return;
        }

        // Keep sitting while there's still something to gain.
        args.Repeat = _cultivationSystem.GetInsightRequired(entity.Comp) != null
            || entity.Comp.InternalInjury > 0
            || entity.Comp.Qi < _cultivationSystem.GetMaxQi(entity.Comp);
    }

    #endregion

    #region Breakthrough

    /// <summary>
    ///     How ready someone is to break through, 70 or more being certain. Anything that helps (pills, a tempered
    ///         Dao heart, an inheritance) adds to it through <see cref="CultivationBreakthroughReadinessEvent"/>.
    /// </summary>
    public int BreakthroughReadiness(Entity<CultivatorComponent> entity)
    {
        return BreakthroughReadiness(entity, out _);
    }

    /// <summary>
    ///     How ready they are, and what went into it, for the breakdown shown to them.
    /// </summary>
    public int BreakthroughReadiness(Entity<CultivatorComponent> entity, out List<ReadinessFactor> factors)
    {
        factors = new List<ReadinessFactor>();
        if (entity.Comp.InternalInjury > 0)
            factors.Add(new ReadinessFactor("injury", -entity.Comp.ReadinessPerInjury * entity.Comp.InternalInjury));
        if (QiSourceMultiplier(entity) > 1f)
            factors.Add(new ReadinessFactor("qi-source", 15));

        var maxQi = _cultivationSystem.GetMaxQi(entity.Comp);
        if (entity.Comp.Qi >= maxQi * 0.9f)
            factors.Add(new ReadinessFactor("full-qi", 10));
        else if (entity.Comp.Qi < maxQi * 0.5f)
            factors.Add(new ReadinessFactor("low-qi", -10));

        if (entity.Comp.DaoHeartTempered)
            factors.Add(new ReadinessFactor("dao-heart", 15));

        var siteBonus = _siteSystem.Evaluate(entity).ReadinessBonus;
        if (siteBonus != 0)
            factors.Add(new ReadinessFactor("site", siteBonus));

        _readinessSystem.AddCommonPenalties(entity, factors, entity.Comp.LastTechniqueAt, entity.Comp.LastBreakthroughAt);

        var readiness = CultivationReadinessSystem.Total(entity.Comp.BaseReadiness, factors);
        var ev = new CultivationBreakthroughReadinessEvent(readiness);
        RaiseLocalEvent(entity, ref ev);
        if (ev.Readiness != readiness)
            factors.Add(new ReadinessFactor("other", ev.Readiness - readiness));

        return ev.Readiness;
    }

    /// <summary>
    ///     Try to break through to the next realm (or, at Nascent Soul, to ascend), telling them why not if they can't.
    /// </summary>
    public void TryBreakthrough(Entity<CultivatorComponent> entity)
    {
        if (_cultivationSystem.GetInsightRequired(entity.Comp) is not { } required || AtAscensionPeak(entity))
        {
            _noticeSystem.Notify(entity, Loc.GetString(AtAscensionPeak(entity) ? "cultivation-at-peak-ascension" : "cultivation-at-peak"),
                PopupType.Medium);
            return;
        }

        if (entity.Comp.Insight < required)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-not-ready",
                ("insight", (int)entity.Comp.Insight), ("required", (int)required)));
            return;
        }

        if (entity.Comp.Realm >= CultivationRealm.NascentSoul)
        {
            StartAscension(entity);
            return;
        }

        StartBreakthrough(entity);
    }

    /// <summary>
    ///     In Wuxia: Ascension, Nascent Soul is the peak: there's no final ascension out of the round to work towards.
    /// </summary>
    public bool AtAscensionPeak(Entity<CultivatorComponent> entity)
    {
        return entity.Comp.Realm >= CultivationRealm.NascentSoul && _ascensionRuleSystem.Active();
    }

    /// <summary>
    ///     Begin enduring heaven's tribulation: three cycles, and two more for every realm already climbed.
    /// </summary>
    public bool StartBreakthrough(Entity<CultivatorComponent> entity)
    {
        // In Ascension, a contender takes the last step only at the place heaven chose, for twice as long, with the whole
        // station told where.
        var lastStep = _ascensionRuleSystem.IsContenderFinalStep(entity, entity.Comp.Realm == CultivationRealm.GoldenCore);
        if (lastStep && !_ascensionRuleSystem.CheckFinalSite(entity))
            return false;

        var readiness = BreakthroughReadiness(entity, out var readinessFactors);
        var cycle = lastStep ? entity.Comp.BreakthroughCycle * WuxiaAscensionRuleSystem.FinalStepSlowdown : entity.Comp.BreakthroughCycle;
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, cycle, new CultivationBreakthroughDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        if (lastStep)
            _ascensionRuleSystem.AnnounceFinalStep(entity);

        _cultivationSystem.SetBreakthrough(entity.Comp, 3 + 2 * (int)entity.Comp.Realm, readiness);
        _audioSystem.PlayPvs(ThunderSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-breakthrough-start", ("user", entity.Owner)), type: PopupType.Medium);
        var effective = SharedCultivationSystem.EffectiveReadiness(readiness, entity.Comp.Realm);
        _noticeSystem.Notify(entity, Loc.GetString(effective >= 70 ? "cultivation-readiness-stable" : "cultivation-readiness-risky",
            ("readiness", effective), ("chance", (int)(SharedCultivationSystem.BreakthroughChance(readiness, entity.Comp.Realm) * 100))),
            type: PopupType.Medium);
        if (entity.Comp.Realm > CultivationRealm.QiCondensation)
            readinessFactors.Add(new ReadinessFactor("realm", effective - readiness));
        _noticeSystem.ChatOnly(entity, Loc.GetString("cultivation-readiness-breakdown", ("base", entity.Comp.BaseReadiness),
            ("factors", _readinessSystem.Describe(readinessFactors))));
        var (strikeChance, strikeDamage) = BreakthroughLightning(entity.Comp.Realm);
        _noticeSystem.Notify(entity, Loc.GetString(entity.Comp.Realm >= CultivationRealm.GoldenCore ? "cultivation-storm-warning-final" : "cultivation-storm-warning",
            ("chance", (int)(strikeChance * 100)), ("damage", (int)strikeDamage)), type: PopupType.Medium);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnBreakthroughDoAfter(Entity<CultivatorComponent> entity, ref CultivationBreakthroughDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled)
        {
            // Stopping halfway is its own deviation.
            _cultivationSystem.SetBreakthrough(entity.Comp, 0, 0);
            Deviate(entity);
            return;
        }

        var cyclesLeft = entity.Comp.BreakthroughCyclesLeft - 1;
        CallLightning(entity, final: cyclesLeft <= 0);

        _cultivationSystem.SetBreakthrough(entity.Comp, cyclesLeft, entity.Comp.BreakthroughReadiness);
        if (cyclesLeft > 0)
        {
            args.Repeat = true;
            return;
        }

        var readiness = entity.Comp.BreakthroughReadiness;
        _cultivationSystem.SetBreakthrough(entity.Comp, 0, 0);
        if (_robustRandom.Prob(SharedCultivationSystem.BreakthroughChance(readiness, entity.Comp.Realm)))
            BreakThrough(entity);
        else
            Deviate(entity);
    }

    /// <summary>
    ///     How often heaven's lightning falls in each cycle of a breakthrough out of this realm, and how hard it hits.
    ///         Leaving Qi Condensation is a light test; the step into Nascent Soul is a real storm, and its last cycle
    ///         always strikes. Grounding rods nearby still draw bolts away, so preparing for it is the fair answer.
    /// </summary>
    public static (float Chance, float Damage) BreakthroughLightning(CultivationRealm realm)
    {
        var tier = (int)realm;
        return (0.3f + 0.15f * tier, 6f + 8f * tier);
    }

    private void CallLightning(Entity<CultivatorComponent> entity, bool final = false)
    {
        var (chance, damage) = BreakthroughLightning(entity.Comp.Realm);
        if (final && entity.Comp.Realm >= CultivationRealm.GoldenCore)
            chance = 1f;

        CallLightning(entity.Owner, chance, damage);
    }

    /// <summary>
    ///     Heaven notices. A bolt may fall on the one breaking through, unless a grounding rod nearby draws it away.
    ///         Body cultivators' later tribulations call it down too.
    /// </summary>
    public void CallLightning(EntityUid target, float chance, float damage)
    {
        if (!_robustRandom.Prob(chance))
            return;

        var coordinates = Transform(target).Coordinates;
        var cloudUid = Spawn(TribulationCloud, coordinates.Offset(new System.Numerics.Vector2(0f, 3f)));

        _lightningTargets.Clear();
        _entityLookupSystem.GetEntitiesInRange(coordinates, 6f, _lightningTargets);
        foreach (var rod in _lightningTargets)
        {
            // Only real lightning sinks (grounding rods and Tesla coils, set up and anchored) draw heaven's bolts away;
            // people, machines and doors are lightning targets too, but heaven isn't fooled by them.
            if (rod.Owner == target
                || rod.Comp.Priority < GroundingPriority
                || !Transform(rod).Anchored
                || !_robustRandom.Prob(0.6f))
                continue;

            _lightningSystem.ShootLightning(cloudUid, rod.Owner, lightningPrototype: TribulationLightning, triggerLightningEvents: false);
            _visualsSystem.HeavenBolt(rod.Owner);
            _noticeSystem.Announce(rod.Owner, Loc.GetString("cultivation-lightning-grounded", ("rod", rod.Owner)), type: PopupType.Small);
            return;
        }

        _lightningSystem.ShootLightning(cloudUid, target, lightningPrototype: TribulationLightning, triggerLightningEvents: false);
        _visualsSystem.HeavenBolt(target);
        // Heaven's heavier strikes come down as a volley of bolts, one after another.
        if (damage >= 15f)
        {
            _visualsSystem.HeavenBolt(target, 1);
            _visualsSystem.HeavenBolt(target, 2);
        }

        _damageableSystem.TryChangeDamage(target, Damage("Shock", damage), ignoreResistances: true, interruptsDoAfters: false);
        _noticeSystem.Announce(target, Loc.GetString("cultivation-lightning-strikes", ("user", target)), type: PopupType.MediumCaution);
    }

    /// <summary>
    ///     Succeed at a breakthrough: the next realm, a full dantian, and the body remade a little.
    /// </summary>
    public void BreakThrough(Entity<CultivatorComponent> entity)
    {
        if (entity.Comp.Realm >= CultivationRealm.NascentSoul)
            return;

        var newRealm = entity.Comp.Realm + 1;
        _cultivationSystem.SetRealm(entity.AsNullable(), newRealm);
        if (newRealm == CultivationRealm.NascentSoul)
        {
            var peak = new CultivationPeakReachedEvent(entity);
            RaiseLocalEvent(ref peak);
        }

        _loadoutSystem.RefreshSlots(entity);
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-30));

        _audioSystem.PlayPvs(BreakthroughSound, entity);
        _visualsSystem.Breakthrough(entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-breakthrough-success",
            ("user", entity.Owner), ("realm", Loc.GetString($"cultivation-realm-{newRealm}"))), type: PopupType.Large);

        if (newRealm == CultivationRealm.FoundationEstablishment)
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-second-law-slot"), type: PopupType.Medium);
    }

    /// <summary>
    ///     Fail a breakthrough: qi deviation tears the meridians and scatters half the insight gathered.
    /// </summary>
    public void Deviate(Entity<CultivatorComponent> entity)
    {
        _visualsSystem.Wave(entity, FxColour.Blood, big: true);
        _cultivationSystem.ScaleInsight(entity.AsNullable(), 0.5f);

        // The higher the realm, the harder a deviation bites: more backlash, deeper injuries, likelier heart demons.
        var realm = (int)entity.Comp.Realm;

        // The backlash is a heavy blow in its own right; only add the stack ourselves if it didn't already.
        var injuryBefore = entity.Comp.InternalInjury;
        _damageableSystem.TryChangeDamage(entity.Owner, Damage("Blunt", 20 + 15 * realm), ignoreResistances: true, interruptsDoAfters: false);
        if (entity.Comp.InternalInjury == injuryBefore)
            _cultivationSystem.AddInternalInjury(entity.AsNullable());
        if (realm >= 2)
            _cultivationSystem.AddInternalInjury(entity.AsNullable(), stacks: realm - 1);

        _audioSystem.PlayPvs(DeviationSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-deviation", ("user", entity.Owner)), type: PopupType.LargeCaution);

        // Doubt tears free and takes your face.
        if (_robustRandom.Prob(MathF.Min(0.9f, HeartDemonChance + 0.15f * realm)))
            _heartDemonSystem.Summon(entity.Owner);
    }

    #endregion

    #region Manuals

    [SubscribeLocalEvent]
    private void OnManualUsed(Entity<CultivationManualComponent> manualEntity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var userUid = args.User;

        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent))
        {
            // Only a master's transmission can awaken a mortal, and never one committed to the path of the flesh.
            if (!manualEntity.Comp.Awakens
                || TryComp<BodyCultivatorComponent>(userUid, out var bodyComponent) && bodyComponent.Committed)
            {
                _noticeSystem.Notify(userUid, Loc.GetString("cultivation-manual-gibberish"));
                return;
            }

            StartReading(userUid, manualEntity, TimeSpan.FromSeconds(10));
            return;
        }

        if (_cultivationSystem.KnowsLaw(cultivatorComponent, manualEntity.Comp.Law))
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-manual-known"));
            return;
        }

        if (cultivatorComponent.Laws.Count >= SharedCultivationSystem.LawSlots(cultivatorComponent.Realm))
        {
            _noticeSystem.Notify(userUid, Loc.GetString(cultivatorComponent.Realm < CultivationRealm.FoundationEstablishment
                ? "cultivation-manual-no-room-yet"
                : "cultivation-manual-no-room"));
            return;
        }

        StartReading(userUid, manualEntity, cultivatorComponent.ReadManualDuration);
    }

    private void StartReading(EntityUid userUid, EntityUid manualUid, TimeSpan duration)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, userUid, duration, new CultivationReadManualDoAfterEvent(), manualUid, used: manualUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(userUid, Loc.GetString("cultivation-manual-start", ("user", userUid), ("manual", manualUid)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnManualRead(Entity<CultivationManualComponent> manualEntity, ref CultivationReadManualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!_cultivatorQuery.HasComp(args.User) && manualEntity.Comp.Awakens)
        {
            EnsureComp<CultivatorComponent>(args.User);
            _noticeSystem.Notify(args.User, Loc.GetString("cultivation-awakened"), type: PopupType.Large);
            _inheritanceSystem.SlipAwakened(manualEntity, args.User);
        }

        TryLearnFromManual(args.User, manualEntity);
    }

    public bool TryLearnFromManual(EntityUid userUid, Entity<CultivationManualComponent> manualEntity)
    {
        return TryLearnLaw(userUid, manualEntity.Comp.Law);
    }

    /// <summary>
    ///     Take up a cultivation law, with its techniques, as a manual would teach it.
    /// </summary>
    public bool TryLearnLaw(EntityUid userUid, ProtoId<CultivationLawPrototype> lawId)
    {
        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent)
            || !ProtoMan.TryIndex(lawId, out var law))
            return false;

        var isRoot = cultivatorComponent.Laws.Count == 0;
        if (!_cultivationSystem.TryLearnLaw((userUid, cultivatorComponent), lawId))
            return false;

        GrantTechniques((userUid, cultivatorComponent));
        _audioSystem.PlayPvs(LearnSound, userUid);
        _noticeSystem.Notify(userUid, Loc.GetString(isRoot ? "cultivation-learned-root" : "cultivation-learned-complementary",
            ("law", Loc.GetString(law.Name))), type: PopupType.Medium);

        if (isRoot)
        {
            _noticeSystem.Notify(userUid, Loc.GetString(law.RootDescription), type: PopupType.Small);
            // A first law starts the path: a few lines on how to walk it, kept in chat.
            _noticeSystem.ChatOnly(userUid, Loc.GetString("cultivation-guide-qi"));
        }

        return true;
    }

    #endregion
}
