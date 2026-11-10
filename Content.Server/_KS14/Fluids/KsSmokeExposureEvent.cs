using Content.Shared.Chemistry.Components;

namespace Content.Server._KS14.Fluids;

/// <summary>
///     Raised on a mob standing in smoke that carries something, just before the smoke gets into it. Whether it breathes
///         any in is decided after this, by whether its internals are working, so a handler can still put them on.
/// </summary>
/// <param name="SmokeUid">The smoke.</param>
/// <param name="Solution">What the smoke carries. Not to be changed.</param>
[ByRefEvent]
public readonly record struct KsSmokeExposureEvent(EntityUid SmokeUid, Solution Solution);
