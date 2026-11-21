namespace Content.Server._KS14.NPC.Ladders;

/// <summary>
///     On an NPC whose steering destination is on another map: the ladder it is making for to get there.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(KsNpcLadderSystem))]
public sealed partial class KsNpcLadderRouteComponent : Component
{
    /// <summary>
    ///     The ladder on its own map that it is making for, if a way was found.
    /// </summary>
    [ViewVariables]
    public EntityUid? LadderUid;

    /// <summary>
    ///     The map <see cref="LadderUid"/> was picked to get to.
    /// </summary>
    [ViewVariables]
    public EntityUid DestinationMapUid;

    /// <summary>
    ///     After a search that found no way, when it may look again.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextSearch;

    /// <summary>
    ///     It stands still at the ladder until this: a climb that takes time is under way.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan ClimbingUntil;
}
