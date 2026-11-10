namespace Content.Client._KS14.PredictedSpawning;

/// <summary>
///     Something this client put on a tile itself, ahead of the server, for its player's own predicted action: a foam
///         blanket or a puddle a kept vapor laid. Client-only, on a client-side entity that is not an engine predicted
///         spawn, so no state reset deletes it. It is kept until the server's own entity for its tile arrives and takes
///         over, or until <see cref="ExpiryTime"/> if that never happens.
/// </summary>
/// <remarks>
///     A kept copy is never re-predicted (see <c>KsSharedPredictedSpawnSystem</c>), so neither is anything it spawns:
///         an engine predicted spawn of its would be deleted by the next state and never made again. Until it is
///         superseded, a placeholder is the real thing on this client, and does what one does. Once superseded, it is
///         hidden at once and deleted on the next frame, out of state application.
/// </remarks>
[RegisterComponent, Access(typeof(KsTilePlaceholderSystem))]
public sealed partial class KsTilePlaceholderComponent : Component
{
    /// <summary>
    ///     When it is removed if the server's entity has not replaced it by then, on the client's clock. Read in
    ///         <c>FrameUpdate</c>, after the frame's ticks, where that clock only moves forward.
    /// </summary>
    [ViewVariables]
    public TimeSpan ExpiryTime;

    /// <summary>
    ///     Whether the server's entity has taken its tile over, so it is only waiting to be deleted.
    /// </summary>
    [ViewVariables]
    public bool Superseded;
}
