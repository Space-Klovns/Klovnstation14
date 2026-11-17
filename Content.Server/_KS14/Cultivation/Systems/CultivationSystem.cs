using Content.Server._KS14.Cultivation.Components;
using Content.Server.Lightning;
using Content.Server.Lightning.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
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
    public static readonly (EntProtoId Action, CultivationRealm Realm)[] UniversalTechniques =
    {
        ("ActionCultivationSpiritualSense", CultivationRealm.QiCondensation),
        ("ActionJianghuDuel", CultivationRealm.QiCondensation),
        ("ActionCultivationEmptyPalm", CultivationRealm.QiCondensation),
        ("ActionCultivationQinggong", CultivationRealm.QiCondensation),
        ("ActionCultivationWriteTalisman", CultivationRealm.QiCondensation),
        ("ActionCultivationRealmPressure", CultivationRealm.FoundationEstablishment),
        ("ActionJianghuFoundSect", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationAcupointSealing", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationTransmitLaw", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationConcealment", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationForcedCirculation", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationForgetLaw", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationGatheringArray", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationBarrierArray", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationAlarmArray", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationBeastContract", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationSummonBeast", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationVoidStep", CultivationRealm.GoldenCore),
        ("ActionCultivationBodyOutsideBody", CultivationRealm.NascentSoul),
    };

    private static readonly SoundSpecifier ThunderSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly EntProtoId TribulationCloud = "KsTribulationCloud";
    private static readonly EntProtoId TribulationLightning = "KsTribulationLightning";

    /// <summary>
    ///     Chance per breakthrough cycle that heaven's lightning falls.
    /// </summary>
    private const float LightningChance = 0.35f;

    /// <summary>
    ///     Chance a qi deviation tears a heart demon loose.
    /// </summary>
    private const float HeartDemonChance = 0.5f;

    private static readonly SoundSpecifier LearnSound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    [Dependency] private CultivationSiteSystem _siteSystem = default!;
    [Dependency] private AncestralRingSystem _ringSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private HeartDemonSystem _heartDemonSystem = default!;
    [Dependency] private LightningSystem _lightningSystem = default!;
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
        }
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

        EntityUid? meditateAction = null;
        EntityUid? breakthroughAction = null;
        _actionsSystem.AddAction(entity, ref meditateAction, entity.Comp.MeditateAction);
        _actionsSystem.AddAction(entity, ref breakthroughAction, entity.Comp.BreakthroughAction);
        _cultivationSystem.SetMeditateActions(entity.Comp, meditateAction, breakthroughAction);

        GrantTechniques(entity);
        _cultivationSystem.UpdateAlerts(entity);

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
    }

    /// <summary>
    ///     Grant every technique the cultivator's laws teach at or below their realm that they don't have yet.
    /// </summary>
    public void GrantTechniques(Entity<CultivatorComponent> entity)
    {
        foreach (var (action, realm) in UniversalTechniques)
        {
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
    }

    /// <returns>True if the technique was newly granted.</returns>
    private bool GrantTechnique(Entity<CultivatorComponent> entity, EntProtoId action)
    {
        if (_cultivationSystem.HasTechnique(entity.Comp, action))
            return false;

        EntityUid? actionUid = null;
        if (!_actionsSystem.AddAction(entity, ref actionUid, action))
            return false;

        _cultivationSystem.RecordTechnique(entity.Comp, action, actionUid.Value);
        return true;
    }

    #endregion

    #region Spending qi

    /// <summary>
    ///     Pay for a technique. Fails with a popup if there isn't enough qi. With internal injuries, paying can twist
    ///         the meridians and hurt you, but the technique still goes off.
    /// </summary>
    public bool TryUseTechnique(EntityUid userUid, float qiCost)
    {
        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent))
            return false;

        if (!_cultivationSystem.TrySpendQi((userUid, cultivatorComponent), qiCost))
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-not-enough-qi"), type: PopupType.SmallCaution);
            return false;
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
        StartMeditation(entity);
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
        _cultivationSystem.AddInsight(entity.AsNullable(), entity.Comp.MeditateInsight * insightEvent.Multiplier);
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
        var readiness = entity.Comp.BaseReadiness - entity.Comp.ReadinessPerInjury * entity.Comp.InternalInjury;
        if (QiSourceMultiplier(entity) > 1f)
            readiness += 15;
        if (entity.Comp.Qi >= _cultivationSystem.GetMaxQi(entity.Comp) * 0.9f)
            readiness += 10;
        if (entity.Comp.DaoHeartTempered)
            readiness += 15;
        readiness += _siteSystem.Evaluate(entity).ReadinessBonus;

        var ev = new CultivationBreakthroughReadinessEvent(readiness);
        RaiseLocalEvent(entity, ref ev);
        return ev.Readiness;
    }

    [SubscribeLocalEvent]
    private void OnBreakthroughAction(Entity<CultivatorComponent> entity, ref CultivationBreakthroughActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (_cultivationSystem.GetInsightRequired(entity.Comp) is not { } required)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-at-peak"));
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
    ///     Begin enduring heaven's tribulation: three cycles, and two more for every realm already climbed.
    /// </summary>
    public bool StartBreakthrough(Entity<CultivatorComponent> entity)
    {
        var readiness = BreakthroughReadiness(entity);
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, entity.Comp.BreakthroughCycle, new CultivationBreakthroughDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        _cultivationSystem.SetBreakthrough(entity.Comp, 3 + 2 * (int)entity.Comp.Realm, readiness);
        _audioSystem.PlayPvs(ThunderSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-breakthrough-start", ("user", entity.Owner)), type: PopupType.Medium);
        _noticeSystem.Notify(entity, Loc.GetString(readiness >= 70 ? "cultivation-readiness-stable" : "cultivation-readiness-risky",
            ("readiness", readiness)), type: PopupType.Medium);
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

        CallLightning(entity);

        var cyclesLeft = entity.Comp.BreakthroughCyclesLeft - 1;
        _cultivationSystem.SetBreakthrough(entity.Comp, cyclesLeft, entity.Comp.BreakthroughReadiness);
        if (cyclesLeft > 0)
        {
            args.Repeat = true;
            return;
        }

        var readiness = entity.Comp.BreakthroughReadiness;
        _cultivationSystem.SetBreakthrough(entity.Comp, 0, 0);
        if (_robustRandom.Prob(SharedCultivationSystem.BreakthroughChance(readiness)))
            BreakThrough(entity);
        else
            Deviate(entity);
    }

    /// <summary>
    ///     Heaven notices. A bolt may fall on the one breaking through, unless a grounding rod nearby draws it away.
    /// </summary>
    private void CallLightning(Entity<CultivatorComponent> entity, float chance = LightningChance)
    {
        if (!_robustRandom.Prob(chance))
            return;

        var coordinates = Transform(entity).Coordinates;
        var cloudUid = Spawn(TribulationCloud, coordinates.Offset(new System.Numerics.Vector2(0f, 3f)));

        _lightningTargets.Clear();
        _entityLookupSystem.GetEntitiesInRange(coordinates, 6f, _lightningTargets);
        foreach (var rod in _lightningTargets)
        {
            if (!_robustRandom.Prob(0.6f))
                continue;

            _lightningSystem.ShootLightning(cloudUid, rod.Owner, lightningPrototype: TribulationLightning, triggerLightningEvents: false);
            _noticeSystem.Announce(rod.Owner, Loc.GetString("cultivation-lightning-grounded", ("rod", rod.Owner)), type: PopupType.Small);
            return;
        }

        _lightningSystem.ShootLightning(cloudUid, entity, lightningPrototype: TribulationLightning, triggerLightningEvents: false);
        var damage = 5f + 5f * (float)entity.Comp.Realm;
        _damageableSystem.TryChangeDamage(entity.Owner, Damage("Shock", damage), ignoreResistances: true, interruptsDoAfters: false);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-lightning-strikes", ("user", entity.Owner)), type: PopupType.MediumCaution);
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

        // The backlash is a heavy blow in its own right; only add the stack ourselves if it didn't already.
        var injuryBefore = entity.Comp.InternalInjury;
        _damageableSystem.TryChangeDamage(entity.Owner, Damage("Blunt", 20), ignoreResistances: true, interruptsDoAfters: false);
        if (entity.Comp.InternalInjury == injuryBefore)
            _cultivationSystem.AddInternalInjury(entity.AsNullable());

        _audioSystem.PlayPvs(DeviationSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-deviation", ("user", entity.Owner)), type: PopupType.LargeCaution);

        // Doubt tears free and takes your face.
        if (_robustRandom.Prob(HeartDemonChance))
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
        }

        TryLearnFromManual(args.User, manualEntity);
    }

    public bool TryLearnFromManual(EntityUid userUid, Entity<CultivationManualComponent> manualEntity)
    {
        if (!_cultivatorQuery.TryComp(userUid, out var cultivatorComponent)
            || !ProtoMan.TryIndex(manualEntity.Comp.Law, out var law))
            return false;

        var isRoot = cultivatorComponent.Laws.Count == 0;
        if (!_cultivationSystem.TryLearnLaw((userUid, cultivatorComponent), manualEntity.Comp.Law))
            return false;

        GrantTechniques((userUid, cultivatorComponent));
        _audioSystem.PlayPvs(LearnSound, userUid);
        _noticeSystem.Notify(userUid, Loc.GetString(isRoot ? "cultivation-learned-root" : "cultivation-learned-complementary",
            ("law", Loc.GetString(law.Name))), type: PopupType.Medium);

        if (isRoot)
            _noticeSystem.Notify(userUid, Loc.GetString(law.RootDescription), type: PopupType.Small);

        return true;
    }

    #endregion
}
