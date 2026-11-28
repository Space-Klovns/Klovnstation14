using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Inheritance. A master names an heir among their disciples or sworn siblings. The moment the master dies,
///         wherever the heir is, a streak of light carries half the master's cultivation to them: the heir's
///         foundation fills, their next breakthrough (or Tribulation of Flesh) cannot fail, and they may learn one of the
///         master's laws. The master, if revived some other way, starts again from the bottom. An Eternal Heart about to
///         restart its owner keeps everything instead.
/// </summary>
public sealed partial class InheritanceSystem : EntitySystem
{
    private static readonly EntProtoId NameHeirAction = "ActionKsNameHeir";

    [Dependency] private CultivationLoadoutSystem _loadoutSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private JianghuSystem _jianghuSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodyCultivationSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    #region Disciples and heirs

    /// <summary>
    ///     A master takes someone on. Having someone to pass the path to, they can now name an heir.
    /// </summary>
    public void RecordDisciple(EntityUid masterUid, EntityUid discipleUid)
    {
        if (!_mindSystem.TryGetMind(masterUid, out var masterMind, out _) || !_mindSystem.TryGetMind(discipleUid, out var discipleMind, out _)
            || masterMind == discipleMind)
            return;

        EnsureComp<CultivationLineageComponent>(masterMind).Disciples.Add(discipleMind);
        GrantNameHeir(masterUid);
    }

    /// <summary>
    ///     Mark a jade slip with its writer, so whoever it awakens becomes their disciple.
    /// </summary>
    public void MarkSlip(EntityUid slipUid, EntityUid writerUid)
    {
        if (_mindSystem.TryGetMind(writerUid, out var writerMind, out _))
            EnsureComp<JadeSlipTeacherComponent>(slipUid).TeacherMind = writerMind;
    }

    public void SlipAwakened(EntityUid slipUid, EntityUid readerUid)
    {
        if (!TryComp<JadeSlipTeacherComponent>(slipUid, out var teacherComponent) || teacherComponent.TeacherMind is not { } teacherMind
            || !TryComp<MindComponent>(teacherMind, out var mindComponent) || mindComponent.OwnedEntity is not { } teacherUid)
            return;

        RecordDisciple(teacherUid, readerUid);
    }

    public void GrantNameHeir(EntityUid uid)
    {
        if (_loadoutSystem.Knows(uid, NameHeirAction))
            return;

        foreach (var action in _actionsSystem.GetActions(uid))
        {
            if (MetaData(action).EntityPrototype?.ID == NameHeirAction.Id)
                return;
        }

        if (_actionsSystem.AddAction(uid, NameHeirAction) is { } heirActionUid)
            _loadoutSystem.Learned(uid, heirActionUid);
    }

    /// <summary>
    ///     Whom a master could name as heir: their disciples and sworn siblings.
    /// </summary>
    public bool IsCandidate(EntityUid masterUid, EntityUid candidateUid)
    {
        if (masterUid == candidateUid || !_mindSystem.TryGetMind(candidateUid, out var candidateMind, out _))
            return false;

        if (_jianghuSystem.AreSworn(masterUid, candidateUid))
            return true;

        return _mindSystem.TryGetMind(masterUid, out var masterMind, out _) && TryComp<CultivationLineageComponent>(masterMind, out var lineageComponent)
            && lineageComponent.Disciples.Contains(candidateMind);
    }

    [SubscribeLocalEvent]
    private void OnNameHeir(Entity<Content.Shared.Actions.Components.ActionsComponent> entity, ref NameHeirActionEvent args)
    {
        if (args.Handled || !_mindSystem.TryGetMind(args.Performer, out var masterMind, out _))
            return;

        args.Handled = true;
        var lineageComponent = EnsureComp<CultivationLineageComponent>(masterMind);
        if (args.Target == args.Performer)
        {
            lineageComponent.Heir = null;
            _noticeSystem.Notify(args.Performer, Loc.GetString("inheritance-no-heir"), PopupType.Medium);
            return;
        }

        if (!IsCandidate(args.Performer, args.Target) || !_mindSystem.TryGetMind(args.Target, out var heirMind, out _))
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("inheritance-not-candidate", ("target", args.Target)));
            return;
        }

        lineageComponent.Heir = heirMind;
        _noticeSystem.Notify(args.Performer, Loc.GetString("inheritance-named", ("target", args.Target)), PopupType.Medium, alsoToChat: true);
        _noticeSystem.Notify(args.Target, Loc.GetString("inheritance-named-heir", ("user", args.Performer)), PopupType.Medium, alsoToChat: true);
    }

    public EntityUid? HeirOf(EntityUid masterUid)
    {
        return _mindSystem.TryGetMind(masterUid, out var masterMind, out _) && TryComp<CultivationLineageComponent>(masterMind, out var lineageComponent)
            ? lineageComponent.Heir
            : null;
    }

    #endregion

    #region Passing on

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            TryPassOn(args.Target);
    }

    /// <summary>
    ///     A master died: half their cultivation flies to their heir, if they have one living.
    /// </summary>
    public bool TryPassOn(EntityUid masterUid)
    {
        if (!_mindSystem.TryGetMind(masterUid, out var masterMind, out _) || !TryComp<CultivationLineageComponent>(masterMind, out var lineageComponent)
            || lineageComponent.Heir is not { } heirMind)
            return false;

        var qiComponent = CompOrNull<CultivatorComponent>(masterUid);
        var bodyComponent = CompOrNull<BodyCultivatorComponent>(masterUid);
        if (qiComponent == null && (bodyComponent == null || !bodyComponent.Committed))
            return false;

        // An Eternal Heart about to restart its owner keeps the cultivation where it is.
        if (bodyComponent is { EternalHeart: true } && (bodyComponent.EternalHeartReviveAt != null || _gameTiming.CurTime >= bodyComponent.EternalHeartReady))
            return false;

        lineageComponent.Heir = null;
        if (!TryComp<MindComponent>(heirMind, out var heirMindComponent) || heirMindComponent.OwnedEntity is not { } heirUid
            || !_mobStateSystem.IsAlive(heirUid) || !HasComp<HumanoidProfileComponent>(heirUid))
        {
            _noticeSystem.Notify(masterUid, Loc.GetString("inheritance-scatters"), PopupType.MediumCaution);
            return false;
        }

        Streak(masterUid, heirUid);
        var legacy = qiComponent != null ? InheritFromQi((masterUid, qiComponent), heirUid) : InheritFromBody((masterUid, bodyComponent!), heirUid);
        EnsureComp<InheritanceBlessingComponent>(heirUid);
        _noticeSystem.Notify(heirUid, Loc.GetString("inheritance-arrives", ("master", Name(masterUid)), ("legacy", legacy)), PopupType.Large,
            alsoToChat: true);
        return true;
    }

    /// <summary>
    ///     Half the master's qi cultivation goes to the heir: insight, and one of the master's laws if there's room. A body
    ///         cultivator heir takes it as tempering instead.
    /// </summary>
    private string InheritFromQi(Entity<CultivatorComponent> master, EntityUid heirUid)
    {
        var legacy = TotalInsight(master.Comp) / 2f;
        var laws = new List<ProtoId<Shared._KS14.Cultivation.Prototypes.CultivationLawPrototype>>(master.Comp.Laws);
        WipeQi(master);

        if (TryComp<BodyCultivatorComponent>(heirUid, out var heirBody) && heirBody.Committed)
        {
            _bodyCultivationSystem.AddTempering((heirUid, heirBody), legacy);
            return Loc.GetString("inheritance-legacy-tempering");
        }

        var heirQi = EnsureComp<CultivatorComponent>(heirUid);
        AddInsightToThreshold((heirUid, heirQi), legacy);
        var learnable = laws.FindAll(law => !_sharedCultivationSystem.KnowsLaw(heirQi, law));
        if (learnable.Count > 0 && heirQi.Laws.Count < SharedCultivationSystem.LawSlots(heirQi.Realm))
        {
            var gift = _robustRandom.Pick(learnable);
            if (_sharedCultivationSystem.TryLearnLaw((heirUid, heirQi), gift) && ProtoMan.TryIndex(gift, out var law))
                return Loc.GetString("inheritance-legacy-law", ("law", Loc.GetString(law.Name)));
        }

        return Loc.GetString("inheritance-legacy-foundation");
    }

    /// <summary>
    ///     Half the master's forged body goes to the heir as tempering, or as insight to a qi cultivator.
    /// </summary>
    private string InheritFromBody(Entity<BodyCultivatorComponent> master, EntityUid heirUid)
    {
        var legacy = SharedBodyCultivationSystem.TotalInvested(master.Comp) / 2f;
        _bodyCultivationSystem.ResetBody(master);

        if (TryComp<CultivatorComponent>(heirUid, out var heirQi))
        {
            AddInsightToThreshold((heirUid, heirQi), legacy);
            return Loc.GetString("inheritance-legacy-insight");
        }

        var heirBody = EnsureComp<BodyCultivatorComponent>(heirUid);
        if (!heirBody.Committed)
            _bodyCultivationSystem.Commit((heirUid, heirBody));

        _bodyCultivationSystem.AddTempering((heirUid, heirBody), legacy);
        return Loc.GetString("inheritance-legacy-bones");
    }

    /// <summary>
    ///     Every point of insight a qi cultivator has consolidated, across all the realms climbed so far.
    /// </summary>
    public static float TotalInsight(CultivatorComponent cultivator)
    {
        var total = cultivator.Insight;
        for (var realm = 0; realm < (int)cultivator.Realm && realm < cultivator.InsightByRealm.Count; realm++)
            total += cultivator.InsightByRealm[realm];

        return total;
    }

    private void AddInsightToThreshold(Entity<CultivatorComponent> entity, float amount)
    {
        if (_sharedCultivationSystem.GetInsightRequired(entity.Comp) is { } required)
            amount = MathF.Min(amount, MathF.Max(0f, required - entity.Comp.Insight));

        _sharedCultivationSystem.AddInsight(entity.AsNullable(), amount);
        _sharedCultivationSystem.AddQi(entity.AsNullable(), _sharedCultivationSystem.GetMaxQi(entity.Comp));
    }

    /// <summary>
    ///     The cultivation has left this body: back to the very start of the path, keeping only the Root Law.
    /// </summary>
    private void WipeQi(Entity<CultivatorComponent> entity)
    {
        var root = _sharedCultivationSystem.GetRootLaw(entity.Comp);
        foreach (var law in new List<ProtoId<Shared._KS14.Cultivation.Prototypes.CultivationLawPrototype>>(entity.Comp.Laws))
        {
            if (law != root)
                _sharedCultivationSystem.TryForgetLaw(entity.AsNullable(), law);
        }

        _sharedCultivationSystem.SetRealm(entity.AsNullable(), CultivationRealm.QiCondensation);
        _sharedCultivationSystem.ScaleInsight(entity.AsNullable(), 0f);
        _sharedCultivationSystem.TrySpendQi(entity.AsNullable(), entity.Comp.Qi);
    }

    /// <summary>
    ///     The visible handoff: a pillar of light where the master fell, and a streak of gold plunging into the heir.
    /// </summary>
    private void Streak(EntityUid masterUid, EntityUid heirUid)
    {
        _visualsSystem.Breakthrough(masterUid);
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, heirUid);
        _visualsSystem.Wave(heirUid, FxColour.Gold, big: true);
        _visualsSystem.Play(CultivationVisualsSystem.GuqinFallSound, heirUid);
        _noticeSystem.Announce(masterUid, Loc.GetString("inheritance-streak-leaves", ("user", masterUid)), type: PopupType.Large);
        _noticeSystem.Announce(heirUid, Loc.GetString("inheritance-streak-arrives", ("user", heirUid)), type: PopupType.Large);
    }

    #endregion

    #region The blessing

    /// <summary>
    ///     The heir's next breakthrough cannot fail. The blessing is spent on it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnBlessedReadiness(Entity<InheritanceBlessingComponent> entity, ref CultivationBreakthroughReadinessEvent args)
    {
        args.Readiness += 100;
        RemCompDeferred<InheritanceBlessingComponent>(entity);
    }

    [SubscribeLocalEvent]
    private void OnBlessedBodyReadiness(Entity<InheritanceBlessingComponent> entity, ref BodyTribulationReadinessEvent args)
    {
        args.Readiness += 100;
        RemCompDeferred<InheritanceBlessingComponent>(entity);
    }

    #endregion
}
