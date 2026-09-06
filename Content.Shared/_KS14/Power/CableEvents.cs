using Content.Shared.Power;

namespace Content.Shared._KS14.Power;

/// <summary>
///     Raised right before someone tries to cut a cable.
/// </summary>
[ByRefEvent]
public record struct AttemptCutCableEvent(CableType CableType, EntityUid UserUid, bool Cancelled);
