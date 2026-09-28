using System.Linq;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Content.Shared.Verbs;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Plays relayed voice positionally.
///
///     The engine gives content no streaming audio source, only whole-buffer sources (<see cref="IAudioManager.LoadAudioRaw"/>
///         plus <see cref="IAudioManager.CreateAudioSource"/>). So each talker's audio is reassembled into a jitter buffer
///         and played as a chain of short chunks. To hide the seams, every chunk also carries the first
///         <see cref="OverlapSamples"/> of the following audio, faded out, and the next chunk starts with those same samples
///         faded in, begun when the current chunk has that much left. Frame-rate timing error then only shifts a short
///         linear crossfade rather than opening a gap.
///
///     Sources are positioned by hand every frame, as the engine's MIDI renderer does for its own streaming sources.
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
    ///     Start the next chunk this far ahead of the ideal moment, since we only get to act once per frame.
    /// </summary>
    private const float StartMarginSeconds = 0.008f;

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
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private readonly Dictionary<NetEntity, KsVoiceSpeaker> _speakers = [];
    private readonly HashSet<NetEntity> _localMutes = [];
    private readonly List<NetEntity> _scratchRemovals = [];
    private readonly short[] _decodeScratch = new short[KsVoiceConstants.MaxChunkSamples + 2];

    private bool _enabled;
    private bool _hearEnabled;
    private float _volume;
    private float _range;
    private int _jitterSamples;

    /// <summary>
    ///     Voice frames received since startup, for tests and diagnostics.
    /// </summary>
    public int ReceivedFrameCount { get; private set; }

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
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _voiceNetManager.FrameReceived -= OnFrameReceived;
        StopAll();
    }

    public bool IsLocallyMuted(NetEntity speaker)
        => _localMutes.Contains(speaker);

    public void SetLocallyMuted(NetEntity speaker, bool muted)
    {
        if (muted)
            _localMutes.Add(speaker);
        else
            _localMutes.Remove(speaker);
    }

    private void OnFrameReceived(KsVoiceFrameMessage message)
    {
        ReceivedFrameCount++;

        if (!_enabled || !_hearEnabled)
            return;

        var sampleCount = KsVoiceAdpcm.Decode(message.Payload, _decodeScratch);
        if (sampleCount <= 0)
            return;

        if (!_speakers.TryGetValue(message.Source, out var speaker))
            _speakers[message.Source] = speaker = new KsVoiceSpeaker();

        speaker.LastReceived = _gameTiming.RealTime;
        speaker.Receive(message.Sequence, _decodeScratch.AsSpan(0, sampleCount).ToArray(), _jitterSamples);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_speakers.Count == 0)
            return;

        var now = _gameTiming.RealTime;
        var listener = _audioSystem.GetListenerCoordinates();

        _scratchRemovals.Clear();
        foreach (var (netEntity, speaker) in _speakers)
        {
            speaker.DisposeFinished();

            if (!TryGetEntity(netEntity, out var speakerUid) || TerminatingOrDeleted(speakerUid.Value))
            {
                speaker.Dispose();
                _scratchRemovals.Add(netEntity);
                continue;
            }

            Schedule(speaker, now);
            UpdateSources(speaker, speakerUid.Value, netEntity, listener);

            if (now - speaker.LastReceived > SpeakerTimeout && !speaker.HasSources)
            {
                speaker.Dispose();
                _scratchRemovals.Add(netEntity);
            }
        }

        foreach (var netEntity in _scratchRemovals)
            _speakers.Remove(netEntity);
    }

    private void Schedule(KsVoiceSpeaker speaker, TimeSpan now)
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
            StartChunk(speaker);
            return;
        }

        var current = speaker.Current;
        var remaining = current is { Source.Playing: true }
            ? current.LengthSeconds - current.Source.PlaybackPosition
            : 0f;

        var startAt = (current?.HasLookahead ?? false) ? OverlapSeconds : 0f;
        if (remaining > startAt + StartMarginSeconds)
            return;

        // Enough for a chunk that can carry its own lookahead, or the talker has stopped and this is the tail.
        if (speaker.BufferedSamples >= OverlapSamples * 2 ||
            speaker.BufferedSamples > 0 && now - speaker.LastReceived > FlushAfter)
        {
            StartChunk(speaker);
            return;
        }

        if (remaining > 0f)
            return;

        // Starved mid-utterance: the current chunk has faded itself out, so wait to rebuffer. Its lookahead was
        //      played (faded out) but never crossfaded into anything; drop it rather than play it twice.
        speaker.Started = false;
        speaker.DropPlayedLookahead(OverlapSamples);
    }

    private void StartChunk(KsVoiceSpeaker speaker)
    {
        var samples = speaker.TakeChunk(ChunkSamples, OverlapSamples, out var hasLookahead);
        if (samples.Length == 0)
            return;

        // Crossfade only across edges that actually overlap another chunk; anything else just gets de-clicked.
        ApplyFades(samples,
            fadeInSamples: speaker.PreviousHadLookahead ? OverlapSamples : DeclickSamples,
            fadeOutSamples: hasLookahead ? OverlapSamples : DeclickSamples);
        speaker.PreviousHadLookahead = hasLookahead;

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

        var chunk = new KsVoiceChunk(source, stream, (float)samples.Length / (float)KsVoiceConstants.SampleRate, hasLookahead);
        speaker.AddChunk(chunk);

        // Position it before it makes any sound.
        speaker.PendingStart = chunk;
    }

    private void UpdateSources(KsVoiceSpeaker speaker, EntityUid speakerUid, NetEntity speakerNetEntity, MapCoordinates listener)
    {
        var gain = _localMutes.Contains(speakerNetEntity) ? 0f : _volume;
        var speakerCoordinates = _transformSystem.GetMapCoordinates(speakerUid);
        var delta = speakerCoordinates.Position - listener.Position;
        var distance = delta.Length();

        var audible = speakerCoordinates.MapId != MapId.Nullspace &&
                      speakerCoordinates.MapId == listener.MapId &&
                      distance <= _range;

        var occlusion = audible ? _audioSystem.GetOcclusion(listener, delta, distance, ignoredEnt: speakerUid) : 0f;

        foreach (var chunk in speaker.Chunks)
        {
            chunk.Source.Gain = audible ? gain : 0f;
            chunk.Source.MaxDistance = _range;
            chunk.Source.Position = distance < 0.01f ? listener.Position : speakerCoordinates.Position;
            chunk.Source.Occlusion = occlusion;
        }

        if (speaker.PendingStart is { } pending)
        {
            speaker.PendingStart = null;
            pending.Source.StartPlaying();
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
        foreach (var speaker in _speakers.Values)
            speaker.Dispose();

        _speakers.Clear();
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

        var target = GetNetEntity(args.Target);
        var muted = _localMutes.Contains(target);

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(muted ? "ks-voice-verb-unmute-local" : "ks-voice-verb-mute-local"),
            ClientExclusive = true,
            Act = () => SetLocallyMuted(target, !muted),
        });
    }

    /// <summary>
    ///     One talker's jitter buffer and the chunks currently playing for them.
    /// </summary>
    private sealed class KsVoiceSpeaker : IDisposable
    {
        private readonly Dictionary<ushort, short[]> _pending = [];
        private readonly List<short> _buffer = [];
        private ushort? _nextSequence;

        public readonly List<KsVoiceChunk> Chunks = [];
        public KsVoiceChunk? PendingStart;
        public TimeSpan LastReceived;
        public bool Started;
        public bool PreviousHadLookahead;

        public int BufferedSamples => _buffer.Count;

        public bool HasSources => Chunks.Count > 0;

        public KsVoiceChunk? Current => Chunks.Count > 0 ? Chunks[^1] : null;

        public void Receive(ushort sequence, short[] samples, int jitterSamples)
        {
            if (_nextSequence is { } next && (short)(sequence - next) < 0)
                return; // Arrived after we gave up on it.

            _nextSequence ??= sequence;
            _pending[sequence] = samples;

            Drain();

            if (_buffer.Count > MaxBufferedSamples)
            {
                // Dropping the head also drops any lookahead the playing chunk was going to crossfade into.
                _buffer.RemoveRange(0, _buffer.Count - jitterSamples);
                PreviousHadLookahead = false;
            }
        }

        private void Drain()
        {
            while (true)
            {
                while (_nextSequence is { } next && _pending.Remove(next, out var samples))
                {
                    _buffer.AddRange(samples);
                    _nextSequence = (ushort)(next + 1);
                }

                if (_pending.Count < MaxPendingBeforeSkip || _nextSequence is not { } missing)
                    return;

                // Give up on the missing packet and resume from the oldest one we have.
                _nextSequence = _pending.Keys.MinBy(sequence => (short)(sequence - missing));
            }
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
            Started = false;
            PreviousHadLookahead = false;
        }
    }

    private sealed class KsVoiceChunk(IAudioSource source, AudioStream stream, float lengthSeconds, bool hasLookahead) : IDisposable
    {
        public readonly IAudioSource Source = source;
        public readonly float LengthSeconds = lengthSeconds;
        public readonly bool HasLookahead = hasLookahead;

        public void Dispose()
        {
            Source.StopPlaying();
            Source.Dispose();
            stream.Dispose();
        }
    }
}
