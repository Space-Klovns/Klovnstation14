using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
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
    private static readonly SoundSpecifier LearnSound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedCultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<MuscleTendonComponent> _muscleTendonQuery = default!;
    [Dependency] private EntityQuery<NineYangComponent> _nineYangQuery = default!;

    private readonly HashSet<Entity<CultivationQiSourceComponent>> _qiSources = new();

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
                * (_nineYangQuery.TryComp(uid, out var nineYangComponent) ? nineYangComponent.RegenMultiplier : 1f);

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
        foreach (var source in _qiSources)
        {
            if (_transformSystem.InRange((source.Owner, null), (uid, null), source.Comp.Range))
                best = MathF.Max(best, source.Comp.Multiplier);
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
        foreach (var lawId in entity.Comp.Laws)
        {
            if (!ProtoMan.TryIndex(lawId, out var law))
                continue;

            foreach (var technique in law.Techniques)
            {
                if (technique.Realm > entity.Comp.Realm || _cultivationSystem.HasTechnique(entity.Comp, technique.Action))
                    continue;

                EntityUid? actionUid = null;
                if (_actionsSystem.AddAction(entity, ref actionUid, technique.Action))
                    _cultivationSystem.RecordTechnique(entity.Comp, technique.Action, actionUid.Value);
            }
        }
    }

    #endregion

    #region Spending qi

    /// <summary>
    ///     Pay for a technique. Fails with a popup if there isn't enough qi. With internal injuries, paying can twist
    ///         the meridians and hurt you, but the technique still goes off.
    /// </summary>
    public bool TryUseTechnique(EntityUid user, float qiCost)
    {
        if (!_cultivatorQuery.TryComp(user, out var cultivatorComponent))
            return false;

        if (!_cultivationSystem.TrySpendQi((user, cultivatorComponent), qiCost))
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-not-enough-qi"), user, user, PopupType.SmallCaution);
            return false;
        }

        var backlashChance = cultivatorComponent.BacklashChancePerInjury * cultivatorComponent.InternalInjury;
        if (backlashChance > 0f && _robustRandom.Prob(backlashChance))
        {
            _damageableSystem.TryChangeDamage(user, Damage("Blunt", 5), ignoreResistances: true, interruptsDoAfters: false);
            _popupSystem.PopupEntity(Loc.GetString("cultivation-backlash"), user, user, PopupType.MediumCaution);
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
            _popupSystem.PopupEntity(Loc.GetString("cultivation-internal-injury"), entity, entity, PopupType.MediumCaution);
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

        _popupSystem.PopupEntity(Loc.GetString("cultivation-meditate-start", ("user", entity.Owner)), entity, PopupType.Small);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnMeditateDoAfter(Entity<CultivatorComponent> entity, ref CultivationMeditateDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var multiplier = QiSourceMultiplier(entity);
        _cultivationSystem.AddInsight(entity.AsNullable(), entity.Comp.MeditateInsight * multiplier);
        _cultivationSystem.AddQi(entity.AsNullable(), entity.Comp.MeditateQi * multiplier);

        if (_cultivationSystem.CountMeditation(entity.AsNullable()))
            _popupSystem.PopupEntity(Loc.GetString("cultivation-injury-eased"), entity, entity);

        if (_cultivationSystem.ReadyToBreakThrough(entity.Comp))
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-ready-to-break-through"), entity, entity, PopupType.Medium);
            return;
        }

        // Keep sitting while there's still something to gain.
        args.Repeat = _cultivationSystem.GetInsightRequired(entity.Comp) != null
            || entity.Comp.InternalInjury > 0
            || entity.Comp.Qi < _cultivationSystem.GetMaxQi(entity.Comp);
    }

    #endregion

    #region Breakthrough

    [SubscribeLocalEvent]
    private void OnBreakthroughAction(Entity<CultivatorComponent> entity, ref CultivationBreakthroughActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (_cultivationSystem.GetInsightRequired(entity.Comp) is not { } required)
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-at-peak"), entity, entity);
            return;
        }

        if (entity.Comp.Insight < required)
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-not-ready",
                ("insight", (int)entity.Comp.Insight), ("required", (int)required)), entity, entity);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, entity.Comp.BreakthroughDuration, new CultivationBreakthroughDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _popupSystem.PopupEntity(Loc.GetString("cultivation-breakthrough-start", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnBreakthroughDoAfter(Entity<CultivatorComponent> entity, ref CultivationBreakthroughDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var chance = SharedCultivationSystem.DeviationChance(entity.Comp.BaseDeviationChance, entity.Comp.DeviationChancePerInjury, entity.Comp.InternalInjury);
        if (_robustRandom.Prob(chance))
            Deviate(entity);
        else
            BreakThrough(entity);
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
        _popupSystem.PopupEntity(Loc.GetString("cultivation-breakthrough-success",
            ("user", entity.Owner), ("realm", Loc.GetString($"cultivation-realm-{newRealm}"))), entity, PopupType.Large);

        if (newRealm == CultivationRealm.FoundationEstablishment)
            _popupSystem.PopupEntity(Loc.GetString("cultivation-second-law-slot"), entity, entity, PopupType.Medium);
    }

    /// <summary>
    ///     Fail a breakthrough: qi deviation tears the meridians and scatters half the insight gathered.
    /// </summary>
    public void Deviate(Entity<CultivatorComponent> entity)
    {
        _cultivationSystem.ScaleInsight(entity.AsNullable(), 0.5f);

        // The backlash is a heavy blow in its own right; only add the stack ourselves if it didn't already.
        var injuryBefore = entity.Comp.InternalInjury;
        _damageableSystem.TryChangeDamage(entity.Owner, Damage("Blunt", 20), ignoreResistances: true, interruptsDoAfters: false);
        if (entity.Comp.InternalInjury == injuryBefore)
            _cultivationSystem.AddInternalInjury(entity.AsNullable());

        _audioSystem.PlayPvs(DeviationSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("cultivation-deviation", ("user", entity.Owner)), entity, PopupType.LargeCaution);
    }

    #endregion

    #region Manuals

    [SubscribeLocalEvent]
    private void OnManualUsed(Entity<CultivationManualComponent> manual, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;

        if (!_cultivatorQuery.TryComp(user, out var cultivatorComponent))
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-manual-gibberish"), user, user);
            return;
        }

        if (_cultivationSystem.KnowsLaw(cultivatorComponent, manual.Comp.Law))
        {
            _popupSystem.PopupEntity(Loc.GetString("cultivation-manual-known"), user, user);
            return;
        }

        if (cultivatorComponent.Laws.Count >= SharedCultivationSystem.LawSlots(cultivatorComponent.Realm))
        {
            _popupSystem.PopupEntity(Loc.GetString(cultivatorComponent.Realm < CultivationRealm.FoundationEstablishment
                ? "cultivation-manual-no-room-yet"
                : "cultivation-manual-no-room"), user, user);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, cultivatorComponent.ReadManualDuration, new CultivationReadManualDoAfterEvent(), manual, used: manual)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _popupSystem.PopupEntity(Loc.GetString("cultivation-manual-start", ("user", user), ("manual", manual.Owner)), user, PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnManualRead(Entity<CultivationManualComponent> manual, ref CultivationReadManualDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        TryLearnFromManual(args.User, manual);
    }

    public bool TryLearnFromManual(EntityUid user, Entity<CultivationManualComponent> manual)
    {
        if (!_cultivatorQuery.TryComp(user, out var cultivatorComponent)
            || !ProtoMan.TryIndex(manual.Comp.Law, out var law))
            return false;

        var isRoot = cultivatorComponent.Laws.Count == 0;
        if (!_cultivationSystem.TryLearnLaw((user, cultivatorComponent), manual.Comp.Law))
            return false;

        GrantTechniques((user, cultivatorComponent));
        _audioSystem.PlayPvs(LearnSound, user);
        _popupSystem.PopupEntity(Loc.GetString(isRoot ? "cultivation-learned-root" : "cultivation-learned-complementary",
            ("law", Loc.GetString(law.Name))), user, user, PopupType.Medium);

        if (isRoot)
            _popupSystem.PopupEntity(Loc.GetString(law.RootDescription), user, user, PopupType.Small);

        return true;
    }

    #endregion
}
