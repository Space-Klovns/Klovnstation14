namespace Content.Client._KS14.OverlayZIndex;

/// <summary>
///     Turns a member of one of the per-space enums into an <see cref="Overlay.ZIndex"/>.
/// </summary>
/// <remarks>
///     An overlay that sets no <see cref="Overlay.ZIndex"/> draws as if it were 0, and each enum's <c>Default</c> stands
///         for those. The index is therefore the member's position relative to <c>Default</c>: negative before it, 0 at
///         it, positive after it. The order of the members decides all of it, and nothing here needs to be edited to
///         insert one.
/// </remarks>
public static class OverlayZIndexExtensions
{
    public static int ToZIndex(this BeforeLightingZIndex member)
    {
        return (int)member - (int)BeforeLightingZIndex.Default;
    }

    public static int ToZIndex(this WorldSpaceBelowEntitiesZIndex member)
    {
        return (int)member - (int)WorldSpaceBelowEntitiesZIndex.Default;
    }

    public static int ToZIndex(this WorldSpaceBelowFovZIndex member)
    {
        return (int)member - (int)WorldSpaceBelowFovZIndex.Default;
    }
}
