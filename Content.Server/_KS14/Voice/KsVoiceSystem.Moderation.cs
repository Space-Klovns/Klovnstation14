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
    ///     Whether the player is muted at all, by an admin or automatically.
    /// </summary>
    public bool IsMuted(NetUserId userId)
    {
        return _mutes.ContainsKey(userId) ||
               _talkers.TryGetValue(userId, out var state) && state.AutoMuteUntil > _gameTiming.CurTime;
    }

    /// <summary>
    ///     Fills <paramref name="autoMutes"/> with every active automatic mute and the time it has left. Kept apart from
    ///         <see cref="Mutes"/>, which are admin mutes, because they run on game time and are tracked per talker.
    /// </summary>
    public void GetAutoMutes(List<(NetUserId UserId, TimeSpan Remaining)> autoMutes)
    {
        autoMutes.Clear();
        var now = _gameTiming.CurTime;
        foreach (var (userId, state) in _talkers)
        {
            if (state.AutoMuteUntil > now)
                autoMutes.Add((userId, state.AutoMuteUntil - now));
        }
    }

    /// <summary>
    ///     Voice-mutes a player. A null <paramref name="duration"/> mutes them for the rest of the round.
    /// </summary>
    public void Mute(NetUserId userId, TimeSpan? duration, string reason, ICommonSession? adminSession)
    {
        var until = duration == null ? (TimeSpan?)null : _gameTiming.RealTime + duration.Value;
        _mutes[userId] = new KsVoiceMute(until, reason, adminSession?.Name ?? Loc.GetString("ks-voice-mute-by-server"));

        var targetName = _playerManager.TryGetSessionById(userId, out var targetSession) ? targetSession.Name : userId.ToString();
        var length = duration == null
            ? Loc.GetString("ks-voice-mute-length-round")
            : Loc.GetString("ks-voice-mute-length-minutes", ("minutes", (int)Math.Ceiling(duration.Value.TotalMinutes)));

        _adminLogManager.Add(LogType.KsVoice, LogImpact.Medium,
            $"{adminSession?.Name ?? "Server"} voice-muted {targetName} ({length}): {reason}");

        if (targetSession?.AttachedEntity is { } attachedUid)
            _popupSystem.PopupEntity(Loc.GetString("ks-voice-popup-muted"), attachedUid, targetSession, type: PopupType.MediumCaution);

        RefreshPageState(userId);
    }

    /// <summary>
    ///     Lifts a voice mute, whether an admin's or an automatic one. Returns false if the player wasn't muted.
    /// </summary>
    public bool Unmute(NetUserId userId, ICommonSession? adminSession)
    {
        var removed = _mutes.Remove(userId);

        if (_talkers.TryGetValue(userId, out var state) && state.AutoMuteUntil > _gameTiming.CurTime)
        {
            state.AutoMuteUntil = TimeSpan.Zero;
            removed = true;
        }

        if (!removed)
            return false;

        var targetName = _playerManager.TryGetSessionById(userId, out var targetSession) ? targetSession.Name : userId.ToString();
        _adminLogManager.Add(LogType.KsVoice, LogImpact.Medium, $"{adminSession?.Name ?? "Server"} voice-unmuted {targetName}");

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

        if (session.AttachedEntity is { } attachedUid)
            _popupSystem.PopupEntity(Loc.GetString("ks-voice-popup-auto-muted"), attachedUid, session, type: PopupType.MediumCaution);

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
            !TryComp(args.User, out ActorComponent? userActorComponent) ||
            !TryComp(args.Target, out ActorComponent? targetActorComponent) ||
            !_adminManager.HasAdminFlag(userActorComponent.PlayerSession, AdminFlags.Moderator))
        {
            return;
        }

        var adminSession = userActorComponent.PlayerSession;
        var targetUserId = targetActorComponent.PlayerSession.UserId;

        // Unmute lifts any mute; muting stays on offer until there's an admin mute, so a player the abuse detector
        //      caught can still be muted for the round.
        if (IsMuted(targetUserId))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("ks-voice-verb-unmute"),
                Category = VerbCategory.Admin,
                Impact = LogImpact.Medium,
                Act = () => Unmute(targetUserId, adminSession),
            });
        }

        if (_mutes.ContainsKey(targetUserId))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("ks-voice-verb-mute-round"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            Act = () => Mute(targetUserId, duration: null, Loc.GetString("ks-voice-mute-reason-verb"), adminSession),
        });
    }
}

/// <summary>
///     An admin voice mute. <see cref="Until"/> is in real time; null means until the end of the round.
/// </summary>
public sealed record KsVoiceMute(TimeSpan? Until, string Reason, string AdminName);
