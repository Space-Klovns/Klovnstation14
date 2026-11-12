namespace Content.Shared._KS14.Vendors;

/// <summary>
///     Broadcast after an automated vendor takes points from a buyer, for anything that keeps those points elsewhere
///         to follow the purchase.
/// </summary>
/// <param name="Actor">Who bought.</param>
/// <param name="PointsType">The vendor's points type, or null for the buyer's ordinary points.</param>
/// <param name="Remaining">The buyer's points of that type after paying.</param>
[ByRefEvent]
public readonly record struct KsVendorPointsSpentEvent(EntityUid Actor, string? PointsType, int Remaining);
