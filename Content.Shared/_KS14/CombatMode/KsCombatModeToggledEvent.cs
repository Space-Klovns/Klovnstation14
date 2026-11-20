namespace Content.Shared._KS14.CombatMode;

/// <summary>
///     Raised on whoever toggled their own combat mode from its action, after the change.
/// </summary>
[ByRefEvent]
public readonly record struct KsCombatModeToggledEvent(bool Enabled);
