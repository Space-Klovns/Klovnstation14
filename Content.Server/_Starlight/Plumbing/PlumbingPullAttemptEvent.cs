using Robust.Shared.GameObjects;

namespace Content.Server._Starlight.Plumbing;

/// <summary>
///     Raised on the SOURCE entity when a machine attempts to pull a specific reagent.
///     Allows for cancelling the pull of specific reagents when subscribed to.
/// </summary>
[ByRefEvent]
public struct PlumbingPullAttemptEvent
{
    /// <summary>
    ///     The entity attempting to pull reagents.
    /// </summary>
    public EntityUid Puller;

    /// <summary>
    ///     The name of the node being pulled from
    /// </summary>
    public string NodeName;

    /// <summary>
    ///     The reagent prototype ID being checked.
    /// </summary>
    public string ReagentPrototype;

    /// <summary>
    ///     Set to true to deny pulling this reagent.
    /// </summary>
    public bool Cancelled;

    public PlumbingPullAttemptEvent(EntityUid puller, string nodeName, string reagentPrototype)
    {
        Puller = puller;
        NodeName = nodeName;
        ReagentPrototype = reagentPrototype;
        Cancelled = false;
    }
}
