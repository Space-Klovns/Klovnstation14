using Content.Server.Administration.Managers;
using Content.Shared._KS14.Voice;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Admin voice mutes and automatic abuse mutes.
///
///     Mutes are keyed by <see cref="NetUserId"/>, so they follow the player across bodies and reconnects, and live in
///         memory: a timed mute runs on real time and survives round restarts, a round mute (no duration) is lifted at
///         the end of the round, and both end with the server.
/// </summary>
public sealed partial class KsVoiceSystem
{
    [Dependency] private IAdminManager _adminManager = default!;

    private readonly Dictionary<NetUserId, KsVoiceMute> _mutes = [];

    public IReadOnlyDictionary<NetUserId, KsVoiceMute> Mutes => _mutes;

    /// <summary>
    ///     Voice-mutes a player. A null <paramref name="duration"/> mutes them for the rest of the round.
    /// </summary>
    public void Mute(NetUserId userId, TimeSpan? duration, string reason, ICommonSession? admin)
    {
        var until = duration == null ? (TimeSpan?)null : _gameTiming.RealTime + duration.Value;
        _mutes[userId] = new KsVoiceMute(until, reason, admin?.Name ?? Loc.GetString("ks-voice-mute-by-server"));

        var target = _playerManager.TryGetSessionById(userId, out var session) ? session.Name : userId.ToString();
        var length = duration == null
            ? Loc.GetString("ks-voice-mute-length-round")
            : Loc.GetString("ks-voice-mute-length-minutes", ("minutes", (int)Math.Ceiling(duration.Value.TotalMinutes)));

        _adminLogManager.Add(LogType.KsVoice, LogImpact.Medium,
            $"{admin?.Name ?? "Server"} voice-muted {target} ({length}): {reason}");

        if (session?.AttachedEntity is { } uid)
            _popupSystem.PopupEntity(Loc.GetString("ks-voice-popup-muted"), uid, session, type: PopupType.MediumCaution);

        RefreshPageState(userId);
    }

    /// <summary>
    ///     Lifts an admin voice mute. Returns false if the player wasn't muted.
    /// </summary>
    public bool Unmute(NetUserId userId, ICommonSession? admin)
    {
        if (!_mutes.Remove(userId))
            return false;

        var target = _playerManager.TryGetSessionById(userId, out var session) ? session.Name : userId.ToString();
        _adminLogManager.Add(LogType.KsVoice, LogImpact.Medium, $"{admin?.Name ?? "Server"} voice-unmuted {target}");

        RefreshPageState(userId);
        return true;
    }

    /// <summary>
    ///     Auto-mutes the talker, unless auto-muting is disabled or they already are. Returns whether a new mute
    ///         was applied.
    /// </summary>
    private bool ApplyAutoMute(NetUserId userId, TalkerState state)
    {
        var now = _gameTiming.CurTime;
        if (_autoMuteSeconds <= 0f || state.AutoMuteUntil > now)
            return false;

        state.AutoMuteUntil = now + TimeSpan.FromSeconds((double)_autoMuteSeconds);
        EndBurst(userId, state);

        if (!_playerManager.TryGetSessionById(userId, out var session))
            return true;

        _adminLogManager.Add(LogType.KsVoice, LogImpact.High,
            $"{session:player} was automatically voice-muted for {_autoMuteSeconds:0}s for abusive audio levels");
        _chatManager.SendAdminAlert(Loc.GetString("ks-voice-admin-alert-auto-muted",
            ("player", session.Name),
            ("seconds", (int)_autoMuteSeconds)));

        if (session.AttachedEntity is { } uid)
            _popupSystem.PopupEntity(Loc.GetString("ks-voice-popup-auto-muted"), uid, session, type: PopupType.MediumCaution);

        return true;
    }

    private void UpdateMutes()
    {
        if (_mutes.Count == 0)
            return;

        var now = _gameTiming.RealTime;

        _scratchUsers.Clear();
        foreach (var (userId, mute) in _mutes)
        {
            if (mute.Until is { } until && until <= now)
                _scratchUsers.Add(userId);
        }

        foreach (var userId in _scratchUsers)
        {
            _mutes.Remove(userId);
            RefreshPageState(userId);
        }
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!_enabled ||
            !TryComp(args.User, out ActorComponent? userActor) ||
            !TryComp(args.Target, out ActorComponent? targetActor) ||
            !_adminManager.HasAdminFlag(userActor.PlayerSession, AdminFlags.Moderator))
        {
            return;
        }

        var admin = userActor.PlayerSession;
        var targetUserId = targetActor.PlayerSession.UserId;

        if (_mutes.ContainsKey(targetUserId))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("ks-voice-verb-unmute"),
                Category = VerbCategory.Admin,
                Impact = LogImpact.Medium,
                Act = () => Unmute(targetUserId, admin),
            });

            return;
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("ks-voice-verb-mute-round"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            Act = () => Mute(targetUserId, duration: null, Loc.GetString("ks-voice-mute-reason-verb"), admin),
        });
    }
}

/// <summary>
///     An admin voice mute. <see cref="Until"/> is in real time; null means until the end of the round.
/// </summary>
public sealed record KsVoiceMute(TimeSpan? Until, string Reason, string AdminName);
