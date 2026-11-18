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

    [Dependency] private Content.Shared.Mind.SharedMindSystem _mindSystem = default!;
    [Dependency] private Content.Shared.Roles.SharedRoleSystem _roleSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;

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
        if (TerminatingOrDeleted(scroll) || HasComp<CultivatorComponent>(user)
            || TryComp<BodyCultivatorComponent>(user, out var existing) && existing.Committed)
            return false;

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

        // In Ascension, everyone racing for the peak is an antagonist to everyone else.
        if (Active() && _mindSystem.TryGetMind(user, out var mindId, out _) && !_roleSystem.MindHasRole<AscensionContenderRoleComponent>(mindId))
            _roleSystem.MindAddRole(mindId, body ? BodyContenderRole : QiContenderRole);

        QueueDel(scroll);
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

    #region The peak

    private void OnPeakReached(ref CultivationPeakReachedEvent args)
    {
        var rules = QueryActiveRules();
        if (!rules.MoveNext(out var ruleUid, out _, out var rule, out _) || rule.Winner != null)
            return;

        var winner = args.Cultivator;
        if (!_mobStateSystem.IsAlive(winner))
            return;

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
