using Content.Shared.Station.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Utility;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Popups over an entity, shown one at a time. Popups raised over the same entity in the same tick are drawn on
///         top of each other, so cultivation's messages (a part forged and a power unlocked, a technique's public
///         flourish and the user's own note) all queue here per entity.
/// </summary>
public sealed partial class CultivationNoticeSystem : EntitySystem
{
    /// <summary>
    ///     How long each message gets before the next one shows.
    /// </summary>
    private static readonly TimeSpan NoticeSpacing = TimeSpan.FromSeconds(1.2);

    private static readonly Color NoticeColor = Color.FromHex("#9fd4b8");

    [Dependency] private Content.Server.Pinpointer.NavMapSystem _navMapSystem = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;

    private static readonly SoundSpecifier DefaultProclamationSound = new SoundPathSpecifier("/Audio/Announcements/announce.ogg");

    private readonly Dictionary<EntityUid, Queue<(string Message, PopupType Type, bool Public)>> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextNotice = new();
    private readonly List<EntityUid> _finished = new();

    /// <summary>
    ///     Show a message to this entity alone, after any already waiting.
    /// </summary>
    /// <param name="alsoToChat">Also put it in their chat, for details worth reading again.</param>
    public void Notify(EntityUid uid, string message, PopupType type = PopupType.Small, bool alsoToChat = false)
    {
        if (alsoToChat && TryComp<ActorComponent>(uid, out var actorComponent))
        {
            _chatManager.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, hideChat: false,
                actorComponent.PlayerSession.Channel, colorOverride: NoticeColor);
        }

        Enqueue(uid, message, type, isPublic: false);
    }

    /// <summary>
    ///     Show a message over this entity to everyone who can see it, after any already waiting there.
    /// </summary>
    public void Announce(EntityUid uid, string message, PopupType type = PopupType.Small)
    {
        Enqueue(uid, message, type, isPublic: true);
    }

    /// <summary>
    ///     A proclamation in everyone's chat: the sender's line in large bold, then the message, without the word
    ///         "Announcement" a station announcement adds. With a source, only those on its station hear it.
    /// </summary>
    public void Proclaim(EntityUid? source, string message, string? sender = null, bool playSound = true,
        SoundSpecifier? announcementSound = null, Color? colorOverride = null, bool playDefaultSound = true)
    {
        var filter = Filter.Broadcast();
        if (source is { } sourceUid)
        {
            if (_stationSystem.GetOwningStation(sourceUid) is not { } station
                || !TryComp<StationDataComponent>(station, out var stationData))
                return;

            filter = _stationSystem.GetInStation(stationData);
        }

        var wrapped = Loc.GetString("cultivation-proclamation-wrap", ("sender", sender ?? Loc.GetString("mandate-omen-sender")),
            ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrapped, source ?? EntityUid.Invalid, false, true, colorOverride);
        if (playSound && playDefaultSound)
            _audioSystem.PlayGlobal(announcementSound ?? DefaultProclamationSound, filter, true, AudioParams.Default.WithVolume(-2f));
    }

    /// <summary>
    ///     How many messages are still waiting their turn over this entity.
    /// </summary>
    public int PendingCount(EntityUid uid)
    {
        return _pending.TryGetValue(uid, out var queue) ? queue.Count : 0;
    }

    private void Enqueue(EntityUid uid, string message, PopupType type, bool isPublic)
    {
        var now = _gameTiming.CurTime;
        if (!_pending.ContainsKey(uid) && _nextNotice.GetValueOrDefault(uid) <= now)
        {
            Show(uid, message, type, isPublic);
            _nextNotice[uid] = now + NoticeSpacing;
            return;
        }

        if (!_pending.TryGetValue(uid, out var queue))
        {
            queue = new Queue<(string, PopupType, bool)>();
            _pending[uid] = queue;
        }

        queue.Enqueue((message, type, isPublic));
    }

    private void Show(EntityUid uid, string message, PopupType type, bool isPublic)
    {
        if (isPublic)
            _popupSystem.PopupEntity(message, uid, type);
        else
            _popupSystem.PopupEntity(message, uid, uid, type);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        _finished.Clear();
        foreach (var (uid, queue) in _pending)
        {
            if (TerminatingOrDeleted(uid))
            {
                _finished.Add(uid);
                continue;
            }

            if (_nextNotice.GetValueOrDefault(uid) > now)
                continue;

            var (message, type, isPublic) = queue.Dequeue();
            Show(uid, message, type, isPublic);
            _nextNotice[uid] = now + NoticeSpacing;
            if (queue.Count == 0)
                _finished.Add(uid);
        }

        foreach (var uid in _finished)
            _pending.Remove(uid);

        // Forget anyone whose spacing has long run out.
        if (_nextNotice.Count > 256)
        {
            _finished.Clear();
            foreach (var (uid, nextTime) in _nextNotice)
            {
                if (nextTime < now && !_pending.ContainsKey(uid))
                    _finished.Add(uid);
            }

            foreach (var uid in _finished)
                _nextNotice.Remove(uid);
        }
    }

    /// <summary>
    ///     Roughly where something is, by the nearest station beacon, as plain text a popup can show (the beacon text
    ///         comes coloured with markup, which popups would print as it is).
    /// </summary>
    public string Where(EntityUid uid)
    {
        return Robust.Shared.Utility.FormattedMessage.RemoveMarkupPermissive(_navMapSystem.GetNearestBeaconString(uid));
    }
}
