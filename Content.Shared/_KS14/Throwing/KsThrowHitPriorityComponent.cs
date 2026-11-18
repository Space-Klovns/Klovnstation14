namespace Content.Shared._KS14.Throwing;

/// <summary>
///     When something thrown hits several things in the same tick, they are hit in order of priority, highest first:
///         a window before the grille under it. Without this, things are hit in the order physics happens to find
///         them, which matters to whatever only counts its first hit.
/// </summary>
[RegisterComponent]
public sealed partial class KsThrowHitPriorityComponent : Component
{
    /// <summary>
    ///     Anything without this component has priority 0.
    /// </summary>
    [DataField]
    public int Priority = 1;
}
