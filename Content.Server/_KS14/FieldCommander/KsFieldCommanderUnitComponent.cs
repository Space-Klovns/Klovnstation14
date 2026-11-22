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
    ///     Whether it is the failed commander himself (<see cref="KsFieldCommanderRuleComponent.Mobik"/>), sent as
    ///         that rather than as one of the assets: <see cref="AssetIndex"/> means nothing then.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Mobik;

    /// <summary>
    ///     Index into <see cref="KsFieldCommanderMobik.Aftermath"/> of the step that sent it, or -1. With
    ///         <see cref="AftermathGroup"/>, it says what it was sent as: <see cref="AssetIndex"/> is then its index in
    ///         that group's units.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int AftermathStep = -1;

    /// <summary>
    ///     Index into that step's <see cref="KsFieldCommanderAftermathStep.Groups"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int AftermathGroup;

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

    /// <summary>
    ///     Put somewhere to stay there, such as an uplink's guard: never taken off the field for being stuck, as where
    ///         it would be sent from is not where it belongs, and what it cost was never the waves' to have back.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool HoldPosition;

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

    /// <summary>
    ///     When its steering last gave up on finding a path, if it has since the last check. Kept here because the move
    ///         that failed takes its steering away with it, long before the check comes round.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan? NoPathAt;

    /// <summary>
    ///     Where its last move ended in range, and how far from there that range was: a unit told to get within six tiles
    ///         of something has got there at six tiles, not at <see cref="KsFieldCommanderRuleComponent.StuckArrivedDistance"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public MapCoordinates? ArrivedCoordinates;

    /// <inheritdoc cref="ArrivedCoordinates"/>
    [ViewVariables(VVAccess.ReadOnly)]
    public float ArrivedRange;

    /// <summary>
    ///     Whether the log already says it is being held back by a defender watching, so it is said once, not every check.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool HeldBackLogged;

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
