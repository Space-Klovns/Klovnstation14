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

    [Dependency] private ChatSystem _chatSystem = default!;
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

    private void GivePathChoice(EntityUid uid)
    {
        if (HasComp<CultivatorComponent>(uid) || TryComp<BodyCultivatorComponent>(uid, out var body) && body.Committed)
            return;

        var scroll = Spawn(PathChoicePrototype, Transform(uid).Coordinates);
        _handsSystem.PickupOrDrop(uid, scroll, checkActionBlocker: false);
        _noticeSystem.Notify(uid, Loc.GetString("ascension-path-given"), PopupType.Large, alsoToChat: true);
    }

    #region The path choice

    private void OnPathChosen(Entity<PathChoiceComponent> entity, ref PathChoiceMessage args)
    {
        Choose(args.Actor, entity, args.Option);
    }

    /// <summary>
    ///     Take a road: the scroll becomes that path's book (a qi law of the chosen element, or the Body Molding Art),
    ///         ready in hand to be read. Reading it is what sets them on the path, as in any wuxia round.
    /// </summary>
    public EntityUid? Choose(EntityUid user, EntityUid scroll, CultivationPathOption option)
    {
        if (TerminatingOrDeleted(scroll) || HasComp<CultivatorComponent>(user)
            || TryComp<BodyCultivatorComponent>(user, out var existing) && existing.Committed)
            return null;

        var body = option == CultivationPathOption.Body;
        var book = Spawn(body ? CultivationLootSystem.BodyMoldingArt : CultivationLootSystem.QiManuals[(int) option],
            Transform(user).Coordinates);
        QueueDel(scroll);
        _handsSystem.PickupOrDrop(user, book, checkActionBlocker: false);
        _visualsSystem.Wave(user, body ? FxColour.Earth : FxColour.Qi, big: true);
        _noticeSystem.Notify(user, Loc.GetString(body ? "ascension-chose-body" : "ascension-chose-qi", ("book", book)),
            PopupType.Medium, alsoToChat: true);
        return book;
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
