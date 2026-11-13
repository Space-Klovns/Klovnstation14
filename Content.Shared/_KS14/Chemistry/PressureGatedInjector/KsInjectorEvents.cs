using Content.Shared.FixedPoint;

namespace Content.Shared._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     Raised on an injector when it is about to be used on a mob, for how long that takes.
/// </summary>
[ByRefEvent]
public record struct KsGetInjectorMobDelayEvent(EntityUid User, EntityUid Target, TimeSpan Delay);

/// <summary>
///     Raised on an injector as it injects, for how much of the planned amount gets in, and whether what is left in the
///         injector is lost.
/// </summary>
[ByRefEvent]
public record struct KsGetInjectorAmountEvent(FixedPoint2 Amount, bool DiscardRest = false);
