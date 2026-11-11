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
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid RuleUid;

    /// <summary>
    ///     Index into <see cref="KsFieldCommanderRuleComponent.Assets"/> of what it was bought as.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int AssetIndex;

    /// <summary>
    ///     Whether the commander keeps pointing it at the defenders.
    /// </summary>
    [ViewVariables]
    public bool GiveOrders = true;
}
