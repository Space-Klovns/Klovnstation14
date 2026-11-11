namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     A unit sent by a field commander, counted towards how many it has alive and pointed at the defenders.
/// </summary>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderUnitComponent : Component
{
    /// <summary>
    ///     The rule that sent it.
    /// </summary>
    [ViewVariables]
    public EntityUid RuleUid;

    /// <summary>
    ///     Index into <see cref="KsFieldCommanderRuleComponent.Assets"/> of what it was bought as, or -1 when it was
    ///         not bought from that list.
    /// </summary>
    [ViewVariables]
    public int AssetIndex = -1;

    /// <summary>
    ///     Whether the commander keeps pointing it at the defenders.
    /// </summary>
    [ViewVariables]
    public bool GiveOrders = true;
}

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
