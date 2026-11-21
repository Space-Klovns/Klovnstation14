using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Administration.Managers;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Admin shortcuts for cultivation, in three right-click submenus: Qi (awaken, break through, fill qi and insight,
///         heal injuries, teach laws, the Demonic Path), Body (start or commit to the path, advance a stage, fill
///         tempering, rest) and Martial Arts (teach any art). Each step goes through the same code as play, so
///         techniques, arts and powers are granted as they would be.
/// </summary>
public sealed partial class CultivationAdminSystem : EntitySystem
{
    private static readonly VerbCategory QiCategory = new("verb-categories-cultivation-qi", null);
    private static readonly VerbCategory BodyCategory = new("verb-categories-cultivation-body", null);
    private static readonly VerbCategory MartialCategory = new("verb-categories-cultivation-martial", null);

    /// <summary>
    ///     Every cultivation admin verb sits in this one tab, shown only on cultivators.
    /// </summary>
    private static readonly VerbCategory CultivationTab = new("verb-categories-cultivation", null);

    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private BodyCultivationSystem _bodyCultivationSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private ForbiddenArtsSystem _forbiddenArtsSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedMartialArtsSystem _martialArtsSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    [SubscribeLocalEvent]
    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actorComponent)
            || !_adminManager.HasAdminFlag(actorComponent.PlayerSession, AdminFlags.Fun)
            || !HasComp<MobStateComponent>(args.Target)
            || !_cultivatorQuery.HasComp(args.Target) && !_bodyCultivatorQuery.HasComp(args.Target))
            return;

        var targetUid = args.Target;
        AddQiVerbs(targetUid, args.Verbs);
        AddBodyVerbs(targetUid, args.Verbs);
        AddMartialVerbs(targetUid, args.Verbs);
    }

    private Verb MakeVerb(VerbCategory category, string text, Action act, int priority = 0)
    {
        // Everything sits in its own Cultivation tab, labelled by which part of cultivation it touches.
        var label = category == MartialCategory ? text : $"{Loc.GetString(category.Text)}: {text}";
        return new Verb
        {
            Text = label,
            Category = CultivationTab,
            Act = act,
            Priority = priority,
            Impact = LogImpact.Medium,
            Message = text,
        };
    }

    #region Qi

    private void AddQiVerbs(EntityUid targetUid, SortedSet<Verb> verbs)
    {
        if (!_cultivatorQuery.TryComp(targetUid, out var cultivatorComponent))
        {
            if (!_bodyCultivatorQuery.TryComp(targetUid, out var bodyComponent) || !bodyComponent.Committed)
                verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-awaken"), () => Awaken(targetUid), priority: 10));

            return;
        }

        if (cultivatorComponent.Realm < CultivationRealm.NascentSoul)
        {
            verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-break-through"), () => BreakThrough(targetUid), priority: 10));
            verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-max-realm"), () => MaxRealm(targetUid), priority: 9));
        }

        verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-fill-insight"), () => FillInsight(targetUid), priority: 8));
        verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-fill-qi"), () => FillQi(targetUid), priority: 7));
        if (cultivatorComponent.InternalInjury > 0)
            verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-heal-injury"), () => HealInjuries(targetUid), priority: 6));

        if (cultivatorComponent.Laws.Count < SharedCultivationSystem.LawSlots(cultivatorComponent.Realm))
        {
            foreach (var law in ProtoMan.EnumeratePrototypes<CultivationLawPrototype>())
            {
                if (_sharedCultivationSystem.KnowsLaw(cultivatorComponent, law.ID))
                    continue;

                var lawId = law.ID;
                verbs.Add(MakeVerb(MartialCategory, Loc.GetString("cultivation-admin-teach-law", ("law", Loc.GetString(law.Name))),
                    () => TeachLaw(targetUid, lawId)));
            }
        }

        if (!TryComp<DemonicPathComponent>(targetUid, out var demonicComponent) || !demonicComponent.Master)
            verbs.Add(MakeVerb(QiCategory, Loc.GetString("cultivation-admin-demonic"), () => _forbiddenArtsSystem.MakeDemonic(targetUid, master: true), priority: -10));
    }

    public void Awaken(EntityUid uid)
    {
        if (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent) && bodyComponent.Committed)
            return;

        EnsureComp<CultivatorComponent>(uid);
        _noticeSystem.Notify(uid, Loc.GetString("cultivation-awakened"), type: Content.Shared.Popups.PopupType.Large);
    }

    /// <summary>
    ///     The next realm, exactly as a successful breakthrough gives it, with no tribulation.
    /// </summary>
    public void BreakThrough(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            _cultivationSystem.BreakThrough((uid, cultivatorComponent));
    }

    public void MaxRealm(EntityUid uid)
    {
        while (_cultivatorQuery.TryComp(uid, out var cultivatorComponent) && cultivatorComponent.Realm < CultivationRealm.NascentSoul)
            _cultivationSystem.BreakThrough((uid, cultivatorComponent));
    }

    /// <summary>
    ///     Enough insight to attempt the next breakthrough (or ascension) the normal way.
    /// </summary>
    public void FillInsight(EntityUid uid)
    {
        if (!_cultivatorQuery.TryComp(uid, out var cultivatorComponent)
            || _sharedCultivationSystem.GetInsightRequired(cultivatorComponent) is not { } required)
            return;

        _sharedCultivationSystem.AddInsight((uid, cultivatorComponent), Math.Max(0f, required - cultivatorComponent.Insight));
    }

    public void FillQi(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            _sharedCultivationSystem.AddQi((uid, cultivatorComponent), _sharedCultivationSystem.GetMaxQi(cultivatorComponent));
    }

    public void HealInjuries(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            _sharedCultivationSystem.HealInternalInjury((uid, cultivatorComponent), stacks: cultivatorComponent.InternalInjury);
    }

    public void TeachLaw(EntityUid uid, ProtoId<CultivationLawPrototype> lawId)
    {
        if (!_cultivatorQuery.TryComp(uid, out var cultivatorComponent)
            || !_sharedCultivationSystem.TryLearnLaw((uid, cultivatorComponent), lawId))
            return;

        _cultivationSystem.GrantTechniques((uid, cultivatorComponent));
    }

    #endregion

    #region Body

    private void AddBodyVerbs(EntityUid targetUid, SortedSet<Verb> verbs)
    {
        if (_cultivatorQuery.HasComp(targetUid))
            return;

        if (!_bodyCultivatorQuery.TryComp(targetUid, out var bodyComponent))
        {
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-start"), () => StartBodyPath(targetUid, commit: false), priority: 10));
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-commit"), () => StartBodyPath(targetUid, commit: true), priority: 9));
            return;
        }

        if (!bodyComponent.Committed)
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-commit"), () => StartBodyPath(targetUid, commit: true), priority: 9));

        if (bodyComponent.Stage < SharedBodyCultivationSystem.StageCap(bodyComponent))
        {
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-advance"), () => AdvanceBodyStage(targetUid), priority: 8));
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-max"), () => MaxBodyStage(targetUid), priority: 7));
        }

        verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-tempering"), () => FillTempering(targetUid), priority: 6));
        if (bodyComponent.Exhaustion > 0f)
            verbs.Add(MakeVerb(BodyCategory, Loc.GetString("cultivation-admin-body-rest"), () => Rest(targetUid), priority: 5));
    }

    /// <summary>
    ///     As if they had read the Copper Skin Primer, or the Body Molding Art that commits them for good.
    /// </summary>
    public void StartBodyPath(EntityUid uid, bool commit)
    {
        if (_cultivatorQuery.HasComp(uid))
            return;

        var bodyComponent = EnsureComp<BodyCultivatorComponent>(uid);
        if (commit && !bodyComponent.Committed)
            _bodySystem.Commit((uid, bodyComponent));
    }

    /// <summary>
    ///     Forge every part as far as the stage allows, then take the next stage as a successful Tribulation does.
    /// </summary>
    public void AdvanceBodyStage(EntityUid uid)
    {
        if (!_bodyCultivatorQuery.TryComp(uid, out var bodyComponent) || bodyComponent.Stage >= SharedBodyCultivationSystem.StageCap(bodyComponent))
            return;

        var partLevel = SharedBodyCultivationSystem.PartCap(bodyComponent);
        foreach (var group in SharedBodyCultivationSystem.AllGroups)
        {
            if (SharedBodyCultivationSystem.GetLevel(bodyComponent, group) < partLevel)
                _bodySystem.SetLevel((uid, bodyComponent), group, partLevel);
        }

        _bodyCultivationSystem.SucceedTribulation((uid, bodyComponent));
    }

    public void MaxBodyStage(EntityUid uid)
    {
        while (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent) && bodyComponent.Stage < SharedBodyCultivationSystem.StageCap(bodyComponent))
            AdvanceBodyStage(uid);
    }

    public void FillTempering(EntityUid uid)
    {
        if (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent))
            _bodySystem.AddTempering((uid, bodyComponent), bodyComponent.TemperingCap);
    }

    public void Rest(EntityUid uid)
    {
        if (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent))
            _bodySystem.AddExhaustion((uid, bodyComponent), -bodyComponent.Exhaustion);
    }

    #endregion

    #region Martial arts

    private void AddMartialVerbs(EntityUid targetUid, SortedSet<Verb> verbs)
    {
        // Every art they don't know yet; those they can't take right now are greyed out with the reason.
        foreach (var art in ProtoMan.EnumeratePrototypes<MartialArtPrototype>())
        {
            var result = _martialArtsSystem.CanLearn(targetUid, art);
            if (result == MartialArtLearnResult.AlreadyKnown)
                continue;

            var artId = art.ID;
            var kind = Loc.GetString($"cultivation-admin-art-kind-{art.Kind}");
            var verb = MakeVerb(MartialCategory, Loc.GetString("cultivation-admin-teach-art", ("art", Loc.GetString(art.Name)), ("kind", kind)),
                () => _martialArtsSystem.TryLearn(targetUid, artId));
            if (result != MartialArtLearnResult.Learned)
            {
                verb.Disabled = true;
                verb.Message = Loc.GetString($"martial-art-learn-{result}");
            }

            verbs.Add(verb);
        }
    }

    #endregion
}
