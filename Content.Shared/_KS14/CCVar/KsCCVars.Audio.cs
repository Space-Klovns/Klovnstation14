using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Fraction (0 - 1 = 0% to 100%) of the minimum volume of ambient music.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> MinAmbientMusicVolume =
        CVarDef.Create("klovn.audio.min_ambmusic_volume", 0.25f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Fraction (0 - 1 = 0% to 100%) of the minimum volume of ambient sounds.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> MinAmbientEffectsVolume =
        CVarDef.Create("klovn.audio.min_ambeffects_volume", 0.85f, CVar.SERVER | CVar.REPLICATED);
}
