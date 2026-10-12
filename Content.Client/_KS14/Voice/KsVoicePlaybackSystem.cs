using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._KS14.Audio;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.EmoteAudioEffect;
using Content.Shared._KS14.Voice;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Verbs;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.Replays.Playback;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Replays;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Plays relayed voice positionally.
///
///     The engine gives content no streaming audio source, only whole-buffer sources (<see cref="IAudioManager.LoadAudioRaw"/>
///         plus <see cref="IAudioManager.CreateAudioSource"/>). So each talker's audio is reassembled into a jitter buffer
///         and played as a chain of short chunks. To hide the seams, every chunk also carries the first
///         <see cref="OverlapSamples"/> of the following audio, faded out, and the next chunk starts with those same samples
///         faded in, timed by <see cref="KsVoiceChunkTiming"/> so the two line up to the sample whatever the frame rate.
///
///     Sources are positioned by hand every frame, as the engine's MIDI renderer does for its own streaming sources.
///         The sources are plain OpenAL sources that are never networked, so the only networked things playback depends
///         on are the voice packets themselves and the speaker's entity, for its position.
///
///     A talker's playback state lives on their entity, in <see cref="KsVoicePlaybackComponent"/>, along with whether
///         the local player muted them. The one exception is a talker whose entity this client doesn't know yet (voice
///         packets and entity state travel separately): their audio waits in <see cref="_pendingSpeakers"/>, since
///         there is no entity to hold it, and moves onto the entity as soon as it arrives.
/// </summary>
public sealed partial class KsVoicePlaybackSystem : EntitySystem
{
    private const int ChunkSamples = KsVoiceConstants.SampleRate * 120 / 1000;
    private const int OverlapSamples = KsVoiceConstants.SampleRate * 20 / 1000;
    private const float OverlapSeconds = (float)OverlapSamples / (float)KsVoiceConstants.SampleRate;

    /// <summary>
    ///     Fade applied at a chunk edge that isn't crossfaded (the start or end of an utterance), just long enough to
    ///         avoid a click without eating into the speech.
    /// </summary>
    private const int DeclickSamples = KsVoiceConstants.SampleRate * 2 / 1000;

    /// <summary>
    ///     A talker silent for this long has finished their utterance, so whatever is still buffered is played out
    ///         rather than held back waiting for more.
    /// </summary>
    private static readonly TimeSpan FlushAfter = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Per-frame decay of the peak-held frame-time estimate: it jumps up on a slow frame and relaxes over a few dozen
    ///         frames, so a single hitch makes scheduling more careful for a moment rather than causing a dropout.
    /// </summary>
    private const float FrameEstimateDecay = 0.95f;

    /// <summary>
    ///     Occlusion is a physics raycast; per talker, refresh it at this interval rather than every frame.
    /// </summary>
    private static readonly TimeSpan OcclusionInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Buffered audio beyond this is dropped, to stop latency growing after a network stall.
    /// </summary>
    private const int MaxBufferedSamples = KsVoiceConstants.SampleRate;

    /// <summary>
    ///     Packets that must arrive past a missing one before it is given up for lost.
    /// </summary>
    private const int MaxPendingBeforeSkip = 3;

    private static readonly TimeSpan SpeakerTimeout = TimeSpan.FromSeconds(2);

    [Dependency] private KsVoiceNetManager _voiceNetManager = default!;
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IReplayRecordingManager _replayRecordingManager = default!;
    [Dependency] private IReplayPlaybackManager _replayPlaybackManager = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private EmoteAudioEffectSystem _emoteAudioEffectSystem = default!;

    [Dependency] private EntityQuery<KsVoicePlaybackComponent> _playbackQuery = default!;

    /// <summary>
    ///     Audio from talkers whose entity this client doesn't know yet, keyed by the entity it's for. Only a holding
    ///         area: each entry moves onto its entity's <see cref="KsVoicePlaybackComponent"/> when the entity arrives,
    ///         or is dropped after <see cref="SpeakerTimeout"/>.
    /// </summary>
    private readonly Dictionary<NetEntity, KsVoiceSpeaker> _pendingSpeakers = [];
    private readonly List<NetEntity> _scratchPendingDone = [];
    /// <summary>
    ///     Decoding space shared by every talker (all decoding happens on the main thread): the longest packet or
    ///         concealed gap, whichever is larger.
    /// </summary>
    private readonly short[] _decodeScratch = new short[Math.Max(KsVoiceConstants.MaxChunkSamples, KsVoiceOpus.MaxConcealSamples)];

    /// <summary>
    ///     The replay tick before the current jump, to tell a rewind from a step forward.
    /// </summary>
    private GameTick _replayTickBeforeJump;

    private bool _enabled;
    private bool _hearEnabled;
    private float _volume;
    private float _range;
    private int _jitterSamples;
    private float _frameEstimate = 1f / 60f;

    /// <summary>
    ///     Voice frames received since startup, for tests and diagnostics.
    /// </summary>
    public int ReceivedFrameCount { get; private set; }

    /// <summary>
    ///     Chunks started with an effect from the talker's gear (a mask's muffling), for tests and diagnostics. Counted
    ///         when the effect is asked for, since headless clients can't create the effect itself.
    /// </summary>
    public int EffectedChunkCount { get; private set; }

    /// <summary>
    ///     Chunks started, effect or not, for tests and diagnostics: the count <see cref="EffectedChunkCount"/> is out of.
    /// </summary>
    public int StartedChunkCount { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.VoiceEnabled, value =>
        {
            _enabled = value;
            if (!value)
                StopAll();
        }, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceHearEnabled, value =>
        {
            _hearEnabled = value;
            if (!value)
                StopAll();
        }, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceVolume, value => _volume = Math.Max(0f, value), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceRange, value => _range = value, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.VoiceJitterBufferMs, value =>
            _jitterSamples = Math.Clamp(value, 20, 1000) * KsVoiceConstants.SampleRate / 1000, invokeImmediately: true);

        _voiceNetManager.FrameReceived += OnFrameReceived;
        _replayPlaybackManager.BeforeSetTick += OnBeforeReplayJump;
        _replayPlaybackManager.AfterSetTick += OnAfterReplayJump;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _voiceNetManager.FrameReceived -= OnFrameReceived;
        _replayPlaybackManager.BeforeSetTick -= OnBeforeReplayJump;
        _replayPlaybackManager.AfterSetTick -= OnAfterReplayJump;
        StopAll();
    }

    public bool IsLocallyMuted(EntityUid speakerUid)
        => TryGetPlayback(speakerUid, out var playbackComponent) && playbackComponent.LocallyMuted;

    public void SetLocallyMuted(EntityUid speakerUid, bool muted)
    {
        if (muted)
        {
            EnsureComp<KsVoicePlaybackComponent>(speakerUid).LocallyMuted = true;
            return;
        }

        if (!TryGetPlayback(speakerUid, out var playbackComponent))
            return;

        playbackComponent.LocallyMuted = false;
        if (playbackComponent.Speaker == null)
            RemComp(speakerUid, playbackComponent);
    }

    /// <summary>
    ///     Buffered samples waiting to play for a talker, or -1 if we aren't tracking them. For tests and diagnostics.
    /// </summary>
    public int GetBufferedSamples(NetEntity speakerNetEntity)
    {
        if (_pendingSpeakers.TryGetValue(speakerNetEntity, out var pendingSpeaker))
            return pendingSpeaker.BufferedSamples;

        return TryGetEntity(speakerNetEntity, out var speakerUid) &&
               TryGetPlayback(speakerUid.Value, out var playbackComponent) &&
               playbackComponent.Speaker is { } speaker
            ? speaker.BufferedSamples
            : -1;
    }

    /// <summary>
    ///     A talker's playback component, if it's live. One removed with <see cref="EntitySystem.RemCompDeferred"/> (as a
    ///         finished utterance's is, from inside the enumeration in <see cref="FrameUpdate"/>) is shut down but stays
    ///         stored until the end of the tick, and must not count.
    /// </summary>
    private bool TryGetPlayback(EntityUid speakerUid, [NotNullWhen(true)] out KsVoicePlaybackComponent? playbackComponent)
    {
        return _playbackQuery.TryComp(speakerUid, out playbackComponent) &&
               playbackComponent.LifeStage <= ComponentLifeStage.Running;
    }

    /// <summary>
    ///     Whether audio for this talker is waiting for their entity to reach this client. For tests and diagnostics.
    /// </summary>
    public bool IsWaitingForEntity(NetEntity speakerNetEntity)
        => _pendingSpeakers.ContainsKey(speakerNetEntity);

    private void OnFrameReceived(KsVoiceFrameMessage message)
    {
        // A client-side recording keeps what this player heard, like it keeps their popups.
        if (_replayRecordingManager.IsRecording)
            _replayRecordingManager.RecordClientMessage(
                new KsVoiceReplayFrameEvent(message.Source, message.Sequence, message.Codec, message.StreamStart, message.Payload));

        HandleFrame(message.Source, message.Sequence, new KsVoicePacket(message.Codec, message.Payload, message.StreamStart));
    }

    /// <summary>
    ///     Voice in a replay being watched. Only replays raise this: the server records it and never sends it. Rewinding
    ///         stops what's playing (<see cref="OnAfterReplayJump"/>), and <c>ContentReplayPlaybackManager</c> drops what
    ///         a skip passes over.
    /// </summary>
    [SubscribeNetworkEvent]
    private void OnReplayFrame(KsVoiceReplayFrameEvent args)
        => HandleFrame(args.Source, args.Sequence, new KsVoicePacket(args.Codec, args.Payload, args.StreamStart));

    /// <summary>
    ///     Accepts one relayed voice frame, as if it had just arrived from the server. Public so tests can drive playback
    ///         without a network.
    /// </summary>
    public void HandleFrame(KsVoiceFrameMessage message)
        => HandleFrame(message.Source, message.Sequence, new KsVoicePacket(message.Codec, message.Payload, message.StreamStart));

    private void HandleFrame(NetEntity speakerNetEntity, ushort sequence, KsVoicePacket packet)
    {
        ReceivedFrameCount++;

        if (!_enabled || !_hearEnabled || packet.Payload.Length == 0)
            return;

        // Kept encoded until its turn: Opus is stateful, so packets have to be decoded in sequence order, which is
        //      only known once the jitter buffer has put them in it.
        var speaker = ResolveSpeaker(speakerNetEntity);
        speaker.LastReceived = _gameTiming.RealTime;
        speaker.Receive(sequence, packet, _jitterSamples, _decodeScratch);
    }

    /// <summary>
    ///     The playback state for a talker, created if they have none: on their entity when this client knows it, in
    ///         <see cref="_pendingSpeakers"/> until it does.
    /// </summary>
    private KsVoiceSpeaker ResolveSpeaker(NetEntity speakerNetEntity)
    {
        if (TryGetEntity(speakerNetEntity, out var speakerUid) && !TerminatingOrDeleted(speakerUid.Value))
        {
            var playbackComponent = EnsureComp<KsVoicePlaybackComponent>(speakerUid.Value);
            return playbackComponent.Speaker ??= new KsVoiceSpeaker();
        }

        if (!_pendingSpeakers.TryGetValue(speakerNetEntity, out var pendingSpeaker))
            _pendingSpeakers[speakerNetEntity] = pendingSpeaker = new KsVoiceSpeaker();

        return pendingSpeaker;
    }

    private void OnBeforeReplayJump()
    {
        _replayTickBeforeJump = _gameTiming.CurTick;
    }

    /// <summary>
    ///     After a rewind, stops every talker: their sequence numbers go backwards, which the jitter buffer would take as
    ///         stale audio and drop. Stepping forward, which scrubbing does every tick, leaves playback alone.
    /// </summary>
    private void OnAfterReplayJump()
    {
        if (_gameTiming.CurTick < _replayTickBeforeJump)
            StopAll();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _gameTiming.RealTime;
        _frameEstimate = MathF.Max(frameTime, _frameEstimate * FrameEstimateDecay);

        // Before the enumeration below, since a talker whose entity has arrived gains a component here.
        if (_pendingSpeakers.Count > 0)
            UpdatePendingSpeakers(now);

        var listenerCoordinates = _audioSystem.GetListenerCoordinates();
        var playbackEnumerator = EntityQueryEnumerator<KsVoicePlaybackComponent>();
        while (playbackEnumerator.MoveNext(out var speakerUid, out var playbackComponent))
        {
            if (playbackComponent.LifeStage > ComponentLifeStage.Running || playbackComponent.Speaker is not { } speaker)
                continue;

            speaker.DisposeFinished();
            Schedule(speakerUid, speaker, now);
            UpdateSources(speaker, speakerUid, playbackComponent.LocallyMuted, listenerCoordinates, now);

            if (now - speaker.LastReceived <= SpeakerTimeout || speaker.HasSources)
                continue;

            // Finished talking. A local mute outlives the utterance; nothing else here does.
            speaker.Dispose();
            playbackComponent.Speaker = null;
            if (!playbackComponent.LocallyMuted)
                RemCompDeferred(speakerUid, playbackComponent);
        }
    }

    private void UpdatePendingSpeakers(TimeSpan now)
    {
        _scratchPendingDone.Clear();
        foreach (var (speakerNetEntity, pendingSpeaker) in _pendingSpeakers)
        {
            if (TryGetEntity(speakerNetEntity, out var speakerUid) && !TerminatingOrDeleted(speakerUid.Value))
            {
                // The entity has arrived: its audio moves onto it. If audio sent after the entity arrived already
                //      started a speaker there, that one is newer and wins.
                var playbackComponent = EnsureComp<KsVoicePlaybackComponent>(speakerUid.Value);
                if (playbackComponent.Speaker == null)
                    playbackComponent.Speaker = pendingSpeaker;
                else
                    pendingSpeaker.Dispose();

                _scratchPendingDone.Add(speakerNetEntity);
                continue;
            }

            // Hold only the newest jitter buffer's worth, so playback doesn't start late once the entity arrives.
            pendingSpeaker.KeepNewest(_jitterSamples);
            if (now - pendingSpeaker.LastReceived > SpeakerTimeout)
            {
                pendingSpeaker.Dispose();
                _scratchPendingDone.Add(speakerNetEntity);
            }
        }

        foreach (var speakerNetEntity in _scratchPendingDone)
            _pendingSpeakers.Remove(speakerNetEntity);
    }

    [SubscribeLocalEvent]
    private void OnPlaybackShutdown(Entity<KsVoicePlaybackComponent> entity, ref ComponentShutdown args)
    {
        // The talker's entity is going away (deleted, or the round ended): stop anything of theirs still playing.
        entity.Comp.Speaker?.Dispose();
        entity.Comp.Speaker = null;
    }

    private void Schedule(EntityUid speakerUid, KsVoiceSpeaker speaker, TimeSpan now)
    {
        if (!speaker.Started)
        {
            // Wait for a full jitter buffer, unless the talker has already stopped: an utterance shorter than
            //      the buffer would otherwise never play.
            if (speaker.BufferedSamples < _jitterSamples &&
                !(speaker.BufferedSamples > 0 && now - speaker.LastReceived > FlushAfter))
            {
                return;
            }

            speaker.Started = true;
            StartChunk(speakerUid, speaker, padSamples: 0, seekSeconds: 0f);
            return;
        }

        var remaining = speaker.RemainingSeconds(now);
        var startAt = speaker.PreviousHadLookahead ? OverlapSeconds : 0f;
        if (!KsVoiceChunkTiming.ShouldStartNext(remaining, startAt, _frameEstimate))
            return;

        // Enough for a chunk that can carry its own lookahead, or the talker has stopped and this is the tail.
        if (speaker.BufferedSamples >= OverlapSamples * 2 ||
            speaker.BufferedSamples > 0 && now - speaker.LastReceived > FlushAfter)
        {
            var (padSamples, seekSeconds) = KsVoiceChunkTiming.Align(remaining, startAt);
            StartChunk(speakerUid, speaker, padSamples, seekSeconds);
            return;
        }

        if (remaining > 0f)
            return;

        // Starved mid-utterance: the current chunk has faded itself out, so wait to rebuffer. Its lookahead was
        //      played (faded out) but never crossfaded into anything; drop it rather than play it twice.
        speaker.Started = false;
        speaker.DropPlayedLookahead(OverlapSamples);
    }

    /// <param name="speakerUid">The talker's entity.</param>
    /// <param name="speaker">The talker's playback state.</param>
    /// <param name="padSamples">Leading silence, so a chunk started early still begins on time.</param>
    /// <param name="seekSeconds">How far to skip in, so a chunk started late still lines up.</param>
    private void StartChunk(EntityUid speakerUid, KsVoiceSpeaker speaker, int padSamples, float seekSeconds)
    {
        var audio = speaker.TakeChunk(ChunkSamples, OverlapSamples, out var hasLookahead);
        if (audio.Length == 0)
            return;

        // Crossfade only across edges that actually overlap another chunk; anything else just gets de-clicked.
        ApplyFades(audio,
            fadeInSamples: speaker.PreviousHadLookahead ? OverlapSamples : DeclickSamples,
            fadeOutSamples: hasLookahead ? OverlapSamples : DeclickSamples);
        speaker.PreviousHadLookahead = hasLookahead;

        var samples = audio;
        if (padSamples > 0)
        {
            samples = new short[padSamples + audio.Length];
            audio.CopyTo(samples, padSamples);
        }

        var stream = _audioManager.LoadAudioRaw(samples, 1, KsVoiceConstants.SampleRate);
        var source = _audioManager.CreateAudioSource(stream);
        if (source == null)
        {
            stream.Dispose();
            return;
        }

        source.Global = false;
        source.MaxDistance = _range;
        source.ReferenceDistance = 1f;
        source.RolloffFactor = 1f;
        source.Gain = 0f;
        StartedChunkCount++;

        // What the talker wears does to their voice what it does to their emotes (a mask muffles both). Asked every
        //      chunk, so putting a mask on or off mid-sentence takes effect within 120 ms.
        if (_emoteAudioEffectSystem.GetEffect(speakerUid, EmoteCategory.Vocal) is { } preset)
        {
            EffectedChunkCount++;
            _audioEffectSystem.TryAddEffect(source, preset);
        }

        var chunk = new KsVoiceChunk(source, stream, (float)samples.Length / (float)KsVoiceConstants.SampleRate, seekSeconds);
        speaker.AddChunk(chunk);

        // Position it before it makes any sound.
        speaker.PendingStart = chunk;
    }

    private void UpdateSources(KsVoiceSpeaker speaker, EntityUid speakerUid, bool locallyMuted, MapCoordinates listenerCoordinates, TimeSpan now)
    {
        var gain = locallyMuted ? 0f : _volume;
        var speakerCoordinates = _transformSystem.GetMapCoordinates(speakerUid);
        var delta = speakerCoordinates.Position - listenerCoordinates.Position;
        var distance = delta.Length();

        var audible = speakerCoordinates.MapId != MapId.Nullspace &&
                      speakerCoordinates.MapId == listenerCoordinates.MapId &&
                      distance <= _range;

        if (!audible)
        {
            speaker.Occlusion = 0f;
        }
        else if (now >= speaker.OcclusionRefreshAt || speaker.PendingStart != null)
        {
            speaker.Occlusion = _audioSystem.GetOcclusion(listenerCoordinates, delta, distance, ignoredEnt: speakerUid);
            speaker.OcclusionRefreshAt = now + OcclusionInterval;
        }

        var occlusion = speaker.Occlusion;

        foreach (var chunk in speaker.Chunks)
        {
            chunk.Source.Gain = audible ? gain : 0f;
            chunk.Source.MaxDistance = _range;
            chunk.Source.Position = distance < 0.01f ? listenerCoordinates.Position : speakerCoordinates.Position;
            chunk.Source.Occlusion = occlusion;
        }

        if (speaker.PendingStart is { } pendingChunk)
        {
            speaker.PendingStart = null;
            if (pendingChunk.SeekSeconds > 0f)
                pendingChunk.Source.PlaybackPosition = pendingChunk.SeekSeconds;

            pendingChunk.Source.StartPlaying();
            pendingChunk.Started = true;
            speaker.LastChunkEndsAt = now + TimeSpan.FromSeconds((double)(pendingChunk.LengthSeconds - pendingChunk.SeekSeconds));
        }
    }

    /// <summary>
    ///     Linear fades over the first <paramref name="fadeInSamples"/> and the last <paramref name="fadeOutSamples"/>.
    ///         Equal-length linear fades sum to unity across two overlapping chunks.
    /// </summary>
    private static void ApplyFades(Span<short> samples, int fadeInSamples, int fadeOutSamples)
    {
        var fadeIn = Math.Min(fadeInSamples, samples.Length / 2);
        for (var i = 0; i < fadeIn; i++)
            samples[i] = (short)((float)samples[i] * ((float)i / (float)fadeIn));

        var fadeOut = Math.Min(fadeOutSamples, samples.Length / 2);
        for (var i = 0; i < fadeOut; i++)
        {
            var index = samples.Length - 1 - i;
            samples[index] = (short)((float)samples[index] * ((float)i / (float)fadeOut));
        }
    }

    private void StopAll()
    {
        foreach (var pendingSpeaker in _pendingSpeakers.Values)
            pendingSpeaker.Dispose();

        _pendingSpeakers.Clear();

        var playbackEnumerator = EntityQueryEnumerator<KsVoicePlaybackComponent>();
        while (playbackEnumerator.MoveNext(out var speakerUid, out var playbackComponent))
        {
            if (playbackComponent.Speaker is not { } speaker)
                continue;

            speaker.Dispose();
            playbackComponent.Speaker = null;
            if (!playbackComponent.LocallyMuted)
                RemCompDeferred(speakerUid, playbackComponent);
        }
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!_enabled ||
            args.Target == _playerManager.LocalEntity ||
            !HasComp<KsVoiceIndicatorComponent>(args.Target))
        {
            return;
        }

        var targetUid = args.Target;
        var muted = IsLocallyMuted(targetUid);

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(muted ? "ks-voice-verb-unmute-local" : "ks-voice-verb-mute-local"),
            ClientExclusive = true,
            Act = () => SetLocallyMuted(targetUid, !muted),
        });
    }

    /// <summary>
    ///     One relayed chunk, still encoded. <see cref="StreamStart"/> marks where the server started a fresh encoder.
    /// </summary>
    internal readonly record struct KsVoicePacket(KsVoiceCodec Codec, byte[] Payload, bool StreamStart);

    /// <summary>
    ///     One talker's jitter buffer and the chunks currently playing for them.
    /// </summary>
    internal sealed class KsVoiceSpeaker : IDisposable
    {
        private readonly Dictionary<ushort, KsVoicePacket> _pending = [];
        private readonly List<short> _buffer = [];
        private ushort? _nextSequence;

        /// <summary>
        ///     This talker's decoder, replaced when their packets change codec. Stateful for Opus, so one per talker.
        /// </summary>
        private KsVoiceDecoder? _decoder;

        /// <summary>
        ///     Length of the last packet decoded, taken as the length of any that go missing.
        /// </summary>
        private int _lastPacketSamples = KsVoiceConstants.MaxChunkSamples;

        public readonly List<KsVoiceChunk> Chunks = [];
        public KsVoiceChunk? PendingStart;
        public TimeSpan LastReceived;
        public bool Started;
        public bool PreviousHadLookahead;

        /// <summary>
        ///     When the newest chunk will finish, kept here rather than on the chunk so it survives the chunk being
        ///         disposed. Refined from the source's own playback position while it plays.
        /// </summary>
        public TimeSpan LastChunkEndsAt;

        public float Occlusion;
        public TimeSpan OcclusionRefreshAt;

        public int BufferedSamples => _buffer.Count;

        public bool HasSources => Chunks.Count > 0;

        public KsVoiceChunk? Current => Chunks.Count > 0 ? Chunks[^1] : null;

        /// <summary>
        ///     Seconds until the newest chunk finishes; negative once it has.
        /// </summary>
        public float RemainingSeconds(TimeSpan now)
        {
            if (Current is { Started: true } current && current.Source.Playing)
            {
                var remaining = current.LengthSeconds - current.Source.PlaybackPosition;
                LastChunkEndsAt = now + TimeSpan.FromSeconds((double)remaining);
                return remaining;
            }

            return (float)(LastChunkEndsAt - now).TotalSeconds;
        }

        /// <summary>
        ///     While the talker's entity is unknown, keeps only the newest <paramref name="keepSamples"/> of audio.
        /// </summary>
        public void KeepNewest(int keepSamples)
        {
            if (_buffer.Count <= keepSamples)
                return;

            _buffer.RemoveRange(0, _buffer.Count - keepSamples);
            PreviousHadLookahead = false;
        }

        public void Receive(ushort sequence, KsVoicePacket packet, int jitterSamples, short[] scratch)
        {
            if (_nextSequence is { } next && (short)(sequence - next) < 0)
                return; // Arrived after we gave up on it.

            _nextSequence ??= sequence;
            _pending[sequence] = packet;

            Drain(scratch);

            if (_buffer.Count > MaxBufferedSamples)
            {
                // Dropping the head also drops any lookahead the playing chunk was going to crossfade into.
                _buffer.RemoveRange(0, _buffer.Count - jitterSamples);
                PreviousHadLookahead = false;
            }
        }

        private void Drain(short[] scratch)
        {
            while (true)
            {
                while (_nextSequence is { } next && _pending.Remove(next, out var packet))
                {
                    Decode(packet, scratch);
                    _nextSequence = (ushort)(next + 1);
                }

                if (_pending.Count < MaxPendingBeforeSkip || _nextSequence is not { } missing)
                    return;

                // Give up on the missing packets and resume from the oldest one we have, letting the decoder fill in
                //      for what was lost if it can.
                var resume = _pending.Keys.MinBy(sequence => (short)(sequence - missing));
                Conceal((ushort)(resume - missing), scratch);
                _nextSequence = resume;
            }
        }

        private void Decode(KsVoicePacket packet, short[] scratch)
        {
            // A stream start is a fresh encoder on the server, so its packets need a decoder with no history either.
            if (_decoder == null || _decoder.Codec != packet.Codec || packet.StreamStart)
                _decoder = KsVoiceDecoder.Create(packet.Codec);

            var sampleCount = _decoder.Decode(packet.Payload, scratch);
            if (sampleCount <= 0)
            {
                // Malformed: as good as lost.
                Conceal(1, scratch);
                return;
            }

            _lastPacketSamples = sampleCount;
            Append(scratch, sampleCount);
        }

        private void Conceal(int lostPackets, short[] scratch)
        {
            if (_decoder == null)
                return;

            var sampleCount = _decoder.Conceal(lostPackets * _lastPacketSamples, scratch);
            if (sampleCount > 0)
                Append(scratch, sampleCount);
        }

        private void Append(short[] samples, int count)
        {
            // Not AddRange(ReadOnlySpan<T>), an extension the sandbox may not allow.
            _buffer.EnsureCapacity(_buffer.Count + count);
            for (var i = 0; i < count; i++)
                _buffer.Add(samples[i]);
        }

        /// <summary>
        ///     Takes up to <paramref name="count"/> samples for the next chunk, plus (without consuming them) the
        ///         following <paramref name="overlap"/> samples when available, to be crossfaded into the chunk after.
        /// </summary>
        public short[] TakeChunk(int count, int overlap, out bool hasLookahead)
        {
            // Keep back enough to carry a lookahead whenever the buffer allows it, even if that shortens the chunk:
            //      a chunk without one can't crossfade into the next.
            var take = _buffer.Count >= count + overlap
                ? count
                : _buffer.Count >= overlap * 2 ? _buffer.Count - overlap : _buffer.Count;
            hasLookahead = _buffer.Count - take >= overlap;

            var length = take + (hasLookahead ? overlap : 0);
            var samples = new short[length];
            _buffer.CopyTo(0, samples, 0, length);
            _buffer.RemoveRange(0, take);

            return samples;
        }

        public void AddChunk(KsVoiceChunk chunk)
            => Chunks.Add(chunk);

        /// <summary>
        ///     After a stall, discards the lookahead the last chunk already played (faded out), which no chunk will
        ///         now crossfade from.
        /// </summary>
        public void DropPlayedLookahead(int overlap)
        {
            if (!PreviousHadLookahead)
                return;

            PreviousHadLookahead = false;
            _buffer.RemoveRange(0, Math.Min(overlap, _buffer.Count));
        }

        public void DisposeFinished()
        {
            for (var i = Chunks.Count - 1; i >= 0; i--)
            {
                var chunk = Chunks[i];
                if (chunk == PendingStart || chunk.Source.Playing)
                    continue;

                chunk.Dispose();
                Chunks.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            foreach (var chunk in Chunks)
                chunk.Dispose();

            Chunks.Clear();
            PendingStart = null;
            _pending.Clear();
            _buffer.Clear();
            _decoder = null;
            _nextSequence = null;
            Started = false;
            PreviousHadLookahead = false;
        }
    }

    internal sealed class KsVoiceChunk(IAudioSource source, AudioStream stream, float lengthSeconds, float seekSeconds) : IDisposable
    {
        public readonly IAudioSource Source = source;
        public readonly float LengthSeconds = lengthSeconds;
        public readonly float SeekSeconds = seekSeconds;
        public bool Started;

        public void Dispose()
        {
            Source.StopPlaying();
            Source.Dispose();
            stream.Dispose();
        }
    }
}
