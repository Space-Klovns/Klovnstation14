namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     Broadcast when what <see cref="KsMaterialRequestSystem.GetSiloReport"/> would report has changed: the silo's
///         contents, or what the lathes have asked for.
/// </summary>
[ByRefEvent]
public readonly record struct KsSiloReportChangedEvent;
