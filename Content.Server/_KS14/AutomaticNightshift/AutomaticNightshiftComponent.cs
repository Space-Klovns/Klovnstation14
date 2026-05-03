using Robust.Shared.Prototypes;

namespace Content.Server._KS14.AutomaticNightshift;

/// <summary>
///     Automatically starts nightshift at a given time.
/// </summary>
[RegisterComponent]
public sealed partial class AutomaticNightshiftComponent : Component
{
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId Proto = "";

    /// <summary>
    ///     Fraction of the current lightcycle-time at which
    ///         this rule will be spawned.
    ///
    ///     Must be between 0 and 1.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float StartTimeFraction = 0.8f;

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextAllowedStart = TimeSpan.MinValue;
}
