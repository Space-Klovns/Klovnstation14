namespace Content.Shared._KS14.Sensors;

/// <summary>
///     Marks an entity (typically a wall) as contributing to its grid's radar
///         cross-section, the quantity active radar detects. Mirrors
///         <see cref="KsThermalSourceComponent"/> exactly, but as an INDEPENDENT
///         value: RCS is summed by the same exposed-sides crawler
///         (<see cref="Content.Server._KS14.Sensors.KsSensorIntelSystem"/>) yet
///         carries its own per-wall figure, so a hull can be radar-bright while
///         running thermally cold. Stealth and heat stay orthogonal ship properties.
/// </summary>
[RegisterComponent]
public sealed partial class KsRadarSourceComponent : Component
{
    /// <summary>Radar return reflected per exposed side/corner, in RCS signature units.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Signature = 1f;
}
