namespace Content.Shared._KS14.Chain;

/// <summary>
///     Raised on a chain link when a link adjacent to it was broken.
/// </summary>
/// <param name="BrokenEntity">The link that had broken.</param>
[ByRefEvent]
public record struct ChainSegmentedEvent(Entity<ChainLinkComponent> BrokenEntity);

/// <summary>
///     Raised on a chain edge the first time the chain has been segmented/broken.
/// </summary>
/// <param name="BrokenEntity">The link that had broken.</param>
[ByRefEvent]
public record struct ChainInitiallyBrokenEvent(Entity<ChainLinkComponent> BrokenEntity);
