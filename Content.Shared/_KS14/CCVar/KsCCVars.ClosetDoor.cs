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
    ///     Whether an open closet door is hidden behind the closets in front of it, as it would be if it were part of
    ///         its closet's sprite. The door is drawn over every closet otherwise, so in a column of closets an open
    ///         door shows through the top of the one below.
    /// </summary>
    public static readonly CVarDef<bool> ClosetDoorOcclusionMask =
        CVarDef.Create("klovn.closet_door.occlusion_mask", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Debugging aid for the contents a closing closet draws inside it: the doorway they are clipped to is drawn in
    ///         translucent magenta, and the contents themselves are tinted green. Green outside the magenta means the clip
    ///         isn't working; contents that aren't green aren't being drawn by the closet at all.
    /// </summary>
    public static readonly CVarDef<bool> ClosetDoorDebugMask =
        CVarDef.Create("klovn.closet_door.debug_mask", false, CVar.CLIENTONLY);
}
