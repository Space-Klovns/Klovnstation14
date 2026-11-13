namespace Content.Shared._KS14.Construction;

/// <summary>
///     Raised on a flatpack as it's unpacked, before it's deleted.
/// </summary>
/// <param name="Unpacked">What came out of it.</param>
/// <param name="User">Who unpacked it.</param>
[ByRefEvent]
public readonly record struct KsFlatpackUnpackedEvent(EntityUid Unpacked, EntityUid User);
