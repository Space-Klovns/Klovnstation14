namespace Content.Client._KS14.Fluids;

/// <summary>
///     What this client's own vapor has spilled into a puddle the server sent it, ahead of the server's spill. Only
///         drawn: the puddle's solution is the server's, and changing it here would be reverted by the next state and
///         redone by the one after, so the puddle would flicker between sizes. Added to the volume the puddle is drawn
///         at until a state shows the server's spill has landed, or until <see cref="ExpiryTime"/>.
/// </summary>
[RegisterComponent, Access(typeof(KsPredictedSpillSystem))]
public sealed partial class KsPredictedPuddleTopUpComponent : Component
{
    /// <summary>
    ///     What was spilled here and is not yet seen in the server's volume, as a fraction of the puddle's overflow
    ///         volume, as <c>PuddleVisuals.CurrentVolume</c> is.
    /// </summary>
    [ViewVariables]
    public float PendingVolume;

    /// <summary>
    ///     The server's drawn volume when the first of them was spilled. Once the server's volume has grown by half of
    ///         <see cref="PendingVolume"/> or more, its spill is taken to have landed: a puddle also evaporates and
    ///         spreads meanwhile, so it is never exact.
    /// </summary>
    [ViewVariables]
    public float BaselineVolume;

    /// <summary>
    ///     When it is dropped if the server's spill has not landed by then, on the client's clock.
    /// </summary>
    [ViewVariables]
    public TimeSpan ExpiryTime;
}
