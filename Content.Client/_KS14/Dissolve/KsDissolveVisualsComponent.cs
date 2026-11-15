using Robust.Client.Graphics;

namespace Content.Client._KS14.Dissolve;

/// <summary>
///     What the client keeps for an entity it is dissolving: its own shader instance and when the dissolve began on
///         the client's clock. Client-only.
/// </summary>
[RegisterComponent]
public sealed partial class KsDissolveVisualsComponent : Component
{
    /// <summary>
    ///     When the dissolve began, on the client's clock: the server's start time is on the server's clock, and the
    ///         client's predicted CurTime runs ahead of it, so the two cannot be compared.
    /// </summary>
    [ViewVariables]
    public TimeSpan LocalStartTime;

    [ViewVariables]
    public ShaderInstance? Shader;

    /// <summary>
    ///     Different for each entity, so neighbours dissolve in different blobs.
    /// </summary>
    [ViewVariables]
    public float Seed;
}
