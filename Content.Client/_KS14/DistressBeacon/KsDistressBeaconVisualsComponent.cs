using Robust.Client.Graphics;

namespace Content.Client._KS14.DistressBeacon;

/// <summary>
///     What the client keeps for a beacon: the outline it has put on its wearer, and the pulse it was last drawn
///         with, which the light and the outline both follow. Client-only.
/// </summary>
[RegisterComponent]
public sealed partial class KsDistressBeaconVisualsComponent : Component
{
    /// <summary>
    ///     Whose sprite has the outline, or null while it has none.
    /// </summary>
    [ViewVariables]
    public EntityUid? OutlinedUid;

    /// <summary>
    ///     The outline's own shader instance.
    /// </summary>
    [ViewVariables]
    public ShaderInstance? Shader;

    /// <summary>
    ///     The pulse, from 0 at the trough to 1 at the peak, as of the last frame. The light's energy and the outline's
    ///         opacity are both worked out from this one number.
    /// </summary>
    [ViewVariables]
    public float Wave;

    /// <summary>
    ///     The outline's opacity as of the last frame.
    /// </summary>
    [ViewVariables]
    public float OutlineAlpha;
}
