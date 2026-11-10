using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    /// Whether items and anchored structures cast ambient occlusion, not just walls.
    /// </summary>
    public static readonly CVarDef<bool> AmbientOcclusionEntities =
        CVarDef.Create("klovn.ambient_occlusion.entities", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
