using Content.Server._KS14.Cultivation.Components;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Bible.Components;
using Content.Server.Chat.Managers;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The ancestral ring: a heavy, tarnished ring with an ancient master asleep in its stone. A cultivator can wake
///         them (a ghost takes the role). The master speaks only into the bearer's head, can lend them qi, and while
///         awake makes their meditation more fruitful. A holy book pressed to the stone banishes them.
/// </summary>
public sealed partial class AncestralRingSystem : EntitySystem
{
    private static readonly Color ElderColor = Color.FromHex("#c8b27a");

    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private QuickDialogSystem _quickDialogSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    /// <summary>
    ///     Whoever carries the ring, anywhere on their person.
    /// </summary>
    public EntityUid? Bearer(EntityUid ringUid)
    {
        var current = Transform(ringUid).ParentUid;
        while (current.IsValid())
        {
            if (HasComp<MobStateComponent>(current))
                return current;

            current = Transform(current).ParentUid;
        }

        return null;
    }

    /// <summary>
    ///     A player is living in the stone.
    /// </summary>
    public bool ElderAwake(EntityUid ringUid)
    {
        return HasComp<ActorComponent>(ringUid);
    }

    /// <summary>
    ///     How much faster this cultivator learns: a waking master in a ring they carry scolds them along.
    /// </summary>
    public float InsightMultiplier(EntityUid cultivatorUid)
    {
        var query = EntityQueryEnumerator<AncestralRingComponent>();
        while (query.MoveNext(out var ringUid, out var ringComponent))
        {
            if (ElderAwake(ringUid) && Bearer(ringUid) == cultivatorUid)
                return ringComponent.InsightMultiplier;
        }

        return 1f;
    }

    [SubscribeLocalEvent]
    private void OnRingExamined(Entity<AncestralRingComponent> entity, ref ExaminedEvent args)
    {
        if (ElderAwake(entity))
            args.PushMarkup(Loc.GetString("ancestral-ring-awake"));
        else if (HasComp<CultivatorComponent>(args.Examiner))
            args.PushMarkup(Loc.GetString("ancestral-ring-dormant"));
    }

    /// <summary>
    ///     A cultivator channels a thread of qi into the stone, and the master inside may wake.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRingUsed(Entity<AncestralRingComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!HasComp<CultivatorComponent>(args.User))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("ancestral-ring-mortal"));
            return;
        }

        if (entity.Comp.Awakened)
        {
            _noticeSystem.Notify(args.User, Loc.GetString(ElderAwake(entity) ? "ancestral-ring-already-awake" : "ancestral-ring-stirring"));
            return;
        }

        Awaken(entity);
        _noticeSystem.Notify(args.User, Loc.GetString("ancestral-ring-channel"), PopupType.Medium);
    }

    /// <summary>
    ///     Offer the master's role to the ghosts.
    /// </summary>
    public void Awaken(Entity<AncestralRingComponent> entity)
    {
        entity.Comp.Awakened = true;
        EnsureComp<MindContainerComponent>(entity);
        EnsureComp<GhostTakeoverAvailableComponent>(entity);
        var ghostRole = EnsureComp<GhostRoleComponent>(entity);
        ghostRole.RoleName = Loc.GetString("ancestral-ring-role-name");
        ghostRole.RoleDescription = Loc.GetString("ancestral-ring-role-description");
        ghostRole.RoleRules = Loc.GetString("ancestral-ring-role-rules");

        foreach (var action in entity.Comp.ElderActions)
            _actionsSystem.AddAction(entity, action);
    }

    [SubscribeLocalEvent]
    private void OnElderArrived(Entity<AncestralRingComponent> entity, ref MindAddedMessage args)
    {
        if (Bearer(entity) is { } bearerUid)
            _noticeSystem.Notify(bearerUid, Loc.GetString("ancestral-ring-yawn"), PopupType.Large, alsoToChat: true);

        if (TryComp<ActorComponent>(entity, out var actorComponent))
            Whisper(actorComponent.PlayerSession, Loc.GetString("ancestral-ring-welcome"));
    }

    /// <summary>
    ///     The master murmurs into the bearer's head.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnElderSpeak(Entity<AncestralRingComponent> entity, ref RingElderSpeakActionEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(entity, out var actorComponent))
            return;

        args.Handled = true;
        var ringUid = entity.Owner;
        _quickDialogSystem.OpenDialog<string>(actorComponent.PlayerSession, Loc.GetString("ancestral-ring-speak-title"),
            Loc.GetString("ancestral-ring-speak-prompt"), rawMessage =>
            {
                var message = FormattedMessage.EscapeText(rawMessage.Trim());
                if (message.Length == 0 || TerminatingOrDeleted(ringUid) || !TryComp<ActorComponent>(ringUid, out var elderActor))
                    return;

                Whisper(elderActor.PlayerSession, Loc.GetString("ancestral-ring-said", ("message", message)));
                if (Bearer(ringUid) is { } bearerUid && TryComp<ActorComponent>(bearerUid, out var bearerActor))
                    Whisper(bearerActor.PlayerSession, Loc.GetString("ancestral-ring-heard", ("message", message)));

                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(ringUid):player} (ancestral ring) said to its bearer: {rawMessage}");
            });
    }

    private void Whisper(ICommonSession session, string message)
    {
        _chatManager.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, hideChat: false, session.Channel, colorOverride: ElderColor);
    }

    [SubscribeLocalEvent]
    private void OnElderLendQi(Entity<AncestralRingComponent> entity, ref RingElderLendQiActionEvent args)
    {
        if (args.Handled)
            return;

        if (Bearer(entity) is not { } bearerUid || !TryComp<CultivatorComponent>(bearerUid, out var cultivatorComponent))
        {
            if (TryComp<ActorComponent>(entity, out var actorComponent))
                Whisper(actorComponent.PlayerSession, Loc.GetString("ancestral-ring-no-dantian"));

            return;
        }

        args.Handled = true;
        _sharedCultivationSystem.AddQi((bearerUid, cultivatorComponent), args.Qi);
        _noticeSystem.Notify(bearerUid, Loc.GetString("ancestral-ring-lent-qi"), PopupType.Medium);
    }

    /// <summary>
    ///     Holy power drives the master out of the stone.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRingInteractUsing(Entity<AncestralRingComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<BibleComponent>(args.Used) || !entity.Comp.Awakened)
            return;

        args.Handled = true;
        if (_mindSystem.TryGetMind(entity, out var mindId, out _))
            _mindSystem.TransferTo(mindId, null);

        RemComp<GhostRoleComponent>(entity);
        RemComp<GhostTakeoverAvailableComponent>(entity);
        entity.Comp.Awakened = false;
        _noticeSystem.Announce(entity, Loc.GetString("ancestral-ring-banished", ("user", args.User), ("item", args.Used), ("target", entity.Owner)),
            type: PopupType.Medium);
    }
}
