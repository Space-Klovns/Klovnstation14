using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    /// <summary>
    ///     When set, the viewport's finished texture is drawn through this shader (single-pass blur, for example).
    ///     Not applied on the z-level path, which blits its own passes.
    /// </summary>
    public ShaderInstance? KsDrawShader;

    /// <summary>
    ///     The box the viewport texture is drawn into, in pixels relative to this control, or null before the
    ///         first draw has created the viewport.
    /// </summary>
    public UIBox2i? KsGetDrawBox() => _viewport == null ? null : GetDrawBox();
}
