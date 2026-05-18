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
        CVarDef.Create("klovn.tts.enabled", true, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Address to be used when requesting data.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<string> TtsEndpoint =
        CVarDef.Create("klovn.tts.endpoint", "http://localhost:5000" /* Piper */, CVar.SERVER);
}
