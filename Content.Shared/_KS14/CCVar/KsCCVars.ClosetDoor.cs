using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether swinging closet doors, and what is mounted on them, are drawn in whole sprite pixels, as BYOND draws a
    ///         transformed icon, rather than sampled smoothly at screen resolution.
    /// </summary>
    public static readonly CVarDef<bool> ClosetDoorPixelSnap =
        CVarDef.Create("klovn.closet_door.pixel_snap", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Debugging aid for the contents a closing closet draws inside it: the doorway they are clipped to is drawn in
    ///         translucent magenta, and the contents themselves are tinted green. Green outside the magenta means the clip
    ///         isn't working; contents that aren't green aren't being drawn by the closet at all.
    /// </summary>
    public static readonly CVarDef<bool> ClosetDoorDebugMask =
        CVarDef.Create("klovn.closet_door.debug_mask", false, CVar.CLIENTONLY);
}
