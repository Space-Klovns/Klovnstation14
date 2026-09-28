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
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Voice;

/// <summary>
///     Relays moderated microphone audio from voice pages (<see cref="KsVoiceUplinkManager"/>) to nearby players, and
///         shows who is talking.
///
///     A chunk is relayed only while voice is enabled, the talker holds their in-game push-to-talk key, is not muted
///         or on cooldown, and their attached entity is not a ghost and passes <see cref="ActionBlockerSystem.CanSpeak"/>
///         (so crit, death, sleep, mime vows and admin freeze-mutes all silence voice exactly as they silence speech).
///         Listeners are the other in-game players within <see cref="KsCCVars.VoiceRange"/> on the same map, excluding
///         incapacitated or sleeping ones; ghosts hear too.
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

    [Dependency] private KsVoiceUplinkManager _uplinkManager = default!;
    [Dependency] private KsVoiceLinkManager _linkManager = default!;
    [Dependency] private IServerNetManager _netManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;
    [Dependency] private EntityQuery<SleepingComponent> _sleepingQuery = default!;
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

    /// <summary>
    ///     Relayed chunks since startup, for tests and diagnostics.
    /// </summary>
    public int RelayedChunkCount { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.VoiceEnabled, OnEnabledChanged, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceRange, value => _range = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceAutoMuteSeconds, value => _autoMuteSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceMaxContinuousSeconds, value => _maxContinuousSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceCooldownSeconds, value => _cooldownSeconds = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceAdminLogBursts, value => _logBursts = value, invokeImmediately: true);
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

        var block = GetBlockReason(session, state, now, out var speakerUid);

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

        UpdatePageState(chunk.UserId, state, block, now);

        if (block != null)
            return;

        Relay(chunk, session, speakerUid, state, now);
    }

    private void Relay(KsVoiceInboundChunk chunk, ICommonSession speaker, EntityUid speakerUid, TalkerState state, TimeSpan now)
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
        foreach (var listener in _playerManager.Sessions)
        {
            if (listener == speaker ||
                listener.Status != SessionStatus.InGame ||
                listener.AttachedEntity is not { Valid: true } listenerUid ||
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

            _recipientChannels.Add(listener.Channel);
        }

        RelayedChunkCount++;

        // Encode even with nobody listening, so the talker's encoder state stays continuous.
        var payload = new byte[KsVoiceAdpcm.EncodedSize(chunk.Samples.Length)];
        KsVoiceAdpcm.Encode(ref state.Encoder, chunk.Samples, payload);
        state.Sequence++;

        if (_recipientChannels.Count == 0)
            return;

        var message = new KsVoiceFrameMessage
        {
            Source = GetNetEntity(speakerUid),
            Sequence = state.Sequence,
            Payload = payload,
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

    private KsVoiceBlockReason? GetBlockReason(ICommonSession session, TalkerState state, TimeSpan now, out EntityUid speakerUid)
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

        if (session.AttachedEntity is not { Valid: true } attached ||
            _ghostQuery.HasComp(attached))
        {
            return KsVoiceBlockReason.NoBody;
        }

        speakerUid = attached;

        if (!state.PushToTalkHeld)
            return KsVoiceBlockReason.NotHoldingKey;

        // CanSpeak runs every chunk, so something that starts blocking mid-sentence stops the voice at once. But the
        //      handlers that refuse speech also show a popup (MutingSystem's "you can't speak", for one), which would
        //      then fire several times a second. So once this body is refused, the refusal stands for the rest of the
        //      key press: one popup per attempt to talk, and a fresh check on the next press.
        if (state.CannotSpeakBody == attached)
            return KsVoiceBlockReason.CannotSpeak;

        if (!_actionBlockerSystem.CanSpeak(attached))
        {
            state.CannotSpeakBody = attached;
            return KsVoiceBlockReason.CannotSpeak;
        }

        return null;
    }

    private void UpdateTalkers()
    {
        var now = _gameTiming.CurTime;

        foreach (var (userId, state) in _talkers)
        {
            if (state.IndicatorUid != null && state.TalkingUntil <= now)
                ClearIndicator(state);

            if (state.BurstStart != null && now - state.LastRelay > BurstGap)
                EndBurst(userId, state);
        }
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
        if (state.IndicatorUid is not { } uid)
            return;

        state.IndicatorUid = null;
        if (!TerminatingOrDeleted(uid))
            _appearanceSystem.SetData(uid, KsVoiceVisuals.Talking, false);
    }

    private void EndBurst(NetUserId userId, TalkerState state)
    {
        if (state.BurstStart is not { } burstStart)
            return;

        state.BurstStart = null;
        if (!_logBursts)
            return;

        var seconds = (state.LastRelay - burstStart).TotalSeconds;
        if (_playerManager.TryGetSessionById(userId, out var session) && session.AttachedEntity is { } uid)
            _adminLogManager.Add(LogType.KsVoice, LogImpact.Low, $"{ToPrettyString(uid):player} talked on voice chat for {seconds:0.#}s");
    }

    private void UpdatePageState(NetUserId userId, TalkerState state, KsVoiceBlockReason? block, TimeSpan now)
    {
        _uplinkManager.SetTransmitting(userId, block == null);

        if (state.PageStateSent && state.LastPageBlock == block)
            return;

        state.PageStateSent = true;
        state.LastPageBlock = block;

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
        UpdatePageState(userId, state, GetBlockReason(session, state, now, out _), now);
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
        state.CannotSpeakBody = null;
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

        var token = _linkManager.ResolveToken(session, reset);
        RaiseNetworkEvent(new KsVoiceLinkEvent(_linkManager.BuildUrl(token), null), session);

        if (reset)
            _adminLogManager.Add(LogType.KsVoice, LogImpact.Low, $"{session:player} reset their voice chat link");
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (!_talkers.TryGetValue(args.Player.UserId, out var state))
            return;

        // Push-to-talk is left alone: it mirrors a key the player may still be holding, and the client only
        //      reports changes. With no body, GetBlockReason already stops the audio.
        ClearIndicator(state);
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
            _mutes.Remove(userId);

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
        public KsVoiceAdpcm.EncoderState Encoder;
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

        /// <summary>
        ///     The body that failed <see cref="ActionBlockerSystem.CanSpeak"/> during the current key press, if any.
        /// </summary>
        public EntityUid? CannotSpeakBody;
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
