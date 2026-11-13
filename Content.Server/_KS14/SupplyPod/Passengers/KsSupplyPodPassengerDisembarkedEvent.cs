namespace Content.Server._KS14.SupplyPod.Passengers;

/// <summary>
///     Raised on an NPC when the supply pod it rode in lets it out, awake.
/// </summary>
[ByRefEvent]
public readonly record struct KsSupplyPodPassengerDisembarkedEvent(EntityUid PodUid);
