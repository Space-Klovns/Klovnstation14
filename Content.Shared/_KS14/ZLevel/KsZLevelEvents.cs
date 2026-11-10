namespace Content.Shared._KS14.ZLevel;

/// <summary>
///     Raised on a z-level whose <see cref="KsZLevelComponent.Depth"/> has just changed.
/// </summary>
/// <remarks>
///     This exists because anything that has to react to a depth change lives behind a different component's
///         <see cref="AccessAttribute"/> than the one that owns Depth - transiting entities most of all, whose
///         height is stored as a fraction of it.
/// </remarks>
[ByRefEvent]
public readonly record struct KsZLevelDepthChangedEvent(float PreviousDepth, float Depth);
