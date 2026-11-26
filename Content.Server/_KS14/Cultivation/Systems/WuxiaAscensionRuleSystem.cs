using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.RoundEnd;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Wuxia: Ascension. Everyone aboard is handed a Fork in the Road and chooses the path of qi or of the body;
///         cultivation goes twice as fast; the first to reach the peak (Nascent Soul, or the ninth stage of the body) is
///         proclaimed the Supreme Sovereign, takes a quarter less damage from every blow, and has until the shuttle comes to settle
///         every score aboard.
/// </summary>
public sealed partial class WuxiaAscensionRuleSystem : GameRuleSystem<WuxiaAscensionRuleComponent>
{
    private static readonly EntProtoId PathChoicePrototype = "KsPathChoice";

    private static readonly EntProtoId QiContenderRole = "MindRoleKsAscensionQi";
    private static readonly EntProtoId BodyContenderRole = "MindRoleKsAscensionBody";
    private static readonly EntProtoId CourtEnforcerRole = "MindRoleKsAscensionCourt";

    [Dependency] private Content.Shared.Mind.SharedMindSystem _mindSystem = default!;
    [Dependency] private Content.Shared.Roles.SharedRoleSystem _roleSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private Content.Shared.Roles.Jobs.SharedJobSystem _jobSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;
    [Dependency] private Content.Server.Pinpointer.NavMapSystem _navMapSystem = default!;
    [Dependency] private Content.Server.Station.Systems.StationSystem _stationSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _robustRandom = default!;

    /// <summary>
    ///     How close to their site a contender has to be to take the last step.
    /// </summary>
    public const float FinalStepSiteRange = 2.5f;

    /// <summary>
    ///     Each cycle of a contender's last step to the peak takes this many times as long: slower, but no more lightning.
    /// </summary>
    public const double FinalStepSlowdown = 2.0;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<CultivationPeakReachedEvent>(OnPeakReached);
        Subs.BuiEvents<PathChoiceComponent>(PathChoiceUiKey.Key, subs => subs.Event<PathChoiceMessage>(OnPathChosen));
        SubscribeLocalEvent<SupremeSovereignComponent, DamageModifyEvent>(OnSovereignDamage);
    }

    public bool Active()
    {
        return GameTicker.IsGameRuleActive<WuxiaAscensionRuleComponent>();
    }

    /// <summary>
    ///     Extra cultivation speed while an Ascension round runs.
    /// </summary>
    public float SpeedBonus()
    {
        var rules = QueryActiveRules();
        return rules.MoveNext(out _, out _, out var rule, out _) ? rule.SpeedBonus : 0f;
    }

    protected override void Started(EntityUid uid, WuxiaAscensionRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Anyone already aboard gets their choice too.
        var bodies = EntityQueryEnumerator<HumanoidProfileComponent, ActorComponent>();
        while (bodies.MoveNext(out var bodyUid, out _, out _))
            GivePathChoice(bodyUid);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (Active())
            GivePathChoice(args.Mob);
    }

    public void GivePathChoice(EntityUid uid)
    {
        if (HasComp<CultivatorComponent>(uid) || TryComp<BodyCultivatorComponent>(uid, out var body) && body.Committed)
            return;

        var scroll = Spawn(PathChoicePrototype, Transform(uid).Coordinates);
        _handsSystem.PickupOrDrop(uid, scroll, checkActionBlocker: false);
        _noticeSystem.Notify(uid, Loc.GetString(Active() ? "ascension-path-given" : "cultivator-path-given"), PopupType.Large, alsoToChat: true);
    }

    #region The path choice

    private void OnPathChosen(Entity<PathChoiceComponent> entity, ref PathChoiceMessage args)
    {
        Choose(args.Actor, entity, args.Option);
    }

    private static readonly ProtoId<CultivationLawPrototype>[] ElementLaws = { "KsMetal", "KsWood", "KsWater", "KsFire", "KsEarth" };

    /// <summary>
    ///     Take a road, at once: the path of qi awakens them (their element comes from a manual they find); the path of the body
    ///         commits them to the Body Molding Art, ready to forge. No book is needed. In Ascension they also become
    ///         a contender for the peak.
    /// </summary>
    public bool Choose(EntityUid user, EntityUid scroll, CultivationPathOption option)
    {
        if (TerminatingOrDeleted(scroll) || !ApplyPath(user, option))
            return false;

        QueueDel(scroll);
        return true;
    }

    /// <summary>
    ///     Set someone on a path directly, as the Fork in the Road would (admins use this). Any Fork they still hold goes.
    /// </summary>
    public bool ApplyPath(EntityUid user, CultivationPathOption option)
    {
        if (HasComp<CultivatorComponent>(user)
            || TryComp<BodyCultivatorComponent>(user, out var existing) && existing.Committed)
            return false;

        foreach (var held in _handsSystem.EnumerateHeld(user))
        {
            if (HasComp<PathChoiceComponent>(held))
                QueueDel(held);
        }

        var body = option == CultivationPathOption.Body;
        if (body)
        {
            var bodyComponent = EnsureComp<BodyCultivatorComponent>(user);
            _bodySystem.Commit((user, bodyComponent));
            _noticeSystem.Notify(user, Loc.GetString("body-manual-committed"), PopupType.Large, alsoToChat: true);
        }
        else
        {
            // Qi awakens; the element is theirs to find, in whichever manual turns up. (An element picked explicitly,
            //     as an admin might, is still taught.)
            EnsureComp<CultivatorComponent>(user);
            _noticeSystem.Notify(user, Loc.GetString("ascension-chose-qi"), PopupType.Large, alsoToChat: true);
            if (option != CultivationPathOption.Qi)
                _cultivationSystem.TryLearnLaw(user, ElementLaws[(int) option]);
        }

        // In Ascension, everyone racing for the peak is an antagonist to everyone else, except the court: heads of
        //     staff and their sworn guards enforce heaven's order and can't claim the peak themselves.
        if (Active() && _mindSystem.TryGetMind(user, out var mindId, out _) && !_roleSystem.MindHasRole<AscensionContenderRoleComponent>(mindId))
            _roleSystem.MindAddRole(mindId, CourtBound(user) ? CourtEnforcerRole : body ? BodyContenderRole : QiContenderRole);

        _visualsSystem.Wave(user, body ? FxColour.Earth : FxColour.Qi, big: true);
        return true;
    }

    /// <summary>
    ///     A cultivator antagonist chooses their own path with a Fork in the Road, rather than being handed a manual.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCultivatorAntagSelected(Entity<Content.Server._KS14.Cultivation.CultivatorRuleComponent> entity, ref Content.Server.Antag.AfterAntagEntitySelectedEvent args)
    {
        GivePathChoice(args.EntityUid);
    }

    #endregion

    /// <summary>
    ///     Keep someone's Ascension role in step with whether they're bound to the court: joining the Jinyiwei turns a
    ///         contender into a Court Enforcer, and leaving it turns them back.
    /// </summary>
    public void RefreshRole(EntityUid uid)
    {
        if (!Active() || !_mindSystem.TryGetMind(uid, out var mindId, out _)
            || !_roleSystem.MindHasRole<AscensionContenderRoleComponent>(mindId))
            return;

        var body = TryComp<BodyCultivatorComponent>(uid, out var bodyComponent) && bodyComponent.Committed;
        _roleSystem.MindRemoveRole<AscensionContenderRoleComponent>(mindId);
        _roleSystem.MindAddRole(mindId, CourtBound(uid) ? CourtEnforcerRole : body ? BodyContenderRole : QiContenderRole);
    }

    /// <summary>
    ///     Heads of staff by job (a stolen ID doesn't count) and the Jinyiwei sworn to a ruler: they can't win Ascension
    ///         themselves; their duty is to stop anyone else from reaching the peak.
    /// </summary>
    public bool CourtBound(EntityUid uid)
    {
        if (HasComp<ImperialGuardComponent>(uid))
            return true;

        return _mindSystem.TryGetMind(uid, out var mindId, out _)
               && _jobSystem.MindTryGetJobId(mindId, out var jobId) && jobId is { } job && MandateSystem.IsHeadJob(job.Id);
    }

    #region The last step

    /// <summary>
    ///     Whether this is an Ascension contender (not bound to the court) about to take their last step to the peak.
    /// </summary>
    public bool IsContenderFinalStep(EntityUid uid, bool lastStep)
    {
        if (!lastStep || !Active())
            return false;

        if (!CourtBound(uid))
            return true;

        // Those bound to the court can't win the round, so heaven asks no pilgrimage of them; say so, so it isn't a mystery.
        _noticeSystem.Notify(uid, Loc.GetString("ascension-court-no-site"), PopupType.Medium);
        return false;
    }

    /// <summary>
    ///     A contender may only take the last step at the place heaven chose for them. The first time they try, choose it
    ///         and tell them; after that, tell them how far off they are.
    /// </summary>
    /// <returns>Whether they're where they need to be (or no place could be found, in which case anywhere will do).</returns>
    public bool CheckFinalSite(EntityUid uid)
    {
        var site = EnsureComp<AscensionSiteComponent>(uid);
        if (site.Beacon is not { } beacon || TerminatingOrDeleted(beacon))
        {
            if (!PickSite(uid, site))
                return true;

            _noticeSystem.Notify(uid, Loc.GetString("ascension-site-chosen", ("place", site.Name)), PopupType.Large, alsoToChat: true);
            PointTo(uid, site);
            return false;
        }

        var here = _transformSystem.GetMapCoordinates(uid);
        var there = _transformSystem.GetMapCoordinates(beacon);
        if (here.MapId == there.MapId && (here.Position - there.Position).Length() <= FinalStepSiteRange)
            return true;

        _noticeSystem.Notify(uid, Loc.GetString("ascension-site-far", ("place", site.Name)), PopupType.Medium, alsoToChat: true);
        PointTo(uid, site);
        return false;
    }

    /// <summary>
    ///     How long the arrow to the site shows; press the button again to see it again.
    /// </summary>
    private const float SiteArrowSeconds = 6f;

    /// <summary>
    ///     Show the contender an arrow to their site for a few seconds.
    /// </summary>
    private void PointTo(EntityUid uid, AscensionSiteComponent site)
    {
        if (site.Beacon is not { } beacon || !TryComp<ActorComponent>(uid, out var actor))
            return;

        RaiseNetworkEvent(new KsPointArrowEvent(_transformSystem.GetMapCoordinates(beacon), SiteArrowSeconds, site.Name),
            actor.PlayerSession);
    }

    /// <summary>
    ///     Warn the whole station: someone is trying to ascend, and where.
    /// </summary>
    public void AnnounceFinalStep(EntityUid uid)
    {
        var place = TryComp<AscensionSiteComponent>(uid, out var site) ? site.Name : _navMapSystem.GetNearestBeaconString(uid, onlyName: true);
        _noticeSystem.Proclaim(null, Loc.GetString("ascension-final-step-warning", ("user", uid), ("place", place)),
            Loc.GetString("cultivation-heavenly-omen"), colorOverride: Color.Gold);
    }

    private bool PickSite(EntityUid uid, AscensionSiteComponent site)
    {
        // Any beacon on the contender's own map. Not just the station's own grids: on Saltern the station is a grid
        //     of its own on a planet map, and its beacons don't count as the station's.
        var mapUid = Transform(uid).MapUid;
        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<Content.Shared.Pinpointer.NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var beaconUid, out var beacon, out var xform))
        {
            if (!beacon.Enabled || !xform.Anchored || xform.GridUid == null || xform.MapUid != mapUid)
                continue;

            candidates.Add(beaconUid);
        }

        if (candidates.Count == 0)
            return false;

        var chosen = _robustRandom.Pick(candidates);
        site.Beacon = chosen;
        site.Name = _navMapSystem.GetNearestBeaconString(chosen, onlyName: true);
        return true;
    }

    #endregion

    #region The peak

    private void OnPeakReached(ref CultivationPeakReachedEvent args)
    {
        var rules = QueryActiveRules();
        if (!rules.MoveNext(out var ruleUid, out _, out var rule, out _) || rule.Winner != null)
            return;

        var winner = args.Cultivator;
        if (!_mobStateSystem.IsAlive(winner))
            return;

        // The court serves heaven's order; it doesn't seize it.
        if (CourtBound(winner))
        {
            _noticeSystem.Notify(winner, Loc.GetString("ascension-peak-court"), PopupType.Large, alsoToChat: true);
            return;
        }

        rule.Winner = winner;
        rule.WinnerName = Name(winner);
        EnsureComp<SupremeSovereignComponent>(winner);
        _visualsSystem.Breakthrough(winner);
        _noticeSystem.Proclaim(winner, Loc.GetString("ascension-peak-announcement", ("winner", Name(winner))),
            Loc.GetString("mandate-omen-sender"), announcementSound: CultivationVisualsSystem.ThunderclapSound, colorOverride: Color.Gold);
        _noticeSystem.Notify(winner, Loc.GetString("ascension-peak-self"), PopupType.Large, alsoToChat: true);
        // The shuttle's own announcement, with its countdown, follows Heaven's.
        _roundEndSystem.RequestRoundEnd(rule.RoundEndDelay, checkCooldown: false, cantRecall: true);
    }

    private void OnSovereignDamage(Entity<SupremeSovereignComponent> entity, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() > 0)
            args.Damage *= entity.Comp.DamageMultiplier;
    }

    protected override void AppendRoundEndText(EntityUid uid, WuxiaAscensionRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        args.AddLine(component.WinnerName is { } name
            ? Loc.GetString("ascension-round-end-winner", ("winner", name))
            : Loc.GetString("ascension-round-end-none"));
    }

    #endregion
}
