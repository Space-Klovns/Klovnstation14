using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Seconds between ore retrieval matrix deliveries. Each delivery hands out what the ore wells made over the
    ///         whole interval, so a longer interval means fewer, bigger deliveries at the same rate.
    /// </summary>
    public static readonly CVarDef<float> OreWellReceiverInterval =
        CVarDef.Create("klovn.orewell.receiver_interval", 30f, CVar.SERVERONLY);
}
