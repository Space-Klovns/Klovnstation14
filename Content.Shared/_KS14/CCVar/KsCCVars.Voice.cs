using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    #region Availability (replicated, so clients can hide the feature)

    /// <summary>
    ///     Master switch for in-game voice chat. While off, the voice web page and its websocket are not served,
    ///         no links are issued, open microphone pages are disconnected, nothing is relayed, and clients
    ///         hide every voice control.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> VoiceEnabled =
        CVarDef.Create("klovn.voice.enabled", false, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether microphone pages are served and may stay connected. Turning this off disconnects every page, so
    ///         nobody can talk (they reopen their link once it's back on), while players keep hearing any audio
    ///         still in flight and all voice UI stays available. <see cref="VoiceEnabled"/> turns the whole
    ///         feature off.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> VoiceUplinkEnabled =
        CVarDef.Create("klovn.voice.uplink_enabled", true, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How far voice carries, in world units. The server relays to listeners within this range and clients
    ///         use it as the audio source's maximum distance.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceRange =
        CVarDef.Create("klovn.voice.range", 10f, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether players may use voice activation (<see cref="VoiceActivation"/>) instead of holding push-to-talk.
    ///         While off, everyone needs the key, whatever their own setting says, and clients hide the option.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> VoiceActivationAllowed =
        CVarDef.Create("klovn.voice.voice_activation_allowed", true, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    #endregion

    #region Server settings

    /// <summary>
    ///     Public base URL that players' browsers use to reach this server's status host, e.g.
    ///         <c>https://ks14.example.com</c>. Browsers only grant microphone access over HTTPS (or on
    ///         <c>http://localhost</c>), so this should point at a TLS reverse proxy.
    ///         When empty, it is derived from <c>hub.server_url</c>, then <c>transfer.http_endpoint</c>.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> VoicePublicUrl =
        CVarDef.Create("klovn.voice.public_url", "", CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Path of the microphone page in the links players are given, after <see cref="VoicePublicUrl"/>. The status host
    ///         always serves the page at <c>/klovn/voice/</c>; a different path here needs a reverse proxy that maps it
    ///         there (the page loads everything relative to itself, so any path works). <c>/</c> puts the page at the
    ///         root of the public URL. Invalid values fall back to the default with a warning.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> VoicePublicPath =
        CVarDef.Create("klovn.voice.public_path", "/klovn/voice/", CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Whether websocket connections must come from a page whose <c>Origin</c> matches the public URL.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<bool> VoiceCheckOrigin =
        CVarDef.Create("klovn.voice.check_origin", true, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Peak ceiling of the server-side limiter, in dBFS. Nobody's voice is relayed louder than this.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceLimiterCeilingDb =
        CVarDef.Create("klovn.voice.limiter_ceiling_db", -6f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Input RMS level, in dBFS, above which a block of audio counts as abusive (screaming into the mic,
    ///         playing distorted noise, etc.).
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceAbuseRmsDb =
        CVarDef.Create("klovn.voice.abuse_rms_db", -9f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of clipped input samples in a block above which that block counts as abusive.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceAbuseClipRatio =
        CVarDef.Create("klovn.voice.abuse_clip_ratio", 0.05f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Seconds of abusive audio within the detection window that trigger an automatic mute. The window is ten
    ///         seconds of transmitted audio; anything longer is treated as the whole window.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceAbuseSeconds =
        CVarDef.Create("klovn.voice.abuse_seconds", 3f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Length of an automatic mute, in seconds. 0 disables automatic muting (the limiter still applies).
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceAutoMuteSeconds =
        CVarDef.Create("klovn.voice.auto_mute_seconds", 120f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Longest a player may talk without a break, in seconds, before being put on cooldown.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceMaxContinuousSeconds =
        CVarDef.Create("klovn.voice.max_continuous_seconds", 60f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Cooldown after hitting <see cref="VoiceMaxContinuousSeconds"/>, in seconds.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceCooldownSeconds =
        CVarDef.Create("klovn.voice.cooldown_seconds", 3f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     How much faster than real time a microphone page may send audio before it is disconnected.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> VoiceUplinkRateFactor =
        CVarDef.Create("klovn.voice.uplink_rate_factor", 1.25f, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Failed websocket authentications allowed per remote address per minute before further attempts
    ///         from that address are refused outright.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> VoiceAuthFailuresPerMinute =
        CVarDef.Create("klovn.voice.auth_failures_per_minute", 10, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Whether each burst of talking is written to the admin log.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> VoiceAdminLogBursts =
        CVarDef.Create("klovn.voice.admin_log_bursts", true, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     How relayed voice is compressed: <c>opus</c>, the default (much cleaner at half the bandwidth, but costs more
    ///         CPU on the server, which encodes, and on every client, which decodes), or <c>adpcm</c> (64 kbps, a little
    ///         hissy, cheap). Anything else means <c>adpcm</c>. Takes effect from each talker's next chunk.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<string> VoiceCodec =
        CVarDef.Create("klovn.voice.codec", "opus", CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Opus bitrate, in bits per second, when <see cref="VoiceCodec"/> is <c>opus</c>. Clamped to 6000–64000.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> VoiceOpusBitrate =
        CVarDef.Create("klovn.voice.opus_bitrate", 32000, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Opus encoder complexity, 0 to 10. Higher sounds marginally better for speech and costs the server more CPU per
    ///         talker (encoding runs on each page connection's thread, not the game loop). See the design doc for
    ///         measured costs.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> VoiceOpusComplexity =
        CVarDef.Create("klovn.voice.opus_complexity", 2, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Whether relayed voice goes into server-side round replays, so it can be heard when they're watched.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> VoiceRecordInReplays =
        CVarDef.Create("klovn.voice.record_in_replays", true, CVar.ARCHIVE | CVar.SERVERONLY);

    #endregion

    #region Client settings

    /// <summary>
    ///     Whether this client plays other players' voices.
    /// </summary>
    public static readonly CVarDef<bool> VoiceHearEnabled =
        CVarDef.Create("klovn.voice.hear_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    ///     Volume multiplier for other players' voices.
    /// </summary>
    public static readonly CVarDef<float> VoiceVolume =
        CVarDef.Create("klovn.voice.volume", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    ///     How much audio, in milliseconds, is buffered per talker before playback starts. Higher values
    ///         survive worse connections at the cost of latency.
    /// </summary>
    public static readonly CVarDef<int> VoiceJitterBufferMs =
        CVarDef.Create("klovn.voice.jitter_buffer_ms", 120, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    ///     Talk whenever the microphone page's noise gate is open, without holding push-to-talk. Replicated to the
    ///         server, which reads it for every chunk; only honoured while <see cref="VoiceActivationAllowed"/>.
    /// </summary>
    public static readonly CVarDef<bool> VoiceActivation =
        CVarDef.Create("klovn.voice.voice_activation", false, CVar.ARCHIVE | CVar.CLIENT | CVar.REPLICATED);

    #endregion
}
