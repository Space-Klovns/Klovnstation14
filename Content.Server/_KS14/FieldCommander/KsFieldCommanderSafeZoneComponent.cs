namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Marks the middle of a zone that a field commander never sends anything into, strikes or orders its units
///         towards. Placed on a map, these replace the zones it would otherwise find around the defenders' spawn
///         points.
/// </summary>
[RegisterComponent]
public sealed partial class KsFieldCommanderSafeZoneComponent : Component
{
    /// <summary>
    ///     Overrides <see cref="KsFieldCommanderRuleComponent.SafeZoneRadius"/> for this zone, when set.
    /// </summary>
    [DataField]
    public float? Radius;
}
