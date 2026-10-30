namespace Content.Client._KS14.ZLevel;

/// <summary>
///     What the z-level renderer is drawing right now, for overlays that can do less work on a lower z-level.
/// </summary>
/// <remarks>
///     Static so overlays can ask without looking anything up, as with
///         <see cref="Content.Client._KS14.Portal.KsPortalRenderSystem.PassActive"/>.
/// </remarks>
public static class KsZLevelRenderPass
{
    /// <summary>
    ///     Whether a z-level below the viewer's is being rendered. It is scaled down and blurred afterwards, so
    ///         soft effects (ambient occlusion, bloom) can be drawn at a fraction of the resolution.
    /// </summary>
    public static bool LowerLevel { get; internal set; }
}
