using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Is TTS enabled? Duh.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> TtsEnabled =
        CVarDef.Create("klovn.tts.enabled", false, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED); // fuck the client they are not allowed to turn it off

    /// <summary>
    ///     Address to be used when requesting data.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> TtsEndpoint =
        CVarDef.Create("klovn.tts.endpoint", "http://localhost:8000/tts", CVar.ARCHIVE | CVar.SERVER);

    /// <summary>
    ///     What the server asks the endpoint for, and what it sends clients.
    ///     <list type="bullet">
    ///         <item><c>vorbis</c>: the original behaviour. The request is unchanged, and whatever comes back is sent
    ///             on as it is.</item>
    ///         <item><c>opus</c>: the request adds <c>"format": "opus"</c>, and the Ogg Opus that comes back is sent
    ///             on as it is. Clients decode it in content, off the game thread, since the engine can't play Opus.</item>
    ///         <item><c>transcode</c>, the default: the request is unchanged, and the server re-encodes the Ogg Vorbis
    ///             or WAV that comes back into Ogg Opus before sending it, per <see cref="TtsOpusBitrate"/>.</item>
    ///     </list>
    ///     Clips are labelled by what they actually contain, not by this setting, so an endpoint that ignores the
    ///         format still plays.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<string> TtsCodec =
        CVarDef.Create("klovn.tts.codec", "transcode", CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Bits per second for <c>klovn.tts.codec transcode</c>. Clamped to 6000-128000.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> TtsOpusBitrate =
        CVarDef.Create("klovn.tts.opus_bitrate", 32000, CVar.ARCHIVE | CVar.SERVERONLY);

    /// <summary>
    ///     Encoder effort for <c>klovn.tts.codec transcode</c>, 0-10. Transcoding runs on the thread pool, once per
    ///         distinct line, so this can afford to be high.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> TtsOpusComplexity =
        CVarDef.Create("klovn.tts.opus_complexity", 10, CVar.ARCHIVE | CVar.SERVERONLY);
}
