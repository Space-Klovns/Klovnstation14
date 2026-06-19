using Content.Shared.Atmos.Components;

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Raised on something to process whether it should lose integrity or not.
/// </summary>
[ByRefEvent]
public record struct KsGasMaxPressureAttemptLoseIntegrityEvent(bool Cancelled, IGasMaxPressureHolder Component);

[ByRefEvent]
public record struct KsGasMaxPressureAfterIntegrityLostEvent(IGasMaxPressureHolder Component);
