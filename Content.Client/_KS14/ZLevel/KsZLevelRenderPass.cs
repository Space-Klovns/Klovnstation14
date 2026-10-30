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

    /// <summary>
    ///     Whether a z-level is being rendered only to capture its light and floor for the z-level below. The floor is
    ///         read from the colour image's alpha, so anything that writes alpha where there's no floor must skip it.
    /// </summary>
    public static bool Capture { get; internal set; }
}
