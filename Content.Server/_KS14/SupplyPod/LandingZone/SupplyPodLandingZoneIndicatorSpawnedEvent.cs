namespace Content.Server._KS14.SupplyPod.LandingZone;

/// <summary>
///     Raised on a landing zone indicator once it has been spawned for a falling pod and its countdown started, so
///         the indicator can fit itself to what is landing.
/// </summary>
/// <param name="PodUid">The pod coming down on it.</param>
[ByRefEvent]
public readonly record struct SupplyPodLandingZoneIndicatorSpawnedEvent(EntityUid PodUid);
