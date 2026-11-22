namespace Content.Server._KS14.NPC.Passages;

/// <summary>
///     On an NPC whose steering destination is on another map: the passage it is making for to get there.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(KsNpcPassageSystem))]
public sealed partial class KsNpcPassageRouteComponent : Component
{
    /// <summary>
    ///     The passage on its own map that it is making for, if a way was found.
    /// </summary>
    [ViewVariables]
    public EntityUid? PassageUid;

    /// <summary>
    ///     The map <see cref="PassageUid"/> was picked to get to.
    /// </summary>
    [ViewVariables]
    public EntityUid DestinationMapUid;

    /// <summary>
    ///     After a search that found no way, when it may look again.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextSearch;

    /// <summary>
    ///     It stands still at the passage until this: taking it takes time, as climbing a ladder may.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan WaitingUntil;
}
