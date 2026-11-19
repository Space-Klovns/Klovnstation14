using Robust.Shared.Map;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     A unit sent by a field commander, counted towards how many it has alive and pointed at the defenders.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsFieldCommanderRuleSystem))]
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

    /// <summary>
    ///     Whether it goes at its target like any other unit, whatever its asset's stand-off: a mortar crewman that has lost
    ///         its mortar.
    /// </summary>
    [ViewVariables]
    public bool IgnoreStandOff;

    /// <summary>
    ///     Whether its kill has been counted and paid: by dying, or by being destroyed, whichever came first. A unit
    ///         brought back to life is a new kill again.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Killed;

    #region Stuck

    /// <summary>
    ///     Where the commander last pointed it, if it takes orders.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityCoordinates? OrderCoordinates;

    /// <summary>
    ///     What its progress was last measured against: where it is steering to, or else its order.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public MapCoordinates? GoalCoordinates;

    /// <summary>
    ///     Where it stood at the last check.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public MapCoordinates? CheckedCoordinates;

    /// <summary>
    ///     The nearest it has come to <see cref="GoalCoordinates"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float BestDistance = float.MaxValue;

    /// <summary>
    ///     When it last got anywhere, or was busy with something that excuses standing still.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan LastProgressTime;

    /// <summary>
    ///     When it was last hurt, which counts as being in a fight. Null if never.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan? LastDamagedTime;

    /// <summary>
    ///     How far through the recovery it is.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public KsFieldCommanderStuckStage StuckStage;

    /// <summary>
    ///     Why it was last found stuck.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public KsFieldCommanderStuckReason StuckReason;

    /// <summary>
    ///     When its nudge was first held back because a defender could see it or where it would go; tried again at
    ///         every check until it is done. Null when no nudge is waiting.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan? NudgeHeldSince;

    #endregion
}

/// <summary>
///     The steps taken with a unit that gets nowhere, each after it has been stuck longer.
/// </summary>
public enum KsFieldCommanderStuckStage : byte
{
    /// <summary>
    ///     Getting somewhere.
    /// </summary>
    None,

    /// <summary>
    ///     Ordered somewhere else, its path dropped, and told to plan again.
    /// </summary>
    Reorder,

    /// <summary>
    ///     Its steering thrown away, and moved onto a free tile beside it out of the defenders' sight.
    /// </summary>
    Nudge,

    /// <summary>
    ///     Taken off the field out of the defenders' sight and sent again from elsewhere, or refunded if there is
    ///         nowhere to send it.
    /// </summary>
    Relocate,
}

public enum KsFieldCommanderStuckReason : byte
{
    None,

    /// <summary>
    ///     It came no nearer where it was going.
    /// </summary>
    NoProgress,

    /// <summary>
    ///     Its steering found no path there.
    /// </summary>
    NoPath,
}
