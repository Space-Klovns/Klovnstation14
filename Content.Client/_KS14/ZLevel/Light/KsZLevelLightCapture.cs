using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._KS14.ZLevel.Light;

/// <summary>
///     One z-level's finished light map and colour image, kept so the z-level below it can be lit by them.
/// </summary>
/// <remarks>
///     Both have to be copied out rather than referenced: the viewport is reused for every z-level pass, and
///         the engine clears its light target at the top of each one
///         (Clyde.LightRendering.cs, immediately before the BeforeLighting overlays run). A pass can therefore
///         never see the pass before it - only what was taken out in between.
/// </remarks>
public sealed class KsZLevelLightCapture : IDisposable
{
    /// <summary>
    ///     The light map, at the engine's light resolution rather than the viewport's.
    /// </summary>
    public IRenderTexture Light = default!;

    /// <summary>
    ///     The colour image, whose alpha is already exactly "where this z-level's floor, walls and entities
    ///         are" - which is the same thing as "where light cannot get through", for free.
    /// </summary>
    public IRenderTexture Colour = default!;

    /// <summary>
    ///     The world area these cover, which is what places them correctly in a pass drawn at another scale.
    /// </summary>
    public Box2Rotated WorldBounds;

    public void Dispose()
    {
        Light?.Dispose();
        Colour?.Dispose();
    }
}
