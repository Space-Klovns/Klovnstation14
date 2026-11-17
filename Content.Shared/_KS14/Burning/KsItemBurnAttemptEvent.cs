namespace Content.Shared._KS14.Burning;

/// <summary>
///     Raised on a burning item as it catches fire, and again before every tick of burning. Cancel it to put the
///         item out, as standing water does.
/// </summary>
[ByRefEvent]
public record struct KsItemBurnAttemptEvent(bool Cancelled = false);
