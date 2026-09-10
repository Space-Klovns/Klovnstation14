using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Fraction of the half-arc of a directional wallmount or stain that stays fully opaque,
    ///         before it starts fading out towards the edge of the arc. One means no fade at all.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ArcVisibilityFeather =
        CVarDef.Create("klovn.arcvisibility.feather", 0.65f, CVar.SERVER | CVar.REPLICATED);
}
