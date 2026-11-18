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
///         proclaimed the Supreme Sovereign, takes half damage from every blow, and has until the shuttle comes to settle
///         every score aboard.
/// </summary>
public sealed partial class WuxiaAscensionRuleSystem : GameRuleSystem<WuxiaAscensionRuleComponent>
{
    private static readonly EntProtoId PathChoicePrototype = "KsPathChoice";

    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private CultivationAdminSystem _adminSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<CultivationPeakReachedEvent>(OnPeakReached);
        SubscribeLocalEvent<PathChoiceComponent, GetVerbsEvent<Verb>>(OnPathVerbs);
        SubscribeLocalEvent<PathChoiceComponent, UseInHandEvent>(OnPathUsed);
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

    private void GivePathChoice(EntityUid uid)
    {
        if (HasComp<CultivatorComponent>(uid) || TryComp<BodyCultivatorComponent>(uid, out var body) && body.Committed)
            return;

        var scroll = Spawn(PathChoicePrototype, Transform(uid).Coordinates);
        _handsSystem.PickupOrDrop(uid, scroll, checkActionBlocker: false);
        _noticeSystem.Notify(uid, Loc.GetString("ascension-path-given"), PopupType.Large, alsoToChat: true);
    }

    #region The path choice

    private void OnPathUsed(Entity<PathChoiceComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _noticeSystem.Notify(args.User, Loc.GetString("ascension-path-how"), PopupType.Medium);
    }

    private void OnPathVerbs(Entity<PathChoiceComponent> entity, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<HumanoidProfileComponent>(args.User))
            return;

        var user = args.User;
        var scroll = entity.Owner;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("ascension-path-qi"),
            Act = () => Choose(user, scroll, body: false),
            Priority = 2,
        });
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("ascension-path-body"),
            Act = () => Choose(user, scroll, body: true),
            Priority = 1,
        });
    }

    /// <summary>
    ///     Walk a path: awaken as a qi cultivator, or commit the body to the Body Molding Art.
    /// </summary>
    public void Choose(EntityUid user, EntityUid scroll, bool body)
    {
        if (TerminatingOrDeleted(scroll) || HasComp<CultivatorComponent>(user)
            || TryComp<BodyCultivatorComponent>(user, out var existing) && existing.Committed)
            return;

        if (body)
            _adminSystem.StartBodyPath(user, commit: true);
        else
            _adminSystem.Awaken(user);

        _visualsSystem.Wave(user, body ? FxColour.Earth : FxColour.Qi, big: true);
        _noticeSystem.Announce(user, Loc.GetString(body ? "ascension-chose-body" : "ascension-chose-qi", ("user", user)), type: PopupType.Medium);
        QueueDel(scroll);
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
        _chatSystem.DispatchStationAnnouncement(winner, Loc.GetString("ascension-peak-announcement", ("winner", Name(winner)),
                ("minutes", (int) rule.RoundEndDelay.TotalMinutes)),
            Loc.GetString("mandate-omen-sender"), announcementSound: CultivationVisualsSystem.ThunderclapSound, colorOverride: Color.Gold);
        _noticeSystem.Notify(winner, Loc.GetString("ascension-peak-self"), PopupType.Large, alsoToChat: true);
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
