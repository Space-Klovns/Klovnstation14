namespace Content.Client._KS14.Atmos.FoamBlanket;

/// <summary>
///     A blanket this client laid itself, ahead of the server, for its player's own extinguisher shot. Client-only, on
///         a client-side entity that is not an engine predicted spawn, so no state reset deletes it: it is kept until
///         the server's blanket for its tile arrives, or until <see cref="ExpiryTime"/> if that never happens.
/// </summary>
/// <remarks>
///     Until then it is the tile's blanket on this client, and does what one does: keeps chemfires out, puts out
///         whatever walks onto it, and is drawn. Once the server's takes over the tile's cache entry it stops acting,
///         is hidden at once, and is deleted on the next frame.
/// </remarks>
[RegisterComponent, Access(typeof(KsFoamBlanketSystem))]
public sealed partial class KsFoamBlanketPlaceholderComponent : Component
{
    /// <summary>
    ///     When it is removed if the server's blanket has not replaced it by then, on the client's clock. Read in
    ///         <c>FrameUpdate</c>, after the frame's ticks, where that clock only moves forward.
    /// </summary>
    [ViewVariables]
    public TimeSpan ExpiryTime;

    /// <summary>
    ///     Whether the server's blanket has taken its tile over, so it is only waiting to be deleted.
    /// </summary>
    [ViewVariables]
    public bool Superseded;
}
