using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    /// Whether swinging closet doors, and what is mounted on them, are drawn in whole sprite pixels, as BYOND draws a
    /// transformed icon, rather than sampled smoothly at screen resolution.
    /// </summary>
    public static readonly CVarDef<bool> ClosetDoorPixelSnap =
        CVarDef.Create("klovn.closet_door.pixel_snap", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
