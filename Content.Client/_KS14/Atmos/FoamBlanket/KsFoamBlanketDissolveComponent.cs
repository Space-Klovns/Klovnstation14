namespace Content.Client._KS14.Atmos.FoamBlanket;

/// <summary>
///     A dissolving blanket the client is still showing, and when it stops showing it. Client-only: the mask
///         animation holds its last frame rather than going away, and the server's removal arrives a little after
///         it ends, so the blanket is hidden once the dissolve is over.
/// </summary>
[RegisterComponent]
public sealed partial class KsFoamBlanketDissolveComponent : Component
{
    /// <summary>
    ///     When the blanket is hidden, on the client's clock.
    /// </summary>
    [ViewVariables]
    public TimeSpan EndTime;
}
