using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Content.Shared.ActionBlocker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech.Muting;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Replays;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Relays moderated microphone audio from voice pages (<see cref="KsVoiceUplinkManager"/>) to nearby players, and
///         shows who is talking.
///
///     A chunk is relayed only while voice is enabled, the talker holds their in-game push-to-talk key (or uses voice
///         activation, <see cref="KsCCVars.VoiceActivation"/>), is not muted or on cooldown, and their attached entity
///         is not a ghost and passes <see cref="ActionBlockerSystem.CanSpeak"/> (so crit, death, sleep, mime vows and
///         admin freeze-mutes all silence voice exactly as they silence speech).
///         Listeners are the other in-game players within <see cref="KsCCVars.VoiceRange"/> on the same map, excluding
///         incapacitated or sleeping ones; ghosts hear too. Relayed chunks also go into server-side replays
///         (<see cref="KsVoiceReplayFrameEvent"/>) unless <see cref="KsCCVars.VoiceRecordInReplays"/> is off.
/// </summary>
public sealed partial class KsVoiceSystem : EntitySystem
{
    /// <summary>
    ///     How long the talking indicator lingers after the last relayed chunk, so it doesn't flicker between words.
    /// </summary>
    private static readonly TimeSpan IndicatorHangover = TimeSpan.FromSeconds(0.3);

    /// <summary>
    ///     A gap longer than this ends a talking burst.
    /// </summary>
    private static readonly TimeSpan BurstGap = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Minimum time between voice link resets for one player.
    /// </summary>
    private static readonly TimeSpan LinkResetCooldown = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often connected pages' players are checked for a change of voice activation setting.
    /// </summary>
    private static readonly TimeSpan VoiceActivationPollInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private KsVoiceUplinkManager _uplinkManager = default!;
    [Dependency] private KsVoiceLinkManager _linkManager = default!;
    [Dependency] private IServerNetManager _netManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetConfigurationManager _configurationManager = default!;
    [Dependency] private IReplayRecordingManager _replayRecordingManager = default!;
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;
    [Dependency] private EntityQuery<SleepingComponent> _sleepingQuery = default!;
    [Dependency] private EntityQuery<MutedComponent> _mutedQuery = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<MetaDataComponent> _metaQuery = default!;

    private readonly Dictionary<NetUserId, TalkerState> _talkers = [];
    private readonly List<INetChannel> _recipientChannels = [];
    private readonly List<NetUserId> _scratchUsers = [];

    private bool _enabled;
    private float _range;
    private float _autoMuteSeconds;
    private float _maxContinuousSeconds;
    private float _cooldownSeconds;
    private bool _logBursts;
    private bool _voiceActivationAllowed;
    private bool _recordInReplays;
    private TimeSpan _nextVoiceActivationPoll;

    /// <summary>
    ///     Relayed chunks since startup, for tests and diagnostics.
    /// </summary>
    public int RelayedChunkCount { get; private set; }

    /// <summary>
    ///     Chunks written into replay recordings since startup, for tests and diagnostics.
    /// </summary>
    public int RecordedChunkCount { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.VoiceEnabled, OnEnabledChanged, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceRange, value => _range = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceAutoMuteSeconds, value => _autoMuteSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceMaxContinuousSeconds, value => _maxContinuousSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceCooldownSeconds, value => _cooldownSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceAdminLogBursts, value => _logBursts = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceActivationAllowed, value =>
        {
            _voiceActivationAllowed = value;
            RefreshAllPageStates();
        }, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceRecordInReplays, value => _recordInReplays = value, invokeImmediately: true);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_uplinkManager.TryDequeueConnectionChange(out var userId, out var connected))
            OnUplinkConnectionChanged(userId, connected);

        while (_uplinkManager.TryDequeueChunk(out var chunk))
        {
            if (_enabled)
                HandleChunk(chunk);
        }

        UpdateTalkers();
        UpdateMutes();
    }

    /// <summary>
    ///     Processes one inbound chunk as if it had just arrived from the user's page. Public so tests can drive the
    ///         relay without a websocket.
    /// </summary>
    public void HandleChunk(KsVoiceInboundChunk chunk)
    {
        var state = GetTalker(chunk.UserId);
        var now = _gameTiming.CurTime;

        if (!_playerManager.TryGetSessionById(chunk.UserId, out var session))
            return;

        var voiceActivation = IsVoiceActivated(session);

        // With voice activation, pages only send while their noise gate is open, so a gap in the audio is where an
        //      utterance ends. It stands in for releasing the key: the next one gets a fresh CanSpeak check (and at
        //      most one popup).
        if (voiceActivation && now - state.LastChunkReceived > BurstGap)
            state.CannotSpeakBodyUid = null;

        state.LastChunkReceived = now;

        var block = GetBlockReason(session, state, now, voiceActivation, attempt: true, out var speakerUid);

        // Only audio that would actually go out can earn a mute. The page counts only frames sent while we told it
        //      it was transmitting, but that flag trails by a chunk, so check again here.
        if (block == null && chunk.AbuseTriggered && ApplyAutoMute(chunk.UserId, state))
            block = KsVoiceBlockReason.AutoMuted;

        if (block == null && _maxContinuousSeconds > 0f)
        {
            if (state.BurstStart is { } burstStart && now - burstStart > TimeSpan.FromSeconds((double)_maxContinuousSeconds))
            {
                state.CooldownUntil = now + TimeSpan.FromSeconds((double)_cooldownSeconds);
                EndBurst(chunk.UserId, state);
                _popupSystem.PopupEntity(Loc.GetString("ks-voice-popup-cooldown"), speakerUid, session, type: PopupType.SmallCaution);
                block = KsVoiceBlockReason.Cooldown;
            }
        }

        UpdatePageState(chunk.UserId, state, block, voiceActivation, now);

        // No payload: the page wasn't transmitting when it sent this, so it wasn't encoded (KsVoiceUplinkConnection).
        if (block != null || chunk.Payload.Length == 0)
            return;

        Relay(chunk, session, speakerUid, state, now);
    }

    private void Relay(KsVoiceInboundChunk chunk, ICommonSession speakerSession, EntityUid speakerUid, TalkerState state, TimeSpan now)
    {
        if (state.BurstStart == null || now - state.LastRelay > BurstGap)
        {
            EndBurst(chunk.UserId, state);
            state.BurstStart = now;
        }

        state.LastRelay = now;
        state.TalkingUntil = now + IndicatorHangover;
        SetIndicator(state, speakerUid);

        var speakerCoordinates = _transformSystem.GetMapCoordinates(speakerUid);
        var speakerVisibilityMask = _metaQuery.Comp(speakerUid).VisibilityMask;

        _recipientChannels.Clear();
        foreach (var listenerSession in _playerManager.Sessions)
        {
            if (listenerSession == speakerSession ||
                listenerSession.Status != SessionStatus.InGame ||
                listenerSession.AttachedEntity is not { Valid: true } listenerUid ||
                !CanHear(listenerUid) ||
                !CanSee(listenerUid, speakerVisibilityMask))
            {
                continue;
            }

            var listenerCoordinates = _transformSystem.GetMapCoordinates(listenerUid);
            if (listenerCoordinates.MapId != speakerCoordinates.MapId ||
                (listenerCoordinates.Position - speakerCoordinates.Position).Length() > _range)
            {
                continue;
            }

            _recipientChannels.Add(listenerSession.Channel);
        }

        RelayedChunkCount++;

        // Already encoded by the talker's page connection, off the main thread (KsVoiceUplinkConnection).
        state.Sequence++;

        var speakerNetEntity = GetNetEntity(speakerUid);

        // Everything relayed, whoever was in range: a replay is watched from anywhere.
        if (_recordInReplays && _replayRecordingManager.IsRecording)
        {
            _replayRecordingManager.RecordServerMessage(
                new KsVoiceReplayFrameEvent(speakerNetEntity, state.Sequence, chunk.Codec, chunk.StreamStart, chunk.Payload));
            RecordedChunkCount++;
        }

        if (_recipientChannels.Count == 0)
            return;

        var message = new KsVoiceFrameMessage
        {
            Source = speakerNetEntity,
            Sequence = state.Sequence,
            Codec = chunk.Codec,
            StreamStart = chunk.StreamStart,
            Payload = chunk.Payload,
        };

        _netManager.ServerSendToMany(message, _recipientChannels);
    }

    /// <summary>
    ///     Whether the listener could see the speaker at all: the same visibility-layer rule PVS applies, so voice
    ///         never reaches anyone the speaker's entity is never sent to. That keeps ghosts, and anything else on a
    ///         layer living eyes lack, out of living players' voice exactly as they are out of their view.
    /// </summary>
    private bool CanSee(EntityUid listenerUid, int speakerVisibilityMask)
    {
        var listenerMask = EyeComponent.DefaultVisibilityMask;
        if (_eyeQuery.TryComp(listenerUid, out var eyeComponent))
            listenerMask |= eyeComponent.VisibilityMask;

        return (listenerMask & speakerVisibilityMask) == speakerVisibilityMask;
    }

    private bool CanHear(EntityUid listenerUid)
    {
        if (_ghostQuery.HasComp(listenerUid))
            return true;

        return !_mobStateSystem.IsIncapacitated(listenerUid) && !_sleepingQuery.HasComp(listenerUid);
    }

    /// <summary>
    ///     Whether this player talks without holding push-to-talk: their own setting, if the server allows it.
    /// </summary>
    public bool IsVoiceActivated(ICommonSession session)
    {
        return _voiceActivationAllowed && _configurationManager.GetClientCVar(session.Channel, KsCCVars.VoiceActivation);
    }

    /// <param name="attempt">
    ///     Whether the player is trying to talk right now (audio arrived, or the key is down), rather than their page's
    ///         state just being brought up to date. Only an attempt runs <see cref="ActionBlockerSystem.CanSpeak"/>,
    ///         whose refusal can show a popup.
    /// </param>
    private KsVoiceBlockReason? GetBlockReason(ICommonSession session, TalkerState state, TimeSpan now, bool voiceActivation, bool attempt, out EntityUid speakerUid)
    {
        speakerUid = default;

        if (!_enabled)
            return KsVoiceBlockReason.Disabled;

        if (_mutes.ContainsKey(session.UserId))
            return KsVoiceBlockReason.AdminMuted;

        if (state.AutoMuteUntil > now)
            return KsVoiceBlockReason.AutoMuted;

        if (state.CooldownUntil > now)
            return KsVoiceBlockReason.Cooldown;

        if (session.AttachedEntity is not { Valid: true } attachedUid ||
            _ghostQuery.HasComp(attachedUid))
        {
            return KsVoiceBlockReason.NoBody;
        }

        speakerUid = attachedUid;

        if (!state.PushToTalkHeld && !voiceActivation)
            return KsVoiceBlockReason.NotHoldingKey;

        // CanSpeak runs every chunk, so something that starts blocking mid-sentence stops the voice at once. But the
        //      handlers that refuse speech also show a popup (MutingSystem's "you can't speak", for one), which would
        //      then fire several times a second. So once this body is refused, the refusal stands for the rest of the
        //      key press (or, with voice activation, the utterance): one popup per attempt to talk, and a fresh check
        //      on the next.
        if (state.CannotSpeakBodyUid == attachedUid)
            return KsVoiceBlockReason.CannotSpeak;

        // Not trying to talk, so no CanSpeak: its refusals show popups. Report what can be seen without it instead, so a
        //      page isn't told it's live for a body that plainly can't speak. The real check runs on the next attempt.
        if (!attempt)
        {
            return _mobStateSystem.IsIncapacitated(attachedUid) ||
                   _sleepingQuery.HasComp(attachedUid) ||
                   _mutedQuery.HasComp(attachedUid)
                ? KsVoiceBlockReason.CannotSpeak
                : null;
        }

        if (!_actionBlockerSystem.CanSpeak(attachedUid))
        {
            state.CannotSpeakBodyUid = attachedUid;
            return KsVoiceBlockReason.CannotSpeak;
        }

        return null;
    }

    private void UpdateTalkers()
    {
        var now = _gameTiming.CurTime;

        // Nothing tells us when a client changes a replicated cvar, so look now and then: the page shows which mode
        //      is on, and a silent page would otherwise not hear about the switch until its player next spoke.
        var pollVoiceActivation = now >= _nextVoiceActivationPoll;
        if (pollVoiceActivation)
            _nextVoiceActivationPoll = now + VoiceActivationPollInterval;

        _scratchUsers.Clear();
        foreach (var (userId, state) in _talkers)
        {
            if (state.IndicatorUid != null && state.TalkingUntil <= now)
                ClearIndicator(state);

            if (state.BurstStart != null && now - state.LastRelay > BurstGap)
                EndBurst(userId, state);

            if (pollVoiceActivation &&
                state.PageStateSent &&
                _playerManager.TryGetSessionById(userId, out var session) &&
                IsVoiceActivated(session) != state.LastPageVoiceActivation)
            {
                _scratchUsers.Add(userId);
            }
        }

        foreach (var userId in _scratchUsers)
            RefreshPageState(userId);
    }

    private void SetIndicator(TalkerState state, EntityUid speakerUid)
    {
        if (state.IndicatorUid == speakerUid)
            return;

        ClearIndicator(state);

        EnsureComp<KsVoiceIndicatorComponent>(speakerUid);
        _appearanceSystem.SetData(speakerUid, KsVoiceVisuals.Talking, true);
        state.IndicatorUid = speakerUid;
    }

    private void ClearIndicator(TalkerState state)
    {
        if (state.IndicatorUid is not { } indicatorUid)
            return;

        state.IndicatorUid = null;
        if (!TerminatingOrDeleted(indicatorUid))
            _appearanceSystem.SetData(indicatorUid, KsVoiceVisuals.Talking, false);
    }

    private void EndBurst(NetUserId userId, TalkerState state)
    {
        if (state.BurstStart is not { } burstStart)
            return;

        state.BurstStart = null;
        if (!_logBursts)
            return;

        var seconds = (state.LastRelay - burstStart).TotalSeconds;
        if (_playerManager.TryGetSessionById(userId, out var session) && session.AttachedEntity is { } attachedUid)
            _adminLogManager.Add(LogType.KsVoice, LogImpact.Low, $"{ToPrettyString(attachedUid):player} talked on voice chat for {seconds:0.#}s");
    }

    private void UpdatePageState(NetUserId userId, TalkerState state, KsVoiceBlockReason? block, bool voiceActivation, TimeSpan now)
    {
        _uplinkManager.SetTransmitting(userId, block == null);

        if (state.PageStateSent && state.LastPageBlock == block && state.LastPageVoiceActivation == voiceActivation)
            return;

        state.PageStateSent = true;
        state.LastPageBlock = block;
        state.LastPageVoiceActivation = voiceActivation;

        double? seconds = block switch
        {
            KsVoiceBlockReason.AutoMuted => (state.AutoMuteUntil - now).TotalSeconds,
            KsVoiceBlockReason.Cooldown => (state.CooldownUntil - now).TotalSeconds,
            // Admin mutes run on real time so that they survive round restarts.
            KsVoiceBlockReason.AdminMuted when _mutes.TryGetValue(userId, out var mute) && mute.Until is { } until => (until - _gameTiming.RealTime).TotalSeconds,
            _ => null,
        };

        _uplinkManager.SendStatus(userId, new
        {
            type = "state",
            transmitting = block == null,
            reason = block == null ? null : ReasonId(block.Value),
            seconds = seconds == null ? (int?)null : (int)Math.Ceiling(Math.Max(0d, seconds.Value)),
            voiceActivation,
        });
    }

    /// <summary>
    ///     Re-evaluates and pushes the user's page state now, for changes that don't come with a chunk.
    /// </summary>
    private void RefreshPageState(NetUserId userId)
    {
        if (!_uplinkManager.IsConnected(userId) || !_playerManager.TryGetSessionById(userId, out var session))
            return;

        var state = GetTalker(userId);
        var now = _gameTiming.CurTime;
        var voiceActivation = IsVoiceActivated(session);

        // Pressing the key is trying to talk; a page just being told the current state isn't.
        var block = GetBlockReason(session, state, now, voiceActivation, attempt: state.PushToTalkHeld, out _);
        UpdatePageState(userId, state, block, voiceActivation, now);
    }

    /// <summary>
    ///     Resends every connected page's state, for a server setting the page shows.
    /// </summary>
    private void RefreshAllPageStates()
    {
        _scratchUsers.Clear();
        foreach (var (userId, state) in _talkers)
        {
            state.PageStateSent = false;
            _scratchUsers.Add(userId);
        }

        foreach (var userId in _scratchUsers)
            RefreshPageState(userId);
    }

    private static string ReasonId(KsVoiceBlockReason reason)
    {
        return reason switch
        {
            KsVoiceBlockReason.Disabled => "disabled",
            KsVoiceBlockReason.AdminMuted => "admin-muted",
            KsVoiceBlockReason.AutoMuted => "auto-muted",
            KsVoiceBlockReason.Cooldown => "cooldown",
            KsVoiceBlockReason.NoBody => "no-body",
            KsVoiceBlockReason.NotHoldingKey => "not-holding-key",
            KsVoiceBlockReason.CannotSpeak => "cannot-speak",
            _ => "unknown",
        };
    }

    private TalkerState GetTalker(NetUserId userId)
    {
        if (!_talkers.TryGetValue(userId, out var state))
            _talkers[userId] = state = new TalkerState();

        return state;
    }

    private void OnUplinkConnectionChanged(NetUserId userId, bool connected)
    {
        var state = GetTalker(userId);
        state.PageStateSent = false;

        if (!_playerManager.TryGetSessionById(userId, out var session))
            return;

        RaiseNetworkEvent(new KsVoiceUplinkStatusEvent(connected), session);
        _adminLogManager.Add(LogType.KsVoice, LogImpact.Low, $"{session:player} {(connected ? "connected" : "disconnected")} a voice chat microphone page");

        if (connected)
            RefreshPageState(userId);
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
            return;

        foreach (var (userId, state) in _talkers)
        {
            ClearIndicator(state);
            EndBurst(userId, state);
            state.PushToTalkHeld = false;
        }
    }

    public bool IsHoldingPushToTalk(NetUserId userId)
        => _talkers.TryGetValue(userId, out var state) && state.PushToTalkHeld;

    /// <summary>
    ///     Records whether the user is holding push-to-talk. Normally driven by <see cref="KsVoicePushToTalkEvent"/>.
    /// </summary>
    public void SetPushToTalk(NetUserId userId, bool held)
    {
        var state = GetTalker(userId);
        state.PushToTalkHeld = _enabled && held;
        state.CannotSpeakBodyUid = null;
        RefreshPageState(userId);
    }

    [SubscribeNetworkEvent]
    private void OnPushToTalk(KsVoicePushToTalkEvent args, EntitySessionEventArgs sessionArgs)
    {
        SetPushToTalk(sessionArgs.SenderSession.UserId, args.Held);
    }

    [SubscribeNetworkEvent]
    private void OnRequestLink(KsVoiceRequestLinkEvent args, EntitySessionEventArgs sessionArgs)
    {
        var session = sessionArgs.SenderSession;

        if (!_enabled || !_configurationManager.GetCVar(KsCCVars.VoiceUplinkEnabled))
        {
            RaiseNetworkEvent(new KsVoiceLinkEvent(null, "ks-voice-link-error-disabled"), session);
            return;
        }

        if (_linkManager.GetPublicBaseUrl() == null)
        {
            RaiseNetworkEvent(new KsVoiceLinkEvent(null, "ks-voice-link-error-no-url"), session);
            return;
        }

        // Resets are rate limited: each one disconnects the page and writes an admin log entry.
        var state = GetTalker(session.UserId);
        var now = _gameTiming.CurTime;
        var reset = args.Reset && now >= state.NextLinkResetAllowed;
        if (reset)
            state.NextLinkResetAllowed = now + LinkResetCooldown;

        // A reset refused by the cooldown must say so. Handing back the old link as if it were new would leave the
        //      player believing a leaked link was revoked when it still works.
        var errorLocId = args.Reset && !reset ? "ks-voice-link-error-reset-cooldown" : null;

        var token = _linkManager.ResolveToken(session, reset);
        RaiseNetworkEvent(new KsVoiceLinkEvent(_linkManager.BuildUrl(token), errorLocId), session);

        if (reset)
            _adminLogManager.Add(LogType.KsVoice, LogImpact.Low, $"{session:player} reset their voice chat link");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected || !_talkers.TryGetValue(args.Session.UserId, out var state))
            return;

        // The client only reports push-to-talk changes, and one that crashed or lost its connection mid-press never
        //      sends the release. Left latched, a player back in the same round with a fresh link would transmit
        //      without holding the key.
        state.PushToTalkHeld = false;
        state.CannotSpeakBodyUid = null;
        ClearIndicator(state);
        EndBurst(args.Session.UserId, state);
    }

    /// <summary>
    ///     A new body (spawning, ghosting, being put in something) changes what the page should say, so tell it now
    ///         rather than whenever the player next talks: otherwise a page opened in the lobby says "no body" all round.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        RefreshPageState(args.Player.UserId);
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (!_talkers.TryGetValue(args.Player.UserId, out var state))
            return;

        // Push-to-talk is left alone: it mirrors a key the player may still be holding, and the client only
        //      reports changes. With no body, GetBlockReason already stops the audio.
        ClearIndicator(state);
        RefreshPageState(args.Player.UserId);
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _scratchUsers.Clear();
        foreach (var (userId, mute) in _mutes)
        {
            if (mute.Until == null)
                _scratchUsers.Add(userId);
        }

        foreach (var userId in _scratchUsers)
        {
            _mutes.Remove(userId);

            // Otherwise their page keeps saying they're muted until they next try to talk.
            RefreshPageState(userId);
        }

        // Entities are about to be deleted; drop indicators without touching them. Players who have left take
        //      their state with them (an active auto-mute doesn't outlive the round anyway).
        _scratchUsers.Clear();
        foreach (var (userId, state) in _talkers)
        {
            state.IndicatorUid = null;
            state.BurstStart = null;
            state.PageStateSent = false;

            if (!_playerManager.TryGetSessionById(userId, out _))
                _scratchUsers.Add(userId);
        }

        foreach (var userId in _scratchUsers)
            _talkers.Remove(userId);
    }

    private sealed class TalkerState
    {
        public bool PushToTalkHeld;
        public ushort Sequence;
        public EntityUid? IndicatorUid;
        public TimeSpan TalkingUntil;
        public TimeSpan? BurstStart;
        public TimeSpan LastRelay;
        public TimeSpan CooldownUntil;
        public TimeSpan AutoMuteUntil;
        public bool PageStateSent;
        public TimeSpan NextLinkResetAllowed;
        public KsVoiceBlockReason? LastPageBlock;
        public bool LastPageVoiceActivation;

        /// <summary>
        ///     When the last chunk arrived from the page, relayed or not.
        /// </summary>
        public TimeSpan LastChunkReceived;

        /// <summary>
        ///     The body that failed <see cref="ActionBlockerSystem.CanSpeak"/> during the current key press, if any.
        /// </summary>
        public EntityUid? CannotSpeakBodyUid;
    }
}

/// <summary>
///     Why a talker's audio is not being relayed right now.
/// </summary>
public enum KsVoiceBlockReason : byte
{
    Disabled,
    AdminMuted,
    AutoMuted,
    Cooldown,
    NoBody,
    NotHoldingKey,
    CannotSpeak,
}
